using System.Data;
using ANDS.RulesEngine;

namespace ANDS.RulesEngine.Tests;

public sealed class SqlSourceTests
{
    [Fact]
    public void Query_builder_quotes_defaults_and_reports_real_option_names()
    {
        var sql = SqlRuleQueryBuilder.BuildSelect(new SqlRuleSourceOptions { ConnectionString = "fake" });
        Assert.Contains("FROM [dbo].[Rules]", sql);
        Assert.Contains("[Id]", sql);
        var exception = Assert.Throws<ArgumentException>(() => SqlRuleQueryBuilder.BuildSelect(
            new SqlRuleSourceOptions { ConnectionString = "fake", IdColumn = "bad-name" }));
        Assert.Contains("IdColumn", exception.Message);
    }

    [Fact]
    public void Query_builder_supports_custom_identifiers_and_rejects_injection()
    {
        var sql = SqlRuleQueryBuilder.BuildSelect(new SqlRuleSourceOptions
        {
            ConnectionString = "fake",
            Schema = "app",
            Table = "RuleSet",
            DefinitionColumn = "JsonDefinition"
        });
        Assert.Contains("FROM [app].[RuleSet]", sql);
        Assert.Contains("[JsonDefinition]", sql);
        Assert.Throws<ArgumentException>(() => SqlRuleQueryBuilder.BuildSelect(
            new SqlRuleSourceOptions
            {
                ConnectionString = "fake",
                Table = "Rules; DROP TABLE Users"
            }));
    }

    [Fact]
    public void Options_validate_connection_and_identifiers_explicitly()
    {
        Assert.Throws<ArgumentException>(() => new SqlRuleSourceOptions().Validate());
        Assert.Throws<ArgumentException>(() => new SqlRuleSourceOptions
        {
            ConnectionString = "fake",
            Schema = "bad.schema"
        }.Validate());
    }

    [Fact]
    public void Mapper_maps_data_reader_rows_and_rejects_bad_definition()
    {
        using var table = CreateTable();
        table.Rows.Add("one", "One", DBNull.Value, 2, true, Definition("x", 1));
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        var rule = SqlRuleMapper.Map(reader, Options());
        Assert.Equal("one", rule.Id);
        Assert.Null(rule.Description);
        Assert.Equal(2, rule.Priority);
        table.Rows[0]["Definition"] = "{";
        using var invalidReader = table.CreateDataReader();
        Assert.True(invalidReader.Read());
        Assert.Throws<RuleValidationException>(() => SqlRuleMapper.Map(invalidReader, Options()));
    }

    [Fact]
    public async Task Source_loads_multiple_rows_in_reader_order_and_uses_built_select()
    {
        using var table = CreateTable();
        table.Rows.Add("one", "One", "first", 2, true, Definition("x", 1));
        table.Rows.Add("two", "Two", "second", 1, false, Definition("x", 2));
        var factory = new TableConnectionFactory(table);
        var options = Options();
        var rules = await new SqlRuleSource(options, factory).LoadRulesAsync();
        Assert.Equal(new[] { "one", "two" }, rules.Select(rule => rule.Id));
        Assert.Equal(SqlRuleQueryBuilder.BuildSelect(options), factory.LastConnection!.LastCommandText);
        Assert.False(rules[1].Enabled);
    }

    [Fact]
    public void Source_constructor_validates_options()
    {
        Assert.Throws<ArgumentException>(() => new SqlRuleSource(new SqlRuleSourceOptions()));
    }

    private static SqlRuleSourceOptions Options() => new() { ConnectionString = "fake" };

    private static string Definition(string field, int value) =>
        $$"""{"condition":{"type":"comparison","field":"{{field}}","operator":"equal","value":{{value}}},"actions":[]}""";

    private static DataTable CreateTable()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Description", typeof(string));
        table.Columns.Add("Priority", typeof(int));
        table.Columns.Add("Enabled", typeof(bool));
        table.Columns.Add("Definition", typeof(string));
        return table;
    }
}
