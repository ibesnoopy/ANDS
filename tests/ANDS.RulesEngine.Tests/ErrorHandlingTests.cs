using System.Data;
using System.Data.Common;
using System.Text.Json;
using ANDS.RulesEngine;

namespace ANDS.RulesEngine.Tests;

public sealed class ErrorHandlingTests
{
    [Fact]
    public async Task Handler_failures_report_rule_action_and_index_and_keep_the_original_exception()
    {
        var actions = new[]
        {
            new RuleAction { Type = "ok" },
            new RuleAction { Type = "boom" }
        };
        var result = await new RulesEngine(new IRuleActionHandler[]
        {
            new RecordingHandler("ok"),
            new ThrowingHandler("boom", new TimeoutException("downstream timed out"))
        }).EvaluateAsync(new[]
        {
            TestSupport.RuleFor(TestSupport.Comparison("ok", ComparisonOperator.Equal, true), "rule",
                actions: actions)
        }, new { ok = true });

        var error = Assert.Single(result.Errors);
        var actionException = Assert.IsType<RuleActionException>(error.Exception);
        Assert.Equal("rule", actionException.RuleId);
        Assert.Equal("boom", actionException.ActionType);
        Assert.Equal(1, actionException.Index);
        Assert.IsType<TimeoutException>(actionException.InnerException);
        Assert.Contains("downstream timed out", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Aborted_and_throw_if_errors_let_callers_detect_swallowed_rule_errors()
    {
        var bad = TestSupport.RuleFor(TestSupport.Comparison("value", ComparisonOperator.Contains, 1), "bad");
        var good = TestSupport.RuleFor(TestSupport.Comparison("ok", ComparisonOperator.Equal, true), "good", 1);
        var facts = new { value = 42, ok = true };

        var aborted = await new RulesEngine().EvaluateAsync(new[] { bad, good }, facts);
        Assert.True(aborted.Aborted);
        Assert.True(aborted.HasErrors);
        var exception = Assert.Throws<RuleEvaluationException>(aborted.ThrowIfErrors);
        Assert.Equal(aborted.Errors, exception.Errors);

        var continued = await new RulesEngine(options: new RulesEngineOptions
        { RuleErrorBehavior = RuleErrorBehavior.Continue }).EvaluateAsync(new[] { bad, good }, facts);
        Assert.False(continued.Aborted);

        var clean = await new RulesEngine().EvaluateAsync(new[] { good }, facts);
        Assert.False(clean.HasErrors);
        clean.ThrowIfErrors();
    }

    [Fact]
    public async Task Condition_errors_name_the_field_and_operator()
    {
        var result = await new RulesEngine().EvaluateAsync(new[]
        {
            TestSupport.RuleFor(TestSupport.Comparison("order.total", ComparisonOperator.StartsWith, "1"), "rule")
        }, new { order = new { total = 42 } });

        var error = Assert.Single(result.Errors);
        Assert.Contains("order.total", error.Message, StringComparison.Ordinal);
        Assert.Contains("StartsWith", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_and_slow_regex_patterns_are_reported_instead_of_escaping_raw()
    {
        var invalid = await new RulesEngine().EvaluateAsync(new[]
        {
            TestSupport.RuleFor(TestSupport.Comparison("value", ComparisonOperator.Matches, "([a-z"), "invalid")
        }, new { value = "abc" });
        Assert.Contains("not a valid regular expression", Assert.Single(invalid.Errors).Message,
            StringComparison.Ordinal);

        var slow = await new RulesEngine(options: new RulesEngineOptions
        { RegexTimeout = TimeSpan.FromMilliseconds(1) }).EvaluateAsync(new[]
        {
            TestSupport.RuleFor(TestSupport.Comparison("value", ComparisonOperator.Matches, "^(a+)+$"), "slow")
        }, new { value = new string('a', 40) + "b" });
        Assert.Contains("did not complete", Assert.Single(slow.Errors).Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RulesEngine(options: new RulesEngineOptions { RegexTimeout = TimeSpan.Zero }));
    }

    [Fact]
    public void Duplicate_or_invalid_handlers_are_rejected_with_an_actionable_message()
    {
        var duplicate = Assert.Throws<ArgumentException>(() => new RulesEngine(new[]
        {
            new RecordingHandler("notify"), new RecordingHandler("NOTIFY")
        }));
        Assert.Contains("notify", duplicate.Message, StringComparison.OrdinalIgnoreCase);

        var blank = Assert.Throws<ArgumentException>(() => new RulesEngine(new[] { new RecordingHandler(" ") }));
        Assert.Contains("ActionType", blank.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Numbers_outside_the_decimal_range_are_compared_instead_of_failing()
    {
        var result = await new RulesEngine().EvaluateAsync(new[]
        {
            TestSupport.RuleFor(TestSupport.Comparison("value", ComparisonOperator.GreaterThan, 1), "big")
        }, new { value = double.MaxValue });

        Assert.Empty(result.Errors);
        Assert.Single(result.MatchedRules);
    }

    [Fact]
    public async Task Fact_property_getters_that_throw_are_surfaced_with_context()
    {
        var result = await new RulesEngine().EvaluateAsync(new[]
        {
            TestSupport.RuleFor(TestSupport.Comparison("Broken", ComparisonOperator.Equal, 1), "rule")
        }, new ThrowingFacts());

        var error = Assert.Single(result.Errors);
        Assert.Contains("Broken", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ThrowingFacts), error.Message, StringComparison.Ordinal);
        Assert.Equal("getter failed", error.Exception.InnerException?.Message);
    }

    [Fact]
    public async Task Json_file_rule_errors_report_the_failing_index_and_reject_null_entries()
    {
        var path = await WriteFile("""
            [{"id":"one","name":"One","condition":{"type":"comparison","field":"x","operator":"equal","value":1},"actions":[]},
             {"id":"","name":"Two","condition":null,"actions":[]}]
            """);
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new JsonFileRuleSource(path).LoadRulesAsync());
            Assert.Contains("rules[1]", exception.Message, StringComparison.Ordinal);
            Assert.IsType<RuleValidationException>(exception.InnerException);

            await File.WriteAllTextAsync(path, "[null]");
            var nullEntry = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new JsonFileRuleSource(path).LoadRulesAsync());
            Assert.Contains("rules[0]", nullEntry.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Condition_group_with_a_non_array_conditions_property_is_explained()
    {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Condition>(
            """{"type":"group","group":"all","conditions":{}}""", RuleJsonSerializer.CreateOptions()));
        Assert.Contains("must be a JSON array", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_mapper_reports_missing_columns_and_unusable_values()
    {
        using var missingColumn = CreateTable(includePriority: false);
        missingColumn.Rows.Add("one", "One", DBNull.Value, true, Definition);
        using var missingReader = missingColumn.CreateDataReader();
        Assert.True(missingReader.Read());
        var missing = Assert.Throws<RuleValidationException>(() => SqlRuleMapper.Map(missingReader, Options));
        Assert.Contains("Priority", missing.Message, StringComparison.Ordinal);

        using var nullPriority = CreateTable();
        nullPriority.Rows.Add("one", "One", DBNull.Value, DBNull.Value, true, Definition);
        using var nullReader = nullPriority.CreateDataReader();
        Assert.True(nullReader.Read());
        var nullValue = Assert.Throws<RuleValidationException>(() => SqlRuleMapper.Map(nullReader, Options));
        Assert.Contains("Priority", nullValue.Message, StringComparison.Ordinal);
        Assert.Equal("one", nullValue.RuleId);
    }

    [Fact]
    public void Sql_mapper_keeps_the_json_exception_for_an_invalid_definition()
    {
        using var table = CreateTable();
        table.Rows.Add("one", "One", DBNull.Value, 1, true, "{");
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        var exception = Assert.Throws<RuleValidationException>(() => SqlRuleMapper.Map(reader, Options));
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task Sql_source_reports_the_failing_row_and_wraps_database_failures()
    {
        using var table = CreateTable();
        table.Rows.Add("one", "One", DBNull.Value, 1, true, Definition);
        table.Rows.Add("two", "Two", DBNull.Value, 2, true, "{");
        var rowFailure = await Assert.ThrowsAsync<RuleValidationException>(() =>
            new SqlRuleSource(Options, new TableConnectionFactory(table)).LoadRulesAsync());
        Assert.Contains("Row 2 of dbo.Rules", rowFailure.Message, StringComparison.Ordinal);

        var dbFailure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SqlRuleSource(Options, new FailingConnectionFactory()).LoadRulesAsync());
        Assert.Contains("dbo.Rules", dbFailure.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<DbException>(dbFailure.InnerException);
    }

    private static SqlRuleSourceOptions Options => new() { ConnectionString = "fake" };

    private static string Definition =>
        """{"condition":{"type":"comparison","field":"x","operator":"equal","value":1},"actions":[]}""";

    private static DataTable CreateTable(bool includePriority = true)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Description", typeof(string));
        if (includePriority)
            table.Columns.Add("Priority", typeof(int));
        table.Columns.Add("Enabled", typeof(bool));
        table.Columns.Add("Definition", typeof(string));
        return table;
    }

    private static async Task<string> WriteFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}

internal sealed class ThrowingHandler : IRuleActionHandler
{
    private readonly Exception _exception;

    public ThrowingHandler(string actionType, Exception exception) =>
        (ActionType, _exception) = (actionType, exception);

    public string ActionType { get; }

    public Task HandleAsync(RuleAction action, RuleActionContext context, CancellationToken cancellationToken) =>
        throw _exception;
}

internal sealed class ThrowingFacts
{
    public int Broken => throw new InvalidOperationException("getter failed");
}

internal sealed class FailingConnectionFactory : IDbConnectionFactory
{
    public DbConnection CreateConnection(string connectionString) => new FailingDbConnection();
}

#pragma warning disable CS8765
internal sealed class FailingDbConnection : DbConnection
{
    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => "Fake";
    public override string DataSource => "Fake";
    public override string ServerVersion => "1.0";
    public override ConnectionState State => ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() => throw new FakeDbException("login failed");
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException();
    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
}

#pragma warning restore CS8765

internal sealed class FakeDbException : DbException
{
    public FakeDbException(string message) : base(message) { }
}
