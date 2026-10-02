using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Acordater.Core.WakeWord;

/// <summary>
/// Streaming wake word detection with openWakeWord models (https://github.com/dscripka/openWakeWord), a C# port of its
/// Python streaming pipeline: 16 kHz audio → melspectrogram model → shared embedding model → wake word model.
/// Audio is processed in 80 ms chunks (1280 samples); each chunk yields one score between 0 and 1.
/// <para>
/// To save battery, the models do not run during silence (docs/spec.md, section 4.7): after a stretch of quiet
/// chunks the detector goes idle and repeats the last (silent) state instead, which keeps its buffers aligned in time.
/// The last few chunks are kept, so when sound comes back they are processed for real and the start of the phrase
/// is not lost.
/// </para>
/// Not thread-safe: feed it from one thread.
/// </summary>
public sealed class OpenWakeWordDetector : IDisposable
{
    public const int SampleRate = 16000;

    /// <summary>80 ms: the step of the pipeline, and a good read size for the microphone.</summary>
    public const int ChunkSamples = 1280;

    /// <summary>
    /// Below openWakeWord's usual 0.5: real speech at a distance scored 0.34–0.49 on a Pixel, while synthesized
    /// unrelated speech scores ~0 (near rhymes such as "hey Travis" reach ~0.4).
    /// </summary>
    public const float DefaultThreshold = 0.35f;

    /// <summary>RMS level (16-bit) under which a chunk counts as silence. A quiet room measured ~70 on a Pixel 8 Pro.</summary>
    public const int DefaultQuietLevel = 150;

    // Melspectrogram frames overlap: each chunk is computed with 3 hops (160 samples) of the previous audio as context.
    const int MelContextSamples = 160 * 3;
    const int MelBins = 32;
    const int MelFramesPerChunk = 8;
    // The embedding model looks at 76 melspectrogram frames (~0.8 s); a chunk adds 8 frames.
    const int EmbeddingWindow = 76;
    const int EmbeddingSize = 96;
    // The Python implementation reports 0 for the first predictions while its buffers fill.
    const int IgnoredPredictions = 5;
    // Chunks kept while idle and processed for real when sound comes back (240 ms).
    const int PreRollChunks = 3;

    readonly InferenceSession melspectrogram;
    readonly InferenceSession embedding;
    readonly InferenceSession wakeWord;
    readonly string melInput, embeddingInput, wakeWordInput;
    readonly int featureFrames;
    readonly int quietLevel;

    // Last samples of the previous chunk (melspectrogram context) followed by the current chunk.
    readonly short[] audio = new short[MelContextSamples + ChunkSamples];
    int contextSamples;
    // Samples of an incomplete chunk, waiting for the rest.
    readonly short[] pending = new short[ChunkSamples];
    int pendingSamples;

    readonly List<float[]> melFrames = [];
    readonly List<float[]> features = [];
    readonly Queue<short[]> preRoll = new();
    int predictions;
    int quietChunks;
    bool idle;

