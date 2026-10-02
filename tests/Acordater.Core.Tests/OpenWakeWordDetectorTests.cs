using Acordater.Core.WakeWord;

namespace Acordater.Core.Tests;

// Runs the real openWakeWord models (bundled with the app) on audio synthesized with Windows text to speech.
public sealed class OpenWakeWordDetectorTests : IDisposable
{
    static readonly string Models = Path.Combine(AppContext.BaseDirectory, "wakeword");
    static readonly string Audio = Path.Combine(AppContext.BaseDirectory, "WakeWord");

    readonly OpenWakeWordDetector detector = new(
        File.ReadAllBytes(Path.Combine(Models, "melspectrogram.onnx")),
        File.ReadAllBytes(Path.Combine(Models, "embedding_model.onnx")),
        File.ReadAllBytes(Path.Combine(Models, "hey_jarvis_v0.1.onnx")));

    public void Dispose() => detector.Dispose();

    [Theory]
    [InlineData("hey_jarvis_zira.wav")]
    [InlineData("hey_jarvis_david.wav")]
    [InlineData("hey_jarvis_helena.wav")] // a Spanish voice
    public void DetectsTheWakeWord(string file)
    {
        Assert.True(Feed(WithSilence(ReadWav(file)), chunk: OpenWakeWordDetector.ChunkSamples));
    }

    // At the default threshold, everyday speech in both languages and phrases starting with "hey" stay silent.
    [Theory]
    [InlineData("negative_es.wav")]
    [InlineData("charla_helena.wav")]
    [InlineData("recordame_helena.wav")]
    [InlineData("hey_javier_helena.wav")]
    [InlineData("talk_david.wav")]
    [InlineData("hey_there_zira.wav")]
    [InlineData("hey_service_david.wav")]
    [InlineData("hey_harvey_david.wav")]
    [InlineData("harvest_david.wav")]
    public void IgnoresOtherSpeech(string file)
    {
        Assert.False(Feed(WithSilence(ReadWav(file)), chunk: OpenWakeWordDetector.ChunkSamples));
    }

    [Fact]
    public void IgnoresSilenceAndNoise()
    {
        var random = new Random(42);
        var noise = new short[OpenWakeWordDetector.SampleRate * 5];
        for (var i = 0; i < noise.Length; i++) noise[i] = (short)random.Next(-300, 300);

        Assert.False(Feed(new short[OpenWakeWordDetector.SampleRate * 3], chunk: OpenWakeWordDetector.ChunkSamples));
        Assert.False(Feed(noise, chunk: OpenWakeWordDetector.ChunkSamples));
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(3000)]
    [InlineData(160)]
    public void AnyReadSizeWorks(int chunk)
    {
        Assert.True(Feed(WithSilence(ReadWav("hey_jarvis_zira.wav")), chunk));
    }

    [Fact]
    public void ResetForgetsWhatWasHeard()
    {
        var clip = WithSilence(ReadWav("hey_jarvis_zira.wav"));
        Assert.True(Feed(clip, OpenWakeWordDetector.ChunkSamples));

        detector.Reset();

        Assert.Equal(0, detector.LastScore);
        Assert.True(Feed(clip, OpenWakeWordDetector.ChunkSamples));
    }

    [Fact]
    public void SkipsTheModelsDuringSilence()
    {
        var silence = new short[OpenWakeWordDetector.SampleRate * 10];

        Assert.False(Feed(silence, OpenWakeWordDetector.ChunkSamples));

        // 125 chunks: only the ones needed to fill the buffers with silence go through the models.
        Assert.InRange(detector.InferredChunks, 1, 20);
    }

    [Theory]
    [InlineData(1.0, 0)]
    [InlineData(0.3, 0)]   // a quieter voice
    [InlineData(1.0, 100)] // over a background noise below the silence level
    public void DetectsAfterALongSilence(double volume, int noise)
    {
        var random = new Random(7);
        var clip = ReadWav("hey_jarvis_zira.wav").Select(s => (short)(s * volume)).ToArray();
        short[] audio = [.. new short[OpenWakeWordDetector.SampleRate * 10], .. clip, .. new short[OpenWakeWordDetector.SampleRate]];
        for (var i = 0; i < audio.Length; i++)
            audio[i] = (short)Math.Clamp(audio[i] + random.Next(-noise, noise + 1), short.MinValue, short.MaxValue);

        Assert.True(Feed(audio, OpenWakeWordDetector.ChunkSamples));
    }

    bool Feed(short[] samples, int chunk)
    {
        var detected = false;
        for (var i = 0; i < samples.Length; i += chunk)
            detected |= detector.Process(samples.AsSpan(i, Math.Min(chunk, samples.Length - i)));
        return detected;
    }

    // One second of silence before (the pipeline warms up) and after (the score peaks after the phrase ends).
    static short[] WithSilence(short[] clip) =>
        [.. new short[OpenWakeWordDetector.SampleRate * 2], .. clip, .. new short[OpenWakeWordDetector.SampleRate]];

    /// <summary>16 kHz mono 16-bit PCM samples of a WAV file.</summary>
    static short[] ReadWav(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Audio, file));
        var position = 12; // after "RIFF", size, "WAVE"
        while (position + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, position, 4);
            var size = BitConverter.ToInt32(bytes, position + 4);
            if (id == "data")
            {
                var samples = new short[size / 2];
                Buffer.BlockCopy(bytes, position + 8, samples, 0, samples.Length * 2);
                return samples;
            }
            position += 8 + size + (size & 1);
        }
        throw new InvalidDataException($"{file} has no data chunk.");
    }
}
