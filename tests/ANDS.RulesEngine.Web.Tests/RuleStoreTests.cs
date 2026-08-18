using System.Text.Json;
using ANDS.RulesEngine.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ANDS.RulesEngine.Web.Tests;

public class RuleStoreTests
{
    private const string ValidDefinition = """
        {
          "condition": { "type": "comparison", "field": "order.total", "operator": "greaterThan", "value": 100 },
          "actions": [ { "type": "notify", "parameters": { "channel": "sales" } } ]
        }
        """;

    private static RulesDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RulesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static RuleEditModel Model(string id = "rule-1", string definition = ValidDefinition, bool enabled = true) =>
        new(id, "Rule one", "description", 10, enabled, definition);

    [Fact]
    public async Task CreateAsync_persists_normalized_definition_and_audit()
    {
        await using var context = CreateContext();
        var store = new RuleStore(context, TimeProvider.System);

        await store.CreateAsync(Model(), "matthew");

        var record = Assert.Single(await store.ListAsync());
        Assert.Equal("rule-1", record.Id);
        Assert.Equal("matthew", record.UpdatedBy);
        var definition = JsonSerializer.Deserialize<RuleDefinition>(record.Definition,
            RuleJsonSerializer.CreateOptions());
        Assert.IsType<ComparisonCondition>(definition!.Condition);
        var audit = Assert.Single(await store.ListAuditAsync(10));
        Assert.Equal("created", audit.Action);
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_id()
    {
        await using var context = CreateContext();
        var store = new RuleStore(context, TimeProvider.System);
        await store.CreateAsync(Model(), "matthew");

        await Assert.ThrowsAsync<RuleStoreValidationException>(() => store.CreateAsync(Model(), "matthew"));
    }

    [Fact]
    public async Task UpdateAsync_replaces_definition_and_records_audit()
    {
        await using var context = CreateContext();
        var store = new RuleStore(context, TimeProvider.System);
        await store.CreateAsync(Model(), "matthew");

        await store.UpdateAsync(Model(enabled: false), "someone-else");

        var record = Assert.Single(await store.ListAsync());
        Assert.False(record.Enabled);
        Assert.Equal("someone-else", record.UpdatedBy);
        Assert.Equal(2, (await store.ListAuditAsync(10)).Count);
    }

    [Fact]
    public async Task UpdateAsync_and_DeleteAsync_fail_for_missing_rule()
    {
        await using var context = CreateContext();
        var store = new RuleStore(context, TimeProvider.System);

        await Assert.ThrowsAsync<RuleStoreValidationException>(() => store.UpdateAsync(Model(), "matthew"));
        await Assert.ThrowsAsync<RuleStoreValidationException>(() => store.DeleteAsync("rule-1", "matthew"));
    }

    [Fact]
    public async Task DeleteAsync_removes_rule_and_keeps_audit()
    {
        await using var context = CreateContext();
        var store = new RuleStore(context, TimeProvider.System);
        await store.CreateAsync(Model(), "matthew");

        await store.DeleteAsync("rule-1", "matthew");

        Assert.Empty(await store.ListAsync());
        Assert.Contains(await store.ListAuditAsync(10), audit => audit.Action == "deleted");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{ \"condition\": { \"type\": \"comparison\", \"field\": \"x\", \"operator\": \"nope\" } }")]
    [InlineData("{ \"actions\": [] }")]
    [InlineData("{ \"condition\": { \"type\": \"comparison\", \"field\": \"\", \"operator\": \"equal\", \"value\": 1 } }")]
    public void NormalizeDefinition_rejects_invalid_definitions(string definition) =>
        Assert.Throws<RuleStoreValidationException>(() => RuleStore.NormalizeDefinition(Model(definition: definition)));

    [Fact]
    public void NormalizeDefinition_requires_id_and_name()
    {
        Assert.Throws<RuleStoreValidationException>(() =>
            RuleStore.NormalizeDefinition(new RuleEditModel(" ", "name", null, 0, true, ValidDefinition)));
        Assert.Throws<RuleStoreValidationException>(() =>
            RuleStore.NormalizeDefinition(new RuleEditModel("id", " ", null, 0, true, ValidDefinition)));
    }

    [Fact]
    public async Task Rules_are_ordered_by_priority_then_id()
    {
        await using var context = CreateContext();
        var store = new RuleStore(context, TimeProvider.System);
        await store.CreateAsync(new RuleEditModel("b", "B", null, 5, true, ValidDefinition), "matthew");
        await store.CreateAsync(new RuleEditModel("a", "A", null, 5, true, ValidDefinition), "matthew");
        await store.CreateAsync(new RuleEditModel("c", "C", null, 1, true, ValidDefinition), "matthew");

        Assert.Equal(new[] { "c", "a", "b" }, (await store.ListAsync()).Select(rule => rule.Id));
    }
}
