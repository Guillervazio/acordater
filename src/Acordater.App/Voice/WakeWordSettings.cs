using System.Globalization;
using System.Text.RegularExpressions;
using Acordater.Core.WakeWord;

namespace Acordater.App.Voice;

/// <summary>
/// Wake word settings: on/off (preferences, off by default), the detection threshold and the optional custom openWakeWord model imported by
/// the user (a file in the app data directory, so no rebuild is needed). Without a custom model the bundled
/// "hey Jarvis" model is used, to test the whole flow.
/// </summary>
public sealed partial class WakeWordSettings(IPreferences preferences)
{
	public const string BuiltInPhrase = "Hey Jarvis";

	// Bundled openWakeWord models (Resources/Raw/wakeword): the shared feature models and the built-in keyword.
	public const string MelspectrogramModel = "wakeword/melspectrogram.onnx";
	public const string EmbeddingModel = "wakeword/embedding_model.onnx";
	public const string BuiltInKeywordModel = "wakeword/hey_jarvis_v0.1.onnx";

	const string EnabledKey = "wake_word_enabled";
	const string PhraseKey = "wake_word_phrase";
	const string ThresholdKey = "wake_word_threshold";

	static string Directory => Path.Combine(FileSystem.AppDataDirectory, "wakeword");

	public static string KeywordFile => Path.Combine(Directory, "keyword.onnx");

	public bool Enabled
	{
		get => preferences.Get(EnabledKey, false);
		set => preferences.Set(EnabledKey, value);
	}

	/// <summary>Score (0 to 1) from which the detector fires: lower detects more easily but also other words.</summary>
	public float Threshold
	{
		get => preferences.Get(ThresholdKey, OpenWakeWordDetector.DefaultThreshold);
		set => preferences.Set(ThresholdKey, value);
	}

	public bool HasCustomKeyword => File.Exists(KeywordFile);

	/// <summary>What to say: the custom model's phrase (from its file name) or <see cref="BuiltInPhrase"/>.</summary>
	public string Phrase => HasCustomKeyword ? preferences.Get(PhraseKey, BuiltInPhrase) : BuiltInPhrase;

	/// <summary>Copies a model trained with openWakeWord (.onnx), e.g. "hey_cordie.onnx".</summary>
	public async Task ImportKeywordAsync(Stream source, string fileName)
	{
		System.IO.Directory.CreateDirectory(Directory);
		await using (var target = File.Create(KeywordFile))
			await source.CopyToAsync(target);
		preferences.Set(PhraseKey, PhraseFromFileName(fileName));
	}

	/// <summary>Back to the built-in keyword.</summary>
	public void RemoveCustomModel()
	{
		File.Delete(KeywordFile);
		preferences.Remove(PhraseKey);
	}

	// Training names files after the phrase, e.g. "hey_cordie.onnx" or "hey_cordie_v0.1.onnx".
	static string PhraseFromFileName(string fileName)
	{
		var name = VersionSuffix().Replace(Path.GetFileNameWithoutExtension(fileName), "");
		var phrase = name.Replace('_', ' ').Replace('-', ' ').Trim();
		return phrase.Length > 0 ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(phrase) : BuiltInPhrase;
	}

	[GeneratedRegex(@"_v\d+(\.\d+)*$")]
	private static partial Regex VersionSuffix();
}
