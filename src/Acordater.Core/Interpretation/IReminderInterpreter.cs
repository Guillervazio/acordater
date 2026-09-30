namespace Acordater.Core.Interpretation;

/// <summary>
/// Turns what the user said into a reminder. Implementations: the offline <see cref="RuleBasedInterpreter"/>
/// (always available, and the fallback) and <see cref="ChatInterpreter"/> for LLM providers using the user's own API key.
/// </summary>
public interface IReminderInterpreter
{
    Task<InterpretedReminder> InterpretAsync(string utterance, CancellationToken cancellationToken = default);
}

/// <param name="Text">What to remember, without the trigger words or time expression.</param>
/// <param name="RequestedAt">First alert the user asked for, or null to use the default.</param>
public sealed record InterpretedReminder(string Text, DateTimeOffset? RequestedAt)
{
    /// <summary>Who produced this result: <see cref="RuleBasedInterpreter.Name"/> or the AI provider's name.</summary>
    public string Interpreter { get; init; } = RuleBasedInterpreter.Name;

    /// <summary>Set when an AI provider was configured but failed, so the rules produced this result instead.</summary>
    public InterpretationFailure? Failure { get; init; }
}

/// <param name="Provider">The AI provider that failed.</param>
/// <param name="Detail">Technical detail (e.g. the provider's error message). Never contains the API key.</param>
public sealed record InterpretationFailure(string Provider, InterpretationFailureKind Kind, string Detail);

public enum InterpretationFailureKind
{
    /// <summary>No answer within the timeout.</summary>
    Timeout,

    /// <summary>No connection or the provider could not be reached.</summary>
    Network,

    /// <summary>The provider rejected the request (invalid API key, unknown model, quota, server error...).</summary>
    ProviderError,

    /// <summary>The answer was not the expected JSON or did not pass validation.</summary>
    InvalidResponse,
}
