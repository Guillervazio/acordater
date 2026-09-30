using System.Globalization;

namespace Acordater.App.Voice;

/// <summary>Speaks confirmations in the current UI language.</summary>
public sealed class VoiceFeedback(ITextToSpeech textToSpeech)
{
	Locale? locale;
	bool localeResolved;

	public async Task SpeakAsync(string text, CancellationToken cancellationToken)
	{
		try
		{
			await textToSpeech.SpeakAsync(text, new SpeechOptions { Locale = await LocaleAsync() }, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The text is on screen anyway; a missing or broken TTS engine must not block saving.
			System.Diagnostics.Debug.WriteLine($"TTS failed: {ex}");
		}
	}

	async Task<Locale?> LocaleAsync()
	{
		if (localeResolved) return locale;

		var culture = CultureInfo.CurrentUICulture;
		var sameLanguage = (await textToSpeech.GetLocalesAsync())
			.Where(l => string.Equals(l.Language, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
			.ToList();
		var region = culture.IsNeutralCulture ? null : new RegionInfo(culture.Name).TwoLetterISORegionName;
		locale = sameLanguage.FirstOrDefault(l => string.Equals(l.Country, region, StringComparison.OrdinalIgnoreCase))
			?? sameLanguage.FirstOrDefault();
		localeResolved = true;
		return locale;
	}
}
