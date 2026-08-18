using ANDS.RulesEngine;

namespace ANDS.RulesEngine.Tests;

public sealed class ValidationTests
{
    [Fact]
    public void Validate_aggregates_multiple_errors()
    {
        var exception = Assert.Throws<RuleValidationException>(() => new Rule
        {
            Id = "",
            Name = "",
            Condition = new ComparisonCondition { Field = "", Operator = ComparisonOperator.GreaterThan },
            Actions = new[] { new RuleAction { Type = "" } }
        }.Validate());
        Assert.Contains("Id is required", exception.Message);
        Assert.Contains("Name is required", exception.Message);
        Assert.Contains("Comparison field is required", exception.Message);
        Assert.Contains("requires a value", exception.Message);
        Assert.Contains("Actions[0].Type is required", exception.Message);
    }

    [Fact]
    public void Validate_requires_condition()
    {
        var exception = Assert.Throws<RuleValidationException>(() => new Rule
        {
            Id = "id",
            Name = "name",
            Actions = []
        }.Validate());
        Assert.Contains("Condition is required", exception.Message);
    }

    [Fact]
    public void Validate_requires_id()
    {
        var exception = Assert.Throws<RuleValidationException>(() => new Rule
        {
            Name = "name",
            Condition = TestSupport.Comparison("value", ComparisonOperator.IsNull),
            Actions = []
        }.Validate());
        Assert.Contains("Id is required", exception.Message);
    }

    [Fact]
    public void Validate_requires_name()
    {
        var exception = Assert.Throws<RuleValidationException>(() => new Rule
        {
            Id = "id",
            Condition = TestSupport.Comparison("value", ComparisonOperator.IsNull),
            Actions = []
        }.Validate());
        Assert.Contains("Name is required", exception.Message);
    }

    [Fact]
    public void Validate_requires_actions()
    {
        var rule = new Rule
        {
            Id = "id",
            Name = "name",
            Condition = TestSupport.Comparison("value", ComparisonOperator.IsNull),
            Actions = null!
        };
        var exception = Assert.Throws<RuleValidationException>(rule.Validate);
        Assert.Contains("Actions is required", exception.Message);
    }

    [Fact]
    public void Validate_rejects_empty_action_type()
    {
        var exception = Assert.Throws<RuleValidationException>(() => TestSupport.RuleFor(
            TestSupport.Comparison("value", ComparisonOperator.Equal, 1),
            actions: new[] { new RuleAction { Type = "" } }).Validate());
        Assert.Contains("Actions[0].Type is required", exception.Message);
    }

    [Fact]
    public void Validation_exception_formats_rule_id_when_present()
    {
        Assert.Equal("Rule 'r1': bad", new RuleValidationException("r1", "bad").Message);
        Assert.Equal("bad", new RuleValidationException(null, "bad").Message);
        Assert.Null(new RuleValidationException(null, "bad").RuleId);
    }

    [Fact]
    public void Equal_and_not_equal_allow_null_values()
    {
        var rule = TestSupport.RuleFor(TestSupport.Comparison("value", ComparisonOperator.Equal));
        rule.Validate();
    }
}
