using System.Diagnostics;

namespace ANDS.RulesEngine;

public enum UnknownActionBehavior
{
    Throw,
    RecordError
}

public enum RuleErrorBehavior
{
    Abort,
    Continue
}

public sealed class RulesEngineOptions
{
    public bool StopOnFirstMatch { get; init; }
    public RuleErrorBehavior RuleErrorBehavior { get; init; } = RuleErrorBehavior.Abort;
    public bool StringCaseSensitive { get; init; }
    public UnknownActionBehavior UnknownActionBehavior { get; init; } = UnknownActionBehavior.Throw;
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromSeconds(1);

    public void Validate()
    {
        if (RegexTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RegexTimeout), RegexTimeout,
                "RegexTimeout must be greater than zero.");
    }
}

public interface IRuleActionHandler
{
    string ActionType { get; }
    Task HandleAsync(RuleAction action, RuleActionContext context, CancellationToken cancellationToken);
}

public sealed class RuleActionContext
{
    internal RuleActionContext(Rule rule, IFactContext facts) => (Rule, Facts) = (rule, facts);
    public Rule Rule { get; }
    public IFactContext Facts { get; }
}

public sealed record RuleError(string RuleId, string Message, Exception Exception);
public sealed record ExecutedAction(string RuleId, string ActionType, int Index);

public sealed class RuleEvaluationResult
{
    internal RuleEvaluationResult(IReadOnlyList<Rule> matchedRules, IReadOnlyList<ExecutedAction> executedActions,
        IReadOnlyList<RuleError> errors, TimeSpan duration, bool aborted)
    {
        MatchedRules = matchedRules;
        ExecutedActions = executedActions;
        Errors = errors;
        Duration = duration;
        Aborted = aborted;
    }

    public IReadOnlyList<Rule> MatchedRules { get; }
    public IReadOnlyList<ExecutedAction> ExecutedActions { get; }
    public IReadOnlyList<RuleError> Errors { get; }
    public TimeSpan Duration { get; }

    /// <summary>True when evaluation stopped early because a rule error occurred.</summary>
    public bool Aborted { get; }

    public bool HasErrors => Errors.Count > 0;

    /// <summary>Throws the recorded rule errors so callers cannot ignore them by accident.</summary>
    public void ThrowIfErrors()
    {
        if (Errors.Count == 0)
            return;
        if (Errors.Count == 1)
            throw new RuleEvaluationException(Errors[0].Message, Errors);
        throw new RuleEvaluationException(
            $"{Errors.Count} rule errors occurred: {string.Join(" ", Errors.Select(error => error.Message))}", Errors);
    }
}

public sealed class RuleEvaluationException : Exception
{
    public RuleEvaluationException(string message, IReadOnlyList<RuleError> errors)
        : base(message, errors.Count > 0 ? errors[0].Exception : null)
    {
        Errors = errors;
    }

    public IReadOnlyList<RuleError> Errors { get; } = Array.Empty<RuleError>();
}

public interface IRulesEngine
{
    Task<RuleEvaluationResult> EvaluateAsync(IEnumerable<Rule> rules, object? facts,
        CancellationToken cancellationToken = default);
}

public sealed class RulesEngine : IRulesEngine
{
    private readonly Dictionary<string, IRuleActionHandler> _handlers;
    private readonly RulesEngineOptions _options;

    public RulesEngine(IEnumerable<IRuleActionHandler>? handlers = null, RulesEngineOptions? options = null)
    {
        _options = options ?? new RulesEngineOptions();
        _handlers = (handlers ?? [])
            .ToDictionary(handler => handler.ActionType, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<RuleEvaluationResult> EvaluateAsync(IEnumerable<Rule> rules, object? facts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var matched = new List<Rule>();
        var executed = new List<ExecutedAction>();
        var errors = new List<RuleError>();
        var evaluationOptions = new RuleEvaluationOptions { StringCaseSensitive = _options.StringCaseSensitive };
        var stopwatch = Stopwatch.StartNew();
        var context = new FactContext(facts);
        var orderedRules = rules.Where(rule => rule.Enabled).OrderBy(rule => rule.Priority).ThenBy(rule => rule.Id,
            StringComparer.Ordinal);

        foreach (var rule in orderedRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                rule.Validate();
                if (!ConditionEvaluator.Evaluate(rule.Condition!, context, evaluationOptions))
                    continue;

                matched.Add(rule);
                for (var index = 0; index < rule.Actions.Count; index++)
                {
                    var action = rule.Actions[index];
                    if (!_handlers.TryGetValue(action.Type, out var handler))
                    {
                        var exception = new UnknownActionException(action.Type, rule.Id);
                        if (_options.UnknownActionBehavior == UnknownActionBehavior.Throw)
                            throw exception;
                        errors.Add(new RuleError(rule.Id, exception.Message, exception));
                        continue;
                    }
                    try
                    {
                        await handler.HandleAsync(action, new RuleActionContext(rule, context), cancellationToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException &&
                                                     !IsFatal(exception))
                    {
                        throw new RuleActionException(rule.Id, action.Type, index, exception);
                    }
                    executed.Add(new ExecutedAction(rule.Id, action.Type, index));
                }

                if (_options.StopOnFirstMatch)
                    break;
            }
            catch (UnknownActionException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException && !IsFatal(exception))
            {
                var error = new RuleError(rule.Id, exception.Message, exception);
                errors.Add(error);
                if (_options.RuleErrorBehavior == RuleErrorBehavior.Abort)
                {
                    aborted = true;
                    break;
                }
            }
        }

        stopwatch.Stop();
        return new RuleEvaluationResult(matched, executed, errors, stopwatch.Elapsed, aborted);
    }

    private static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException
            or BadImageFormatException or TypeInitializationException;
}

public sealed class RuleActionException : Exception
{
    public RuleActionException(string ruleId, string actionType, int index, Exception innerException)
        : base($"Action '{actionType}' at index {index} of rule '{ruleId}' failed: {innerException.Message}",
            innerException)
    {
        RuleId = ruleId;
        ActionType = actionType;
        Index = index;
    }

    public string RuleId { get; }
    public string ActionType { get; }
    public int Index { get; }
}

public class UnknownActionException : InvalidOperationException
{
    public UnknownActionException() { }

    public UnknownActionException(string? message) : base(message) { }

    public UnknownActionException(string? message, Exception? innerException)
        : base(message, innerException) { }

    public UnknownActionException(string actionType, string ruleId)
        : base($"No handler registered for action type '{actionType}' on rule '{ruleId}'.")
    {
        ActionType = actionType;
        RuleId = ruleId;
    }

    public string? ActionType { get; }
    public string? RuleId { get; }
}
