using System.Collections;
using System.Text.Json;
using ANDS.RulesEngine;

namespace ANDS.RulesEngine.Tests;

public sealed class FactContextTests
{
    [Fact]
    public void Dictionary_paths_are_case_insensitive()
    {
        var context = new FactContext(new Dictionary<string, object?>
        {
            ["Customer"] = new Dictionary<string, object?> { ["AGE"] = 42 }
        });
        Assert.True(context.TryGetValue("customer.age", out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void Poco_paths_are_case_insensitive_and_support_deep_dotted_paths()
    {
        var facts = new { Order = new { Customer = new { Address = new { Zip = "12345" } } } };
        var context = new FactContext(facts);
        Assert.True(context.TryGetValue("ORDER.customer.ADDRESS.zip", out var value));
        Assert.Equal("12345", value);
    }

    [Fact]
    public void Indexers_support_arrays_lists_and_dictionary_keys()
    {
        var facts = new
        {
            Items = new[] { new { Sku = "A" }, new { Sku = "B" } },
            Metadata = new Dictionary<string, object?> { ["key"] = "value" }
        };
        var context = new FactContext(facts);
        Assert.True(context.TryGetValue("items[1].sku", out var item));
        Assert.Equal("B", item);
        Assert.True(context.TryGetValue("metadata[key]", out var metadata));
        Assert.Equal("value", metadata);
    }

    [Theory]
    [InlineData("items[3]")]
    [InlineData("missing.property")]
    [InlineData("items[0].missing")]
    [InlineData("items[0].missing.deep")]
    public void Invalid_paths_return_false(string path)
    {
        var context = new FactContext(new { Items = new[] { new { Sku = "A" } } });
        Assert.False(context.TryGetValue(path, out _));
    }

    [Fact]
    public void Path_through_null_intermediate_is_missing()
    {
        var context = new FactContext(new { Customer = (object?)null });
        Assert.False(context.TryGetValue("customer.name", out _));
    }

    [Fact]
    public void Present_null_is_distinct_from_missing()
    {
        var context = new FactContext(new { Value = (string?)null });
        Assert.True(context.TryGetValue("value", out var value));
        Assert.Null(value);
        Assert.False(context.TryGetValue("missing", out _));
    }

    [Fact]
    public void JsonElement_paths_are_resolved_case_insensitively()
    {
        using var document = JsonDocument.Parse("""{"Order":{"Customer":{"Age":42}}}""");
        var context = new FactContext(document.RootElement);

        Assert.True(context.TryGetValue("order.customer.age", out var value));
        Assert.Equal(JsonValueKind.Number, Assert.IsType<JsonElement>(value).ValueKind);
        Assert.Equal(42, Assert.IsType<JsonElement>(value).GetInt32());
    }

    [Fact]
    public void Non_generic_dictionaries_are_resolved_case_insensitively()
    {
        var facts = new Hashtable
        {
            ["Customer"] = new Hashtable { ["AGE"] = 42 }
        };
        var context = new FactContext(facts);

        Assert.True(context.TryGetValue("customer.age", out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void Public_fields_are_resolved_case_insensitively()
    {
        var context = new FactContext(new PublicFields { Nested = new PublicFields { Value = "found" } });

        Assert.True(context.TryGetValue("NESTED.value", out var value));
        Assert.Equal("found", value);
    }

    [Fact]
    public void Indexer_properties_are_not_treated_as_path_segments()
    {
        var context = new FactContext(new IndexerOnly());

        Assert.False(context.TryGetValue("Item", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Empty_or_whitespace_paths_return_false(string path)
    {
        Assert.False(new FactContext(new { Value = 1 }).TryGetValue(path, out _));
    }

    [Theory]
    [InlineData("items[0")]
    [InlineData("items]")]
    [InlineData("items[]")]
    [InlineData("items[0]]")]
    [InlineData("items[[0]]")]
    public void Malformed_bracket_syntax_returns_false(string path)
    {
        var context = new FactContext(new { Items = new[] { "value" } });

        Assert.False(context.TryGetValue(path, out _));
    }

    private sealed class PublicFields
    {
        public PublicFields? Nested;
        public string? Value;
    }

    private sealed class IndexerOnly
    {
        public string this[int index] => index.ToString();
    }
}
