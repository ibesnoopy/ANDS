using System.Data;
using System.Text.Json;
using ANDS.RulesEngine;

namespace ANDS.RulesEngine.Tests;

public sealed class RulesEngineTests
{
    [Theory]
    [InlineData(ComparisonOperator.Equal, 10, 10, true)]
    [InlineData(ComparisonOperator.NotEqual, 10, 11, true)]
    [InlineData(ComparisonOperator.GreaterThan, 11, 10, true)]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, 10, 10, true)]
    [InlineData(ComparisonOperator.LessThan, 9, 10, true)]
    [InlineData(ComparisonOperator.LessThanOrEqual, 10, 10, true)]
    [InlineData(ComparisonOperator.Contains, "hello world", "WORLD", true)]
    [InlineData(ComparisonOperator.StartsWith, "Hello", "he", true)]
    [InlineData(ComparisonOperator.EndsWith, "Hello", "LO", true)]
    [InlineData(ComparisonOperator.Matches, "ABC-123", "^[a-z]{3}-\\d{3}$", true)]
    public async Task Operators_evaluate_with_default_case_insensitive_comparison(
        ComparisonOperator comparisonOperator, object actual, object expected, bool expectedResult)
    {
        var rule = RuleFor(new ComparisonCondition { Field = "value", Operator = comparisonOperator, Value = expected });
        var result = await new RulesEngine(new[] { NoOpHandler("record") })
            .EvaluateAsync(new[] { rule }, new Dictionary<string, object?> { ["value"] = actual });
        Assert.Equal(expectedResult, result.MatchedRules.Count == 1);
    }

    [Fact]
    public async Task In_and_not_in_support_json_arrays()
    {
        var rules = new[]
        {
            RuleFor(new ComparisonCondition
            {
                Field = "status", Operator = ComparisonOperator.In,
                Value = new[] { "new", "approved" }
            }, id: "in"),
            RuleFor(new ComparisonCondition
            {
                Field = "status", Operator = ComparisonOperator.NotIn,
                Value = new[] { "closed", "cancelled" }
            }, id: "not-in")
        };
        var result = await new RulesEngine().EvaluateAsync(rules, new { status = "NEW" });
        Assert.Equal(new[] { "in", "not-in" }, result.MatchedRules.Select(rule => rule.Id));
    }

    [Fact]
    public async Task String_case_sensitivity_is_configurable()
    {
        var rule = RuleFor(new ComparisonCondition
        {
            Field = "text",
            Operator = ComparisonOperator.Equal,
            Value = "hello"
        });
        var facts = new { text = "HELLO" };
        Assert.Single((await new RulesEngine().EvaluateAsync(new[] { rule }, facts)).MatchedRules);
        Assert.Empty((await new RulesEngine(options: new RulesEngineOptions { StringCaseSensitive = true })
            .EvaluateAsync(new[] { rule }, facts)).MatchedRules);
    }

    [Fact]
    public async Task Numeric_and_date_strings_are_explicitly_coerced_but_bool_and_string_types_are_not()
    {
        var rules = new[]
        {
            RuleFor(new ComparisonCondition { Field = "number", Operator = ComparisonOperator.Equal, Value = "12.5" }, id: "number"),
            RuleFor(new ComparisonCondition
            {
                Field = "when", Operator = ComparisonOperator.GreaterThan,
                Value = "2024-01-01T00:00:00Z"
            }, id: "date"),
            RuleFor(new ComparisonCondition { Field = "flag", Operator = ComparisonOperator.Equal, Value = "true" }, id: "bool"),
            RuleFor(new ComparisonCondition { Field = "text", Operator = ComparisonOperator.Equal, Value = 12 }, id: "text")
        };
        var facts = new { number = 12.5m, when = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), flag = true, text = "12" };
        var result = await new RulesEngine().EvaluateAsync(rules, facts);
        Assert.Equal(new[] { "bool", "date", "number" }, result.MatchedRules.Select(rule => rule.Id));
    }

    [Fact]
    public async Task Missing_path_is_distinct_from_null_and_only_null_operators_match_as_expected()
    {
        var rules = new[]
        {
            RuleFor(new ComparisonCondition { Field = "missing", Operator = ComparisonOperator.IsNull, Value = null }, id: "missing-null"),
            RuleFor(new ComparisonCondition { Field = "missing", Operator = ComparisonOperator.IsNotNull, Value = null }, id: "missing-not-null"),
            RuleFor(new ComparisonCondition { Field = "present", Operator = ComparisonOperator.IsNull, Value = null }, id: "present-null"),
            RuleFor(new ComparisonCondition { Field = "present", Operator = ComparisonOperator.Equal, Value = null }, id: "present-equal")
        };
        var facts = new { present = (string?)null };
        var context = new FactContext(facts);
        Assert.True(context.TryGetValue("present", out var present));
        Assert.Null(present);
        var result = await new RulesEngine().EvaluateAsync(rules, facts);
        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "present-equal", "present-null" }, result.MatchedRules.Select(rule => rule.Id));
    }

    [Fact]
    public async Task Dotted_paths_support_pocos_dictionaries_and_indexers()
    {
        var facts = new
        {
            Order = new
            {
                Customer = new Dictionary<string, object?> { ["Age"] = 42 },
                Items = new[] { new { Sku = "ABC" } }
            }
        };
        var rules = new[]
        {
            RuleFor(new ComparisonCondition { Field = "order.customer.age", Operator = ComparisonOperator.Equal, Value = 42 }, id: "age"),
            RuleFor(new ComparisonCondition { Field = "order.items[0].sku", Operator = ComparisonOperator.Equal, Value = "abc" }, id: "sku")
        };
        var result = await new RulesEngine().EvaluateAsync(rules, facts);
        Assert.Equal(new[] { "age", "sku" }, result.MatchedRules.Select(rule => rule.Id));
    }

    [Fact]
    public async Task Composite_groups_use_all_any_none_truth_tables_and_empty_semantics()
    {
        var trueCondition = new ComparisonCondition { Field = "value", Operator = ComparisonOperator.Equal, Value = 1 };
        var falseCondition = new ComparisonCondition { Field = "value", Operator = ComparisonOperator.Equal, Value = 2 };
        var rules = new[]
        {
            RuleFor(new ConditionGroup { Group = ConditionGroupType.All, Conditions = new[] { trueCondition, falseCondition } }, id: "all-false"),
            RuleFor(new ConditionGroup { Group = ConditionGroupType.Any, Conditions = new[] { trueCondition, falseCondition } }, id: "any-true"),
            RuleFor(new ConditionGroup { Group = ConditionGroupType.None, Conditions = new[] { falseCondition } }, id: "none-true"),
            RuleFor(new ConditionGroup { Group = ConditionGroupType.All, Conditions = Array.Empty<Condition>() }, id: "empty-all"),
            RuleFor(new ConditionGroup { Group = ConditionGroupType.Any, Conditions = Array.Empty<Condition>() }, id: "empty-any"),
            RuleFor(new ConditionGroup { Group = ConditionGroupType.None, Conditions = Array.Empty<Condition>() }, id: "empty-none")
        };
        var result = await new RulesEngine().EvaluateAsync(rules, new { value = 1 });
        Assert.Equal(new[] { "any-true", "empty-all", "empty-none", "none-true" }, result.MatchedRules.Select(rule => rule.Id));
    }

    [Fact]
    public async Task Priority_disabled_and_stop_first_options_are_applied()
    {
        var handler = new RecordingHandler("record");
        var rules = new[]
        {
            RuleFor(new ComparisonCondition { Field = "ok", Operator = ComparisonOperator.Equal, Value = true }, "late", 10,
                actions: new[] { new RuleAction { Type = "record" } }),
            RuleFor(new ComparisonCondition { Field = "ok", Operator = ComparisonOperator.Equal, Value = true }, "early", 1,
                actions: new[] { new RuleAction { Type = "record" } }),
            RuleFor(new ComparisonCondition { Field = "ok", Operator = ComparisonOperator.Equal, Value = true }, "disabled", 0,
                actions: new[] { new RuleAction { Type = "record" } }) with { Enabled = false }
        };
        var result = await new RulesEngine(new[] { handler }, new RulesEngineOptions { StopOnFirstMatch = true })
            .EvaluateAsync(rules, new { ok = true });
        Assert.Equal(new[] { "early" }, result.MatchedRules.Select(rule => rule.Id));
        Assert.Single(handler.Actions);
    }

    [Fact]
    public async Task Action_handlers_dispatch_and_unknown_action_can_be_recorded_or_thrown()
    {
        var handler = new RecordingHandler("custom");
        var rule = RuleFor(new ComparisonCondition { Field = "ok", Operator = ComparisonOperator.Equal, Value = true },
            actions: new[] { new RuleAction { Type = "custom" }, new RuleAction { Type = "missing" } });
        var recorded = await new RulesEngine(new[] { handler },
                new RulesEngineOptions { UnknownActionBehavior = UnknownActionBehavior.RecordError })
            .EvaluateAsync(new[] { rule }, new { ok = true });
        Assert.Single(recorded.ExecutedActions);
        Assert.Single(recorded.Errors);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            new RulesEngine(new[] { handler }).EvaluateAsync(new[] { rule }, new { ok = true }));
    }

    [Fact]
    public async Task Rule_errors_can_abort_or_continue()
    {
        var bad = RuleFor(new ComparisonCondition { Field = "x", Operator = ComparisonOperator.Contains, Value = 1 }, "bad");
        var good = RuleFor(new ComparisonCondition { Field = "x", Operator = ComparisonOperator.Equal, Value = "ok" }, "good", 2);
        var continued = await new RulesEngine(options: new RulesEngineOptions { RuleErrorBehavior = RuleErrorBehavior.Continue })
            .EvaluateAsync(new[] { bad, good }, new { x = "ok" });
        Assert.Single(continued.Errors);
        Assert.Contains(continued.MatchedRules, rule => rule.Id == "good");
        var aborted = await new RulesEngine().EvaluateAsync(new[] { bad, good }, new { x = "ok" });
        Assert.Single(aborted.Errors);
        Assert.Empty(aborted.MatchedRules);
    }

    [Fact]
    public async Task Json_source_reads_array_and_wrapper_and_reports_file_and_rule()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {"rules":[{"id":"json-1","name":"JSON","priority":1,"enabled":true,
                "condition":{"type":"comparison","field":"score","operator":"greaterThan","value":5},
                "actions":[{"type":"record","parameters":{}}]}]}
                """);
            var rules = await new JsonFileRuleSource(path).LoadRulesAsync();
            Assert.Single(rules);
            Assert.Equal("json-1", rules[0].Id);
            await File.WriteAllTextAsync(path, """
                [{"id":"json-2","name":"JSON array","priority":2,"enabled":true,
                "condition":{"type":"comparison","field":"score","operator":"greaterThan","value":5},
                "actions":[]}]
                """);
            Assert.Equal("json-2", (await new JsonFileRuleSource(path).LoadRulesAsync())[0].Id);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Json_file_to_engine_executes_expected_action()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                [{"id":"e2e","name":"E2E","priority":1,"enabled":true,
                "condition":{"type":"comparison","field":"score","operator":"greaterThan","value":5},
                "actions":[{"type":"record","parameters":{"tag":"accepted"}}]}]
                """);
            var handler = new RecordingHandler("record");
            var rules = await new JsonFileRuleSource(path).LoadRulesAsync();
            var result = await new RulesEngine(new[] { handler }).EvaluateAsync(rules, new { score = 7 });
            Assert.Equal("e2e", Assert.Single(result.MatchedRules).Id);
            Assert.Equal("record", Assert.Single(result.ExecutedActions).ActionType);
            Assert.Equal("accepted", handler.Actions[0].Parameters["tag"]?.ToString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Json_source_rejects_malformed_json_unknown_operator_and_missing_fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """[{"id":"bad","name":"Bad","condition":{"type":"comparison","field":"x","operator":"wat"},"actions":[]}]""");
            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => new JsonFileRuleSource(path).LoadRulesAsync());
            Assert.Contains(path, exception.Message);
            await File.WriteAllTextAsync(path, """[{"id":"","name":"","condition":null,"actions":[]}]""");
            exception = await Assert.ThrowsAsync<InvalidDataException>(() => new JsonFileRuleSource(path).LoadRulesAsync());
            Assert.Contains("Id is required", exception.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Sql_query_builder_quotes_defaults_and_custom_identifiers_and_rejects_injection()
    {
        var sql = SqlRuleQueryBuilder.BuildSelect(new SqlRuleSourceOptions { ConnectionString = "Server=x" });
        Assert.Contains("FROM [dbo].[Rules]", sql);
        Assert.Contains("[Definition]", sql);
        var custom = SqlRuleQueryBuilder.BuildSelect(new SqlRuleSourceOptions
        {
            ConnectionString = "Server=x",
            Schema = "app",
            Table = "RuleSet",
            IdColumn = "RuleKey"
        });
        Assert.Contains("FROM [app].[RuleSet]", custom);
        Assert.Contains("[RuleKey]", custom);
        Assert.Throws<ArgumentException>(() => SqlRuleQueryBuilder.BuildSelect(
            new SqlRuleSourceOptions { ConnectionString = "Server=x", Table = "Rules; DROP TABLE Users" }));
    }

    [Fact]
    public void Sql_mapper_maps_row_and_rejects_null_or_invalid_definition()
    {
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Description", typeof(string));
        table.Columns.Add("Priority", typeof(int));
        table.Columns.Add("Enabled", typeof(bool));
        table.Columns.Add("Definition", typeof(string));
        table.Rows.Add("sql-1", "SQL", DBNull.Value, 3, true,
            """{"condition":{"type":"comparison","field":"x","operator":"equal","value":1},"actions":[]}""");
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        Assert.Equal("sql-1", SqlRuleMapper.Map(reader, new SqlRuleSourceOptions { ConnectionString = "Server=x" }).Id);
        table.Rows[0]["Definition"] = DBNull.Value;
        using var nullReader = table.CreateDataReader();
        Assert.True(nullReader.Read());
        Assert.Throws<RuleValidationException>(() => SqlRuleMapper.Map(nullReader,
            new SqlRuleSourceOptions { ConnectionString = "Server=x" }));
        table.Rows[0]["Definition"] = "{";
        using var invalidReader = table.CreateDataReader();
        Assert.True(invalidReader.Read());
        Assert.Throws<RuleValidationException>(() => SqlRuleMapper.Map(invalidReader,
            new SqlRuleSourceOptions { ConnectionString = "Server=x" }));
    }

    private static Rule RuleFor(Condition condition, string id = "rule", int priority = 0,
        IReadOnlyList<RuleAction>? actions = null) =>
        new()
        {
            Id = id,
            Name = id,
            Priority = priority,
            Enabled = true,
            Condition = condition,
            Actions = actions ?? Array.Empty<RuleAction>()
        };

    private static IRuleActionHandler NoOpHandler(string type) => new RecordingHandler(type);

    private sealed class RecordingHandler : IRuleActionHandler
    {
        public RecordingHandler(string actionType) => ActionType = actionType;
        public string ActionType { get; }
        public List<RuleAction> Actions { get; } = new();
        public Task HandleAsync(RuleAction action, RuleActionContext context, CancellationToken cancellationToken)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }
}
