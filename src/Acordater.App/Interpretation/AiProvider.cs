using System.ClientModel;
using Acordater.Core.Interpretation;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Acordater.App.Interpretation;

/// <summary>LLM providers the user can pick for interpretation (docs/spec.md, section 5), each with the user's own API key.</summary>
public enum AiProvider
{
	/// <summary>Offline rules only (the default).</summary>
	None,
	Claude,
	OpenAI,
	Gemini,
}

public static class AiProviders
{
	/// <summary>Providers offered in Settings, in display order. Gemini Nano is not offered: see docs/spec.md, section 5.</summary>
	public static readonly IReadOnlyList<AiProvider> All = [AiProvider.None, AiProvider.Claude, AiProvider.OpenAI, AiProvider.Gemini];

	// Cheap and fast by default: the voice flow waits for the answer (ChatInterpreter.DefaultTimeout).
	public static string DefaultModel(AiProvider provider) => provider switch
	{
		AiProvider.Claude => "claude-haiku-4-5",
		AiProvider.OpenAI => "gpt-6-luna",
		AiProvider.Gemini => "gemini-3.5-flash-lite",
		_ => "",
	};

	public static string DisplayName(AiProvider provider) => provider switch
	{
		AiProvider.Claude => "Claude",
		AiProvider.OpenAI => "OpenAI",
		AiProvider.Gemini => "Gemini",
		_ => RuleBasedInterpreter.Name,
	};

	const int MaxOutputTokens = 1024;

	// Gemini's OpenAI-compatible endpoint: same client as OpenAI, no extra SDK (and its auth dependencies) in the app.
	static readonly Uri GeminiEndpoint = new("https://generativelanguage.googleapis.com/v1beta/openai/");

	/// <summary>An interpreter for the provider, or null for <see cref="AiProvider.None"/>.</summary>
	/// <param name="model">Null or empty for <see cref="DefaultModel"/>.</param>
	public static ChatInterpreter? CreateInterpreter(AiProvider provider, string apiKey, string? model, TimeProvider time)
	{
		var modelId = string.IsNullOrWhiteSpace(model) ? DefaultModel(provider) : model.Trim();
		var options = new ChatOptions { ModelId = modelId, MaxOutputTokens = MaxOutputTokens };

		IChatClient? chat = provider switch
		{
			// No retries: the interpreter's short timeout bounds the whole call and the rules are the fallback.
			AiProvider.Claude => new AnthropicClient { ApiKey = apiKey, MaxRetries = 0 }.AsIChatClient(modelId, MaxOutputTokens),
			AiProvider.OpenAI => OpenAIChat(modelId, apiKey, endpoint: null),
			AiProvider.Gemini => OpenAIChat(modelId, apiKey, GeminiEndpoint),
			_ => null,
		};
		if (chat is null) return null;

		if (provider is AiProvider.OpenAI or AiProvider.Gemini)
			options.ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.Json;
		// The default OpenAI model reasons at medium effort unless told otherwise, which is too slow for the voice flow.
		// Not sent for a model the user chose, since models without reasoning reject the parameter.
		if (provider is AiProvider.OpenAI && modelId == DefaultModel(provider))
			options.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None };

		return new ChatInterpreter(chat, DisplayName(provider), time, System.Globalization.CultureInfo.CurrentUICulture, options);
	}

	static IChatClient OpenAIChat(string model, string apiKey, Uri? endpoint)
	{
		var options = new OpenAIClientOptions { RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(maxRetries: 0) };
		if (endpoint is not null) options.Endpoint = endpoint;
		return new ChatClient(model, new ApiKeyCredential(apiKey), options).AsIChatClient();
	}
}
