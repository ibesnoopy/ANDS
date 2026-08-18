using ANDS.RulesEngine;

namespace ANDS.RulesEngine.Tests;

public sealed class ConditionEvaluationTests
{
    public static TheoryData<ComparisonOperator, object, object> NumericTrueCases =>
        new()
        {
            { ComparisonOperator.Equal, 5, 5 },
            { ComparisonOperator.NotEqual, 5, 6 },
            { ComparisonOperator.GreaterThan, 6, 5 },
            { ComparisonOperator.GreaterThanOrEqual, 5, 5 },
            { ComparisonOperator.LessThan, 4, 5 },
            { ComparisonOperator.LessThanOrEqual, 5, 5 }
        };

    [Theory]
    [MemberData(nameof(NumericTrueCases))]
    public async Task Numeric_operators_match_expected_values(ComparisonOperator op, object actual, object expected)
    {
        var result = await Evaluate(TestSupport.Comparison("value", op, expected), actual);
        Assert.Single(result.MatchedRules);
    }

    public static TheoryData<ComparisonOperator, object, object> NumericFalseCases =>
        new()
        {
            { ComparisonOperator.Equal, 5, 6 },
            { ComparisonOperator.NotEqual, 5, 5 },
            { ComparisonOperator.GreaterThan, 5, 6 },
            { ComparisonOperator.GreaterThanOrEqual, 4, 5 },
            { ComparisonOperator.LessThan, 6, 5 },
            { ComparisonOperator.LessThanOrEqual, 6, 5 }
        };

