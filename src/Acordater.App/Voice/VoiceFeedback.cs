using System.Globalization;

namespace Acordater.App.Voice;

/// <summary>Speaks confirmations in the language chosen for Acordater's voice (<see cref="LanguageSettings.VoiceCulture"/>).</summary>
public sealed class VoiceFeedback(ITextToSpeech textToSpeech)
{
	Locale? locale;
	string? localeResolvedFor;

	/// <param name="culture">Language of <paramref name="text"/>, which selects the voice.</param>
	public async Task SpeakAsync(string text, CultureInfo culture, CancellationToken cancellationToken)
	{
		try
		{
			await textToSpeech.SpeakAsync(text, new SpeechOptions { Locale = await LocaleAsync(culture) }, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The text is on screen anyway; a missing or broken TTS engine must not block saving.
			System.Diagnostics.Debug.WriteLine($"TTS failed: {ex}");
		}
	}

	async Task<Locale?> LocaleAsync(CultureInfo culture)
	{
		if (localeResolvedFor == culture.Name) return locale;

		var sameLanguage = (await textToSpeech.GetLocalesAsync())
			.Where(l => string.Equals(l.Language, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
			.ToList();
		var region = culture.IsNeutralCulture ? null : new RegionInfo(culture.Name).TwoLetterISORegionName;
		locale = sameLanguage.FirstOrDefault(l => string.Equals(l.Country, region, StringComparison.OrdinalIgnoreCase))
			?? sameLanguage.FirstOrDefault();
		localeResolvedFor = culture.Name;
		return locale;
	}
}
