using System.Text.Json;
using ANDS.RulesEngine;

namespace ANDS.RulesEngine.Tests;

public sealed class JsonSerializationTests
{
    [Fact]
    public void Condition_round_trips_through_json()
    {
        var condition = new ConditionGroup
        {
            Group = ConditionGroupType.All,
            Conditions = new Condition[]
            {
                TestSupport.Comparison("age", ComparisonOperator.GreaterThan, 18),
                new ConditionGroup
                {
                    Group = ConditionGroupType.Any,
                    Conditions = new[] { TestSupport.Comparison("name", ComparisonOperator.StartsWith, "A") }
                }
            }
        };
        var options = RuleJsonSerializer.CreateOptions();
        var copy = JsonSerializer.Deserialize<Condition>(JsonSerializer.Serialize<Condition>(condition, options), options);
        var copyGroup = Assert.IsType<ConditionGroup>(copy);
        Assert.Equal(ConditionGroupType.All, copyGroup.Group);
        Assert.Equal(2, copyGroup.Conditions.Count);
        Assert.Equal(condition.Conditions.Select(child => child.GetType()),
            copyGroup.Conditions.Select(child => child.GetType()));
        Assert.Equal(condition.Conditions.OfType<ComparisonCondition>().Single().Field,
            copyGroup.Conditions.OfType<ComparisonCondition>().Single().Field);
    }

    [Theory]
    [InlineData("""{"kind":"group","group":"all","conditions":[]}""", typeof(ConditionGroup))]
    [InlineData("""{"kind":"leaf","field":"x","operator":"equal","value":1}""", typeof(ComparisonCondition))]
    public void Kind_and_leaf_aliases_are_supported(string json, Type expectedType)
    {
        var condition = JsonSerializer.Deserialize<Condition>(json, RuleJsonSerializer.CreateOptions());
        Assert.IsType(expectedType, condition);
    }

    [Theory]
    [InlineData("""{"type":"other"}""", "Unknown condition type")]
    [InlineData("""{"type":"group","group":"all"}""", "conditions")]
    [InlineData("""{"type":"comparison","field":"x","operator":"wat","value":1}""", "Unknown comparison operator")]
    public void Invalid_condition_json_has_actionable_errors(string json, string expectedMessage)
    {
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Condition>(json, RuleJsonSerializer.CreateOptions()));
        Assert.Contains(expectedMessage, exception.Message);
    }

    [Fact]
    public void Integer_enum_values_are_rejected()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Condition>(
            """{"type":"comparison","field":"x","operator":1,"value":1}""",
            RuleJsonSerializer.CreateOptions()));
    }
}
