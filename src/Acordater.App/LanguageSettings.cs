using System.Globalization;

namespace Acordater.App;

/// <summary>Language chosen in Settings (docs/spec.md, section 4.5).</summary>
public enum AppLanguage
{
	/// <summary>Follow the phone (the default).</summary>
	Phone,
	Spanish,
	English,
	/// <summary>App in the phone's language; dictation switches between Spanish and English as it hears them.</summary>
	Bilingual,
}

/// <summary>Language Acordater speaks in (spoken confirmations and the alarm reading).</summary>
public enum VoiceLanguage
{
	/// <summary>The language of the app (the default).</summary>
	SameAsApp,
	Spanish,
	English,
}

/// <summary>
/// The language of the app and the dictation, and separately the one Acordater speaks in. Stored in preferences
/// and applied at startup; the user interface changes the next time the app starts, the rest right away.
/// </summary>
public sealed class LanguageSettings(IPreferences preferences)
{
	const string Key = "app_language";
	const string VoiceKey = "voice_language";

	// Spanish and English variants used for dictation and speech. es-ES: the app's users live in Spain.
	public const string SpanishTag = "es-ES";
	public const string EnglishTag = "en-US";

	/// <summary>The phone's own culture, captured before the app overrides it.</summary>
	static CultureInfo? phoneCulture;

	public static readonly IReadOnlyList<AppLanguage> All = [AppLanguage.Phone, AppLanguage.Spanish, AppLanguage.English, AppLanguage.Bilingual];

	public static readonly IReadOnlyList<VoiceLanguage> AllVoices = [VoiceLanguage.SameAsApp, VoiceLanguage.Spanish, VoiceLanguage.English];

	public AppLanguage Language
	{
		get => Enum.TryParse<AppLanguage>(preferences.Get(Key, nameof(AppLanguage.Phone)), out var language) ? language : AppLanguage.Phone;
		set => preferences.Set(Key, value.ToString());
	}

	static CultureInfo PhoneCulture => phoneCulture ??= CultureInfo.CurrentUICulture;

	public VoiceLanguage Voice
	{
		get => Enum.TryParse<VoiceLanguage>(preferences.Get(VoiceKey, nameof(VoiceLanguage.SameAsApp)), out var voice) ? voice : VoiceLanguage.SameAsApp;
		set => preferences.Set(VoiceKey, value.ToString());
	}

	/// <summary>Culture Acordater speaks in: its sentences and the text to speech voice.</summary>
	public CultureInfo VoiceCulture => Voice switch
	{
		VoiceLanguage.Spanish => new CultureInfo(SpanishTag),
		VoiceLanguage.English => new CultureInfo(EnglishTag),
		_ => Culture,
	};

	/// <summary>Culture of the user interface.</summary>
	public CultureInfo Culture => Language switch
	{
		AppLanguage.Spanish => new CultureInfo(SpanishTag),
		AppLanguage.English => new CultureInfo(EnglishTag),
		_ => PhoneCulture,
	};

	/// <summary>Main dictation language (a BCP 47 tag).</summary>
	public string DictationLanguage => Language switch
	{
		AppLanguage.Spanish => SpanishTag,
		AppLanguage.English => EnglishTag,
		// Start in the phone's language when it is one of the two, else in Spanish.
		AppLanguage.Bilingual => PhoneCulture.TwoLetterISOLanguageName == "en" ? EnglishTag : SpanishTag,
		_ => PhoneCulture.Name,
	};

	/// <summary>Languages dictation may switch between, or null for a single language.</summary>
	public IReadOnlyList<string>? DictationSwitchLanguages =>
		Language == AppLanguage.Bilingual ? [SpanishTag, EnglishTag] : null;

	/// <summary>Applies the saved language to .NET (and, on Android, to Java) before any page is created.</summary>
	public void Apply()
	{
		_ = PhoneCulture;
		var culture = Culture;
		CultureInfo.DefaultThreadCurrentCulture = culture;
		CultureInfo.DefaultThreadCurrentUICulture = culture;
		CultureInfo.CurrentCulture = culture;
		CultureInfo.CurrentUICulture = culture;
#if ANDROID
		Java.Util.Locale.Default = Java.Util.Locale.ForLanguageTag(culture.Name);
#endif
	}
}
