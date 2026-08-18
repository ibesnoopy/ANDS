using ANDS.RulesEngine;

namespace ANDS.RulesEngine.Tests;

public sealed class EngineTests
{
    [Fact]
    public async Task Rules_are_priority_ordered_with_id_tie_breaking_and_disabled_rules_skipped()
    {
        var rules = new[]
        {
            TestSupport.RuleFor(TestSupport.Comparison("ok", ComparisonOperator.Equal, true), "z", 1),
            TestSupport.RuleFor(TestSupport.Comparison("ok", ComparisonOperator.Equal, true), "a", 1),
            TestSupport.RuleFor(TestSupport.Comparison("ok", ComparisonOperator.Equal, true), "disabled", 0) with { Enabled = false }
        };
        var result = await new RulesEngine().EvaluateAsync(rules, new { ok = true });
        Assert.Equal(new[] { "a", "z" }, result.MatchedRules.Select(rule => rule.Id));
    }

    [Fact]
    public async Task Stop_on_first_match_and_evaluate_all_have_distinct_results()
    {
        var rules = new[]
        {
            TestSupport.RuleFor(TestSupport.Comparison("ok", ComparisonOperator.Equal, true), "first", 1),
            TestSupport.RuleFor(TestSupport.Comparison("ok", ComparisonOperator.Equal, true), "second", 2)
        };
        var first = await new RulesEngine(options: new RulesEngineOptions { StopOnFirstMatch = true })
            .EvaluateAsync(rules, new { ok = true });
        var all = await new RulesEngine().EvaluateAsync(rules, new { ok = true });
        Assert.Single(first.MatchedRules);
        Assert.Equal(2, all.MatchedRules.Count);
    }

    [Fact]
    public async Task Handler_action_type_lookup_is_case_insensitive_and_actions_execute_in_order()
    {
        var handler = new RecordingHandler("Notify");
        var actions = new[]
        {
            new RuleAction { Type = "notify", Parameters = new Dictionary<string, object?> { ["n"] = 1 } },
            new RuleAction { Type = "NOTIFY", Parameters = new Dictionary<string, object?> { ["n"] = 2 } }
        };
        var result = await new RulesEngine(new[] { handler }).EvaluateAsync(new[]
        {
            TestSupport.RuleFor(TestSupport.Comparison("ok", ComparisonOperator.Equal, true), actions: actions)
        }, new { ok = true });
        Assert.Equal(new[] { 1, 2 }, handler.Actions.Select(action => (int)action.Parameters["n"]!));
        Assert.Equal(new[] { 0, 1 }, result.ExecutedActions.Select(action => action.Index));
    }

    [Fact]
    public async Task Unknown_action_record_error_exposes_action_and_rule()
    {
        var result = await new RulesEngine(options: new RulesEngineOptions
        { UnknownActionBehavior = UnknownActionBehavior.RecordError }).EvaluateAsync(new[]
        {
            TestSupport.RuleFor(TestSupport.Comparison("ok", ComparisonOperator.Equal, true), "rule",
                actions: new[] { new RuleAction { Type = "unknown" } })
        }, new { ok = true });
        Assert.Single(result.Errors);
        Assert.Contains("unknown", result.Errors[0].Message);
    }

    [Fact]
    public async Task Unknown_action_throw_exposes_action_and_rule_to_callers()
    {
        var exception = await Assert.ThrowsAsync<UnknownActionException>(() =>
            new RulesEngine().EvaluateAsync(new[]
            {
                TestSupport.RuleFor(TestSupport.Comparison("ok", ComparisonOperator.Equal, true), "rule",
                    actions: new[] { new RuleAction { Type = "unknown" } })
            }, new { ok = true }));
        Assert.Equal("unknown", exception.ActionType);
        Assert.Equal("rule", exception.RuleId);
    }

    [Fact]
    public async Task Rule_errors_can_abort_or_continue()
    {
        var bad = TestSupport.RuleFor(TestSupport.Comparison("value", ComparisonOperator.Contains, 1), "bad");
        var good = TestSupport.RuleFor(TestSupport.Comparison("ok", ComparisonOperator.Equal, true), "good", 1);
        var continued = await new RulesEngine(options: new RulesEngineOptions
        { RuleErrorBehavior = RuleErrorBehavior.Continue }).EvaluateAsync(new[] { bad, good },
            new { value = 42, ok = true });
        var aborted = await new RulesEngine().EvaluateAsync(new[] { bad, good },
            new { value = 42, ok = true });
        Assert.Equal("good", Assert.Single(continued.MatchedRules).Id);
        Assert.Empty(aborted.MatchedRules);
        Assert.Single(continued.Errors);
        Assert.Single(aborted.Errors);
    }

    [Fact]
    public async Task Cancellation_token_is_honored()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new RulesEngine().EvaluateAsync(new[] { TestSupport.RuleFor(
                TestSupport.Comparison("ok", ComparisonOperator.Equal, true)) },
                new { ok = true }, cancellation.Token));
    }

    [Fact]
    public async Task Duration_is_populated()
    {
        var result = await new RulesEngine().EvaluateAsync(new[] { TestSupport.RuleFor(
            TestSupport.Comparison("ok", ComparisonOperator.Equal, true)) }, new { ok = true });
        Assert.True(result.Duration >= TimeSpan.Zero);
    }
}
