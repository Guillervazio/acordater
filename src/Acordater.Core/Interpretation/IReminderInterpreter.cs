namespace Acordater.Core.Interpretation;

/// <summary>
/// Turns what the user said into a reminder. Implementations: the offline <see cref="RuleBasedInterpreter"/>
/// (always available, and the fallback) and, later, LLM providers using the user's own API key.
/// </summary>
public interface IReminderInterpreter
{
    Task<InterpretedReminder> InterpretAsync(string utterance, CancellationToken cancellationToken = default);
}

/// <param name="Text">What to remember, without the trigger words or time expression.</param>
/// <param name="RequestedAt">First alert the user asked for, or null to use the default.</param>
public sealed record InterpretedReminder(string Text, DateTimeOffset? RequestedAt);
