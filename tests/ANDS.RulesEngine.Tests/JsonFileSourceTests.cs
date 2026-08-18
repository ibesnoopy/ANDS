using ANDS.RulesEngine;

namespace ANDS.RulesEngine.Tests;

public sealed class JsonFileSourceTests
{
    [Fact]
    public async Task Reads_wrapper_and_bare_array_shapes()
    {
        var path = await WriteFile("""{"rules":[{"id":"one","name":"One","condition":{"type":"comparison","field":"x","operator":"equal","value":1},"actions":[]}]}""");
        try
        {
            Assert.Equal("one", (await new JsonFileRuleSource(path).LoadRulesAsync())[0].Id);
            await File.WriteAllTextAsync(path, """[{"id":"two","name":"Two","condition":{"type":"comparison","field":"x","operator":"equal","value":1},"actions":[]}]""");
            Assert.Equal("two", (await new JsonFileRuleSource(path).LoadRulesAsync())[0].Id);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Nonexistent_file_is_actionable()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new JsonFileRuleSource(path).LoadRulesAsync());
        Assert.Contains(path, exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{}""")]
    [InlineData("""{"rules":{}}""")]
    [InlineData("""{"other":[]}""")]
    public async Task Empty_or_invalid_root_shapes_are_rejected(string content)
    {
        var path = await WriteFile(content);
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new JsonFileRuleSource(path).LoadRulesAsync());
            Assert.Contains(path, exception.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Malformed_json_and_invalid_rule_include_file_and_rule_details()
    {
        var path = await WriteFile("""[{"id":"","name":"","condition":null,"actions":[{"type":""}]}]""");
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new JsonFileRuleSource(path).LoadRulesAsync());
            Assert.Contains(path, exception.Message);
            Assert.Contains("Id is required", exception.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Duplicate_rule_ids_are_preserved_in_source_order()
    {
        var path = await WriteFile("""
            [{"id":"same","name":"One","condition":{"type":"comparison","field":"x","operator":"equal","value":1},"actions":[]},
             {"id":"same","name":"Two","condition":{"type":"comparison","field":"x","operator":"equal","value":1},"actions":[]}]
            """);
        try
        {
            var rules = await new JsonFileRuleSource(path).LoadRulesAsync();
            Assert.Equal(2, rules.Count);
            Assert.Equal(new[] { "One", "Two" }, rules.Select(rule => rule.Name));
        }
        finally { File.Delete(path); }
    }

    private static async Task<string> WriteFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
