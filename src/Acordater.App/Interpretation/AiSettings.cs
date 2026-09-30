namespace Acordater.App.Interpretation;

/// <summary>
/// The chosen AI provider and model (app preferences) and one API key per provider (SecureStorage, encrypted by
/// the Android keystore). Keys are never logged.
/// </summary>
public sealed class AiSettings(IPreferences preferences, ISecureStorage secureStorage)
{
	const string ProviderKey = "ai_provider";

	public AiProvider Provider
	{
		get => Enum.TryParse<AiProvider>(preferences.Get(ProviderKey, nameof(AiProvider.None)), out var provider) ? provider : AiProvider.None;
		set => preferences.Set(ProviderKey, value.ToString());
	}

	/// <summary>The model the user typed for a provider, or "" for the default.</summary>
	public string GetModel(AiProvider provider) => preferences.Get(ModelKey(provider), "");

	public void SetModel(AiProvider provider, string? model) => preferences.Set(ModelKey(provider), model?.Trim() ?? "");

	public async Task<string> GetApiKeyAsync(AiProvider provider) =>
		provider == AiProvider.None ? "" : await secureStorage.GetAsync(ApiKeyKey(provider)) ?? "";

	public async Task SetApiKeyAsync(AiProvider provider, string? apiKey)
	{
		if (provider == AiProvider.None) return;

		if (string.IsNullOrWhiteSpace(apiKey))
			secureStorage.Remove(ApiKeyKey(provider));
		else
			await secureStorage.SetAsync(ApiKeyKey(provider), apiKey.Trim());
	}

	static string ModelKey(AiProvider provider) => $"ai_model_{provider}";

	static string ApiKeyKey(AiProvider provider) => $"ai_api_key_{provider}";
}
