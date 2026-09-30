using Acordater.App.Resources.Strings;
using Acordater.Core.Interpretation;

namespace Acordater.App.Interpretation;

/// <summary>
/// The interpreter the app uses: the AI provider chosen in Settings, or the offline rules when none is chosen.
/// Settings are read on every call, so a change applies to the next phrase without restarting.
/// </summary>
public sealed class ConfiguredInterpreter(AiSettings settings, TimeProvider time) : IReminderInterpreter
{
	readonly RuleBasedInterpreter rules = new(time);
	(AiProvider Provider, string Model, string ApiKey, ChatInterpreter Interpreter)? cached;

	public async Task<InterpretedReminder> InterpretAsync(string utterance, CancellationToken cancellationToken = default)
	{
		var provider = settings.Provider;
		if (provider == AiProvider.None) return rules.Interpret(utterance);

		var apiKey = await settings.GetApiKeyAsync(provider);
		if (apiKey.Length == 0)
		{
			return rules.Interpret(utterance) with
			{
				Failure = new InterpretationFailure(AiProviders.DisplayName(provider), InterpretationFailureKind.ProviderError, AppResources.AiKeyMissing),
			};
		}

		var model = settings.GetModel(provider);
		if (cached is not { } c || c.Provider != provider || c.Model != model || c.ApiKey != apiKey)
			cached = c = (provider, model, apiKey, AiProviders.CreateInterpreter(provider, apiKey, model, time)!);

		var result = await c.Interpreter.InterpretAsync(utterance, cancellationToken);
		if (result.Failure is { } failure)
			System.Diagnostics.Debug.WriteLine($"AI interpretation fell back to rules: {failure.Provider} {failure.Kind}");
		return result;
	}
}
