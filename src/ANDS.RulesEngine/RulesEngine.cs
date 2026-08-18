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
        IReadOnlyList<RuleError> errors, TimeSpan duration)
    {
        MatchedRules = matchedRules;
        ExecutedActions = executedActions;
        Errors = errors;
        Duration = duration;
    }

    public IReadOnlyList<Rule> MatchedRules { get; }
    public IReadOnlyList<ExecutedAction> ExecutedActions { get; }
    public IReadOnlyList<RuleError> Errors { get; }
    public TimeSpan Duration { get; }
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
        _handlers = (handlers ?? Array.Empty<IRuleActionHandler>())
            .ToDictionary(handler => handler.ActionType, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<RuleEvaluationResult> EvaluateAsync(IEnumerable<Rule> rules, object? facts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var matched = new List<Rule>();
        var executed = new List<ExecutedAction>();
        var errors = new List<RuleError>();
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
                if (!ConditionEvaluator.Evaluate(rule.Condition!, context,
                        new RuleEvaluationOptions { StringCaseSensitive = _options.StringCaseSensitive }))
                    continue;

                matched.Add(rule);
                for (var index = 0; index < rule.Actions.Count; index++)
                {
                    var action = rule.Actions[index];
                    if (!_handlers.TryGetValue(action.Type, out var handler))
                    {
                        var exception = new UnknownActionException(
                            $"No handler registered for action type '{action.Type}'.");
                        if (_options.UnknownActionBehavior == UnknownActionBehavior.Throw)
                            throw exception;
                        errors.Add(new RuleError(rule.Id, exception.Message, exception));
                        continue;
                    }
                    await handler.HandleAsync(action, new RuleActionContext(rule, context), cancellationToken);
                    executed.Add(new ExecutedAction(rule.Id, action.Type, index));
                }

                if (_options.StopOnFirstMatch)
                    break;
            }
            catch (UnknownActionException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var error = new RuleError(rule.Id, exception.Message, exception);
                errors.Add(error);
                if (_options.RuleErrorBehavior == RuleErrorBehavior.Abort)
                    break;
            }
        }

        stopwatch.Stop();
        return new RuleEvaluationResult(matched, executed, errors, stopwatch.Elapsed);
    }
}

internal sealed class UnknownActionException : InvalidOperationException
{
    public UnknownActionException(string message) : base(message) { }
}
