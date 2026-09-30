namespace Acordater.App.Voice;

/// <summary>
/// Wake word settings: on/off (preferences, off by default), the Picovoice AccessKey (SecureStorage) and the
/// optional custom model imported by the user (files in the app data directory, so no rebuild is needed).
/// Without a custom model the built-in keyword <see cref="BuiltInPhrase"/> is used, to test the whole flow.
/// </summary>
public sealed class WakeWordSettings(IPreferences preferences, ISecureStorage secureStorage)
{
	/// <summary>Porcupine's built-in keyword used until a custom model is imported (English model, no .pv needed).</summary>
	public const string BuiltInPhrase = "Jarvis";

	const string EnabledKey = "wake_word_enabled";
	const string PhraseKey = "wake_word_phrase";
	const string ParametersNameKey = "wake_word_parameters_name";
	const string AccessKeyKey = "picovoice_access_key";

	static string Directory => Path.Combine(FileSystem.AppDataDirectory, "wakeword");

	public static string KeywordFile => Path.Combine(Directory, "keyword.ppn");

	public static string ParametersFile => Path.Combine(Directory, "parameters.pv");

	public bool Enabled
	{
		get => preferences.Get(EnabledKey, false);
		set => preferences.Set(EnabledKey, value);
	}

	public bool HasCustomKeyword => File.Exists(KeywordFile);

	public bool HasCustomParameters => File.Exists(ParametersFile);

	/// <summary>What to say: the custom model's phrase (from its file name) or <see cref="BuiltInPhrase"/>.</summary>
	public string Phrase => HasCustomKeyword ? preferences.Get(PhraseKey, BuiltInPhrase) : BuiltInPhrase;

	/// <summary>File name of the imported language parameters, or null when the built-in English ones are used.</summary>
	public string? ParametersName => HasCustomParameters ? preferences.Get(ParametersNameKey, "") : null;

	public async Task<string> GetAccessKeyAsync() => await secureStorage.GetAsync(AccessKeyKey) ?? "";

	public async Task SetAccessKeyAsync(string? accessKey)
	{
		if (string.IsNullOrWhiteSpace(accessKey))
			secureStorage.Remove(AccessKeyKey);
		else
			await secureStorage.SetAsync(AccessKeyKey, accessKey.Trim());
	}

	/// <summary>Copies a keyword model (.ppn) from Picovoice Console, e.g. "Hey-Cordie_es_android_v4_0_0.ppn".</summary>
	public async Task ImportKeywordAsync(Stream source, string fileName)
	{
		await CopyAsync(source, KeywordFile);
		preferences.Set(PhraseKey, PhraseFromFileName(fileName));
	}

	/// <summary>Copies the language parameters (.pv) a non-English keyword needs, e.g. "porcupine_params_es.pv".</summary>
	public async Task ImportParametersAsync(Stream source, string fileName)
	{
		await CopyAsync(source, ParametersFile);
		preferences.Set(ParametersNameKey, fileName);
	}

	/// <summary>Back to the built-in keyword and English parameters.</summary>
	public void RemoveCustomModel()
	{
		File.Delete(KeywordFile);
		File.Delete(ParametersFile);
		preferences.Remove(PhraseKey);
		preferences.Remove(ParametersNameKey);
	}

	// Console names files "<Phrase-With-Dashes>_<language>_<platform>_<version>.ppn".
	internal static string PhraseFromFileName(string fileName)
	{
		var name = Path.GetFileNameWithoutExtension(fileName);
		var underscore = name.IndexOf('_');
		var phrase = (underscore > 0 ? name[..underscore] : name).Replace('-', ' ').Trim();
		return phrase.Length > 0 ? phrase : BuiltInPhrase;
	}

	static async Task CopyAsync(Stream source, string destination)
	{
		System.IO.Directory.CreateDirectory(Directory);
		await using var target = File.Create(destination);
		await source.CopyToAsync(target);
	}
}
