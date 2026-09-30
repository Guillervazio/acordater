using System.Globalization;
using Acordater.App.Resources.Strings;
using Acordater.Core.Interpretation;

namespace Acordater.App.Interpretation;

/// <summary>User-facing text about which interpreter produced a result, including a fallback to the rules.</summary>
public static class InterpreterDescription
{
	/// <summary>E.g. "Interpretado con: Claude" or "Interpretado con: reglas (respaldo: Claude no respondió a tiempo)".</summary>
	public static string Describe(InterpretedReminder result)
	{
		var who = result switch
		{
			{ Failure: { } failure } => string.Format(CultureInfo.CurrentCulture, AppResources.InterpretedFallback, failure.Provider, Reason(failure.Kind)),
			{ Interpreter: RuleBasedInterpreter.Name } => AppResources.InterpretedByRules,
			_ => result.Interpreter,
		};
		return string.Format(CultureInfo.CurrentCulture, AppResources.InterpretedWith, who);
	}

	public static string Reason(InterpretationFailureKind kind) => kind switch
	{
		InterpretationFailureKind.Timeout => AppResources.FailureTimeout,
		InterpretationFailureKind.Network => AppResources.FailureNetwork,
		InterpretationFailureKind.InvalidResponse => AppResources.FailureInvalid,
		_ => AppResources.FailureProvider,
	};
}