    [Theory]
    [MemberData(nameof(NumericFalseCases))]
    public async Task Numeric_operators_return_false_when_comparison_is_false(
        ComparisonOperator op, object actual, object expected)
    {
        var result = await Evaluate(TestSupport.Comparison("value", op, expected), actual);
        Assert.Empty(result.MatchedRules);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(ComparisonOperator.Equal, "abc", "ABC", true)]
    [InlineData(ComparisonOperator.Equal, "abc", "def", false)]
    [InlineData(ComparisonOperator.NotEqual, "abc", "def", true)]
    [InlineData(ComparisonOperator.NotEqual, "abc", "ABC", false)]
    [InlineData(ComparisonOperator.GreaterThan, "b", "a", true)]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, "b", "b", true)]
    [InlineData(ComparisonOperator.LessThan, "a", "b", true)]
    [InlineData(ComparisonOperator.LessThanOrEqual, "b", "b", true)]
    [InlineData(ComparisonOperator.Contains, "Hello world", "WORLD", true)]
    [InlineData(ComparisonOperator.Contains, "Hello", "xyz", false)]
    [InlineData(ComparisonOperator.StartsWith, "Hello", "he", true)]
    [InlineData(ComparisonOperator.StartsWith, "Hello", "lo", false)]
    [InlineData(ComparisonOperator.EndsWith, "Hello", "LO", true)]
    [InlineData(ComparisonOperator.EndsWith, "Hello", "he", false)]
    [InlineData(ComparisonOperator.Matches, "ABC-123", "^[a-z]{3}-\\d{3}$", true)]
    [InlineData(ComparisonOperator.Matches, "ABC", "^\\d+$", false)]
    public async Task String_operators_cover_true_and_false_results(
        ComparisonOperator op, string actual, string expected, bool shouldMatch)
    {
        var result = await Evaluate(TestSupport.Comparison("value", op, expected), actual);
        Assert.Equal(shouldMatch, result.MatchedRules.Count == 1);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Boolean_equal_and_not_equal_are_type_aware(bool actual, bool expected, bool equal)
    {
        var equalResult = await Evaluate(TestSupport.Comparison("value", ComparisonOperator.Equal, expected), actual);
        var notEqualResult = await Evaluate(TestSupport.Comparison("value", ComparisonOperator.NotEqual, expected), actual);
        Assert.Equal(equal, equalResult.MatchedRules.Count == 1);
        Assert.Equal(!equal, notEqualResult.MatchedRules.Count == 1);
    }

    [Fact]
    public async Task DateTime_ordering_and_equality_support_DateTime_actuals()
    {
        var actual = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var before = "2024-01-01T00:00:00Z";
        var equal = "2024-02-01T00:00:00Z";
        var after = "2024-03-01T00:00:00Z";
        Assert.Single((await Evaluate(TestSupport.Comparison("value", ComparisonOperator.GreaterThan, before), actual)).MatchedRules);
        Assert.Single((await Evaluate(TestSupport.Comparison("value", ComparisonOperator.Equal, equal), actual)).MatchedRules);
        Assert.Single((await Evaluate(TestSupport.Comparison("value", ComparisonOperator.LessThan, after), actual)).MatchedRules);
        Assert.Empty((await Evaluate(TestSupport.Comparison("value", ComparisonOperator.GreaterThan, after), actual)).MatchedRules);
    }

    [Theory]
    [InlineData(ComparisonOperator.Contains, 42)]
    [InlineData(ComparisonOperator.StartsWith, 42)]
    [InlineData(ComparisonOperator.EndsWith, 42)]
    [InlineData(ComparisonOperator.Matches, 42)]
    public async Task Present_wrong_typed_actual_values_are_rule_errors(
        ComparisonOperator op, object actual)
    {
        var bad = TestSupport.RuleFor(TestSupport.Comparison("value", op, "x"), "bad");
        var good = TestSupport.RuleFor(TestSupport.Comparison("ok", ComparisonOperator.Equal, true), "good", 1);
        var result = await new RulesEngine(options: new RulesEngineOptions
        { RuleErrorBehavior = RuleErrorBehavior.Continue }).EvaluateAsync(new[] { bad, good },
            new Dictionary<string, object?> { ["value"] = actual, ["ok"] = true });
        Assert.Single(result.Errors);
        Assert.Equal("bad", result.Errors[0].RuleId);
        Assert.Contains("requires", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("good", Assert.Single(result.MatchedRules).Id);
    }

    [Theory]
    [InlineData(ComparisonOperator.GreaterThan)]
    [InlineData(ComparisonOperator.GreaterThanOrEqual)]
    [InlineData(ComparisonOperator.LessThan)]
    [InlineData(ComparisonOperator.LessThanOrEqual)]
    public async Task Ordering_with_wrong_typed_actual_value_is_a_rule_error(ComparisonOperator op)
    {
        var rule = TestSupport.RuleFor(TestSupport.Comparison("value", op, 1));
        var result = await new RulesEngine().EvaluateAsync(new[] { rule }, new { value = "not numeric" });
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task Contains_with_wrong_expected_type_is_a_rule_error()
    {
        var rule = TestSupport.RuleFor(TestSupport.Comparison("value", ComparisonOperator.Contains, 42));
        var result = await new RulesEngine().EvaluateAsync(new[] { rule }, new { value = "abc" });
        Assert.Single(result.Errors);
        Assert.Contains("string expected", result.Errors[0].Message);
    }

    [Fact]
    public async Task In_and_not_in_require_collection_expected_values()
    {
        foreach (var op in new[] { ComparisonOperator.In, ComparisonOperator.NotIn })
        {
            var rule = TestSupport.RuleFor(TestSupport.Comparison("value", op, "abc"));
            var result = await new RulesEngine().EvaluateAsync(new[] { rule }, new { value = "abc" });
            Assert.Single(result.Errors);
        }
    }

    [Fact]
    public async Task NotIn_on_missing_path_returns_false_without_error()
    {
        var rule = TestSupport.RuleFor(TestSupport.Comparison("missing", ComparisonOperator.NotIn, new[] { "x" }));
        var result = await new RulesEngine().EvaluateAsync(new[] { rule }, new { value = "abc" });
        Assert.Empty(result.MatchedRules);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task In_and_not_in_cover_true_and_false_membership()
    {
        var rules = new[]
        {
            TestSupport.RuleFor(TestSupport.Comparison("value", ComparisonOperator.In, new[] { "a", "b" }), "in"),
            TestSupport.RuleFor(TestSupport.Comparison("value", ComparisonOperator.NotIn, new[] { "x", "y" }), "not-in"),
            TestSupport.RuleFor(TestSupport.Comparison("value", ComparisonOperator.In, new[] { "x" }), "in-false"),
            TestSupport.RuleFor(TestSupport.Comparison("value", ComparisonOperator.NotIn, new[] { "a" }), "not-in-false")
        };
        var result = await new RulesEngine().EvaluateAsync(rules, new { value = "a" });
        Assert.Equal(new[] { "in", "not-in" }, result.MatchedRules.Select(rule => rule.Id));
    }

    [Fact]
    public async Task Nested_groups_evaluate_recursively()
    {
        var condition = new ConditionGroup
        {
            Group = ConditionGroupType.All,
            Conditions = new Condition[]
            {
                new ConditionGroup
                {
                    Group = ConditionGroupType.Any,
                    Conditions = new Condition[]
                    {
                        TestSupport.Comparison("a", ComparisonOperator.Equal, 1),
                        new ConditionGroup
                        {
                            Group = ConditionGroupType.None,
                            Conditions = new[] { TestSupport.Comparison("b", ComparisonOperator.Equal, 2) }
                        }
                    }
                },
                TestSupport.Comparison("c", ComparisonOperator.IsNotNull)
            }
        };
        var result = await new RulesEngine().EvaluateAsync(new[] { TestSupport.RuleFor(condition) },
            new { a = 0, b = 3, c = "present" });
        Assert.Single(result.MatchedRules);
    }

    private static Task<RuleEvaluationResult> Evaluate(Condition condition, object actual) =>
        new RulesEngine().EvaluateAsync(new[] { TestSupport.RuleFor(condition) },
            new Dictionary<string, object?> { ["value"] = actual });
}