    /// <param name="melspectrogramModel">openWakeWord's melspectrogram.onnx.</param>
    /// <param name="embeddingModel">openWakeWord's embedding_model.onnx.</param>
    /// <param name="wakeWordModel">A wake word model (e.g. hey_jarvis_v0.1.onnx or a custom trained one).</param>
    /// <param name="quietLevel">RMS level under which audio counts as silence; 0 never goes idle.</param>
    public OpenWakeWordDetector(byte[] melspectrogramModel, byte[] embeddingModel, byte[] wakeWordModel,
        float threshold = DefaultThreshold, int quietLevel = DefaultQuietLevel)
    {
        // One thread each: the models are small and battery matters more than latency here.
        using var options = new SessionOptions
        {
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        melspectrogram = new InferenceSession(melspectrogramModel, options);
        embedding = new InferenceSession(embeddingModel, options);
        wakeWord = new InferenceSession(wakeWordModel, options);
        melInput = melspectrogram.InputMetadata.Keys.First();
        embeddingInput = embedding.InputMetadata.Keys.First();
        wakeWordInput = wakeWord.InputMetadata.Keys.First();

        // Wake word models take [1, frames, 96]; most use 16 frames (1.28 s).
        var shape = wakeWord.InputMetadata[wakeWordInput].Dimensions;
        featureFrames = shape.Length == 3 && shape[1] > 0 ? shape[1] : 16;
        Threshold = threshold;
        this.quietLevel = quietLevel;
        Reset();
    }

    public float Threshold { get; }

    /// <summary>Score of the last complete chunk (0 to 1), for diagnostics.</summary>
    public float LastScore { get; private set; }

    /// <summary>Chunks that went through the models (the rest were skipped as silence), for diagnostics.</summary>
    public long InferredChunks { get; private set; }

    /// <summary>
    /// Feeds 16 kHz mono 16-bit audio of any length. Returns true when a chunk completed by it scores at or above
    /// <see cref="Threshold"/>.
    /// </summary>
    public bool Process(ReadOnlySpan<short> samples)
    {
        var detected = false;
        while (!samples.IsEmpty)
        {
            var take = Math.Min(ChunkSamples - pendingSamples, samples.Length);
            samples[..take].CopyTo(pending.AsSpan(pendingSamples));
            pendingSamples += take;
            samples = samples[take..];

            if (pendingSamples < ChunkSamples) break;
            pendingSamples = 0;
            LastScore = OnChunk(pending);
            detected |= LastScore >= Threshold;
        }
        return detected;
    }

    /// <summary>Forgets the audio heard so far, e.g. after pausing the microphone.</summary>
    public void Reset()
    {
        contextSamples = 0;
        pendingSamples = 0;
        melFrames.Clear();
        // Like the Python implementation: start from a neutral melspectrogram so the first embeddings can be computed.
        for (var i = 0; i < EmbeddingWindow; i++) melFrames.Add(Enumerable.Repeat(1f, MelBins).ToArray());
        features.Clear();
        preRoll.Clear();
        predictions = 0;
        quietChunks = 0;
        idle = false;
        LastScore = 0;
    }

    float OnChunk(short[] chunk)
    {
        var quiet = quietLevel > 0 && Rms(chunk) < quietLevel;
        if (!idle)
        {
            var score = Infer(chunk);
            quietChunks = quiet ? quietChunks + 1 : 0;
            // Idle only once every feature in the window comes from silence, so repeating the last one is faithful.
            idle = features.Count == featureFrames && quietChunks > featureFrames;
            return score;
        }

        if (quiet)
        {
            preRoll.Enqueue((short[])chunk.Clone());
            if (preRoll.Count > PreRollChunks) Skip(preRoll.Dequeue());
            return 0;
        }

        // Sound again: catch up on the kept chunks, in order, then this one.
        idle = false;
        quietChunks = 0;
        var best = 0f;
        while (preRoll.Count > 0) best = Math.Max(best, Infer(preRoll.Dequeue()));
        return Math.Max(best, Infer(chunk));
    }

    /// <summary>Runs the models on a chunk and returns its score.</summary>
    float Infer(short[] chunk)
    {
        InferredChunks++;
        chunk.CopyTo(audio, contextSamples);
        var length = contextSamples + ChunkSamples;

        AddMelFrames(audio.AsSpan(0, length));
        AddFeatures();
        KeepContext(length);

        if (features.Count < featureFrames) return 0;
        var score = Predict();
        return ++predictions <= IgnoredPredictions ? 0 : score;
    }

    /// <summary>A silent chunk while idle: advances the buffers with the last (silent) values instead of running the models.</summary>
    void Skip(short[] chunk)
    {
        chunk.CopyTo(audio, contextSamples);
        KeepContext(contextSamples + ChunkSamples);

        var lastFrame = melFrames[^1];
        for (var i = 0; i < MelFramesPerChunk; i++) melFrames.Add(lastFrame);
        melFrames.RemoveRange(0, melFrames.Count - EmbeddingWindow);
        features.Add(features[^1]);
        features.RemoveAt(0);
    }

    // Keep the end of this chunk as context for the next one.
    void KeepContext(int length)
    {
        audio.AsSpan(length - MelContextSamples, MelContextSamples).CopyTo(audio);
        contextSamples = MelContextSamples;
    }

    void AddMelFrames(ReadOnlySpan<short> samples)
    {
        // The model takes the raw 16-bit values as floats.
        var input = new DenseTensor<float>([1, samples.Length]);
        var span = input.Buffer.Span;
        for (var i = 0; i < samples.Length; i++) span[i] = samples[i];

        using var results = melspectrogram.Run([NamedOnnxValue.CreateFromTensor(melInput, input)]);
        var values = results[0].AsTensor<float>().ToArray(); // [1, 1, frames, 32]
        var frames = values.Length / MelBins;
        for (var f = 0; f < frames; f++)
        {
            var frame = new float[MelBins];
            for (var b = 0; b < MelBins; b++)
                frame[b] = values[f * MelBins + b] / 10 + 2; // openWakeWord's transform to match Google's model
            melFrames.Add(frame);
        }

        if (melFrames.Count > EmbeddingWindow) melFrames.RemoveRange(0, melFrames.Count - EmbeddingWindow);
    }

    void AddFeatures()
    {
        var input = new DenseTensor<float>([1, EmbeddingWindow, MelBins, 1]);
        var span = input.Buffer.Span;
        for (var f = 0; f < EmbeddingWindow; f++)
            melFrames[f].CopyTo(span.Slice(f * MelBins, MelBins));

        using var results = embedding.Run([NamedOnnxValue.CreateFromTensor(embeddingInput, input)]);
        features.Add(results[0].AsTensor<float>().ToArray()); // [1, 1, 1, 96]

        if (features.Count > featureFrames) features.RemoveAt(0);
    }

    float Predict()
    {
        var input = new DenseTensor<float>([1, featureFrames, EmbeddingSize]);
        var span = input.Buffer.Span;
        for (var f = 0; f < featureFrames; f++)
            features[f].CopyTo(span.Slice(f * EmbeddingSize, EmbeddingSize));

        using var results = wakeWord.Run([NamedOnnxValue.CreateFromTensor(wakeWordInput, input)]);
        return results[0].AsTensor<float>().GetValue(0);
    }

    static double Rms(short[] chunk)
    {
        double sum = 0;
        foreach (var sample in chunk) sum += sample * (double)sample;
        return Math.Sqrt(sum / chunk.Length);
    }

    public void Dispose()
    {
        melspectrogram.Dispose();
        embedding.Dispose();
        wakeWord.Dispose();
    }
}
