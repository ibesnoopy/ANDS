using System.Text.Json;
using System.Text.Json.Serialization;

namespace ANDS.RulesEngine;

public enum ConditionGroupType
{
    All,
    Any,
    None
}

public enum ComparisonOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains,
    StartsWith,
    EndsWith,
    In,
    NotIn,
    Matches,
    IsNull,
    IsNotNull
}

[JsonConverter(typeof(ConditionJsonConverter))]
public abstract record Condition;

public sealed record ConditionGroup : Condition
{
    public ConditionGroupType Group { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = [];
}

public sealed record ComparisonCondition : Condition
{
    public string Field { get; init; } = string.Empty;
    public ComparisonOperator Operator { get; init; }
    public object? Value { get; init; }
}

public sealed record RuleAction
{
    public string Type { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}

public sealed record Rule
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int Priority { get; init; }
    public bool Enabled { get; init; } = true;
    public Condition? Condition { get; init; }
    public IReadOnlyList<RuleAction> Actions { get; init; } = [];

    public void Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id))
            errors.Add("Id is required.");
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Name is required.");
        if (Condition is null)
            errors.Add("Condition is required.");
        else
            ConditionValidator.Validate(Condition, errors);
        ValidationHelpers.ValidateCollection(Actions, errors, "Actions is required.",
            (action, index) =>
            {
                if (string.IsNullOrWhiteSpace(action.Type))
                    errors.Add($"Actions[{index}].Type is required.");
                if (action.Parameters is null)
                    errors.Add($"Actions[{index}].Parameters is required.");
            });

        if (errors.Count > 0)
            throw new RuleValidationException(Id, string.Join(" ", errors));
    }
}

public sealed class RuleValidationException : Exception
{
    public RuleValidationException(string? ruleId, string message)
        : base(ruleId is null or "" ? message : $"Rule '{ruleId}': {message}")
    {
        RuleId = ruleId;
    }

    public string? RuleId { get; }
}

internal static class ValidationHelpers
{
    public static void ValidateCollection<T>(IReadOnlyList<T>? items, ICollection<string> errors,
        string requiredMessage, Action<T, int> validateItem)
    {
        if (items is null)
        {
            errors.Add(requiredMessage);
            return;
        }

        for (var index = 0; index < items.Count; index++)
            validateItem(items[index], index);
    }
}

internal static class ConditionValidator
{
    public static void Validate(Condition condition, ICollection<string> errors)
    {
        switch (condition)
        {
            case ConditionGroup group:
                ValidationHelpers.ValidateCollection(group.Conditions, errors,
                    "Condition group Conditions is required.",
                    (child, index) =>
                    {
                        if (child is null)
                            errors.Add($"Condition.Conditions[{index}] cannot be null.");
                        else
                            Validate(child, errors);
                    });
                break;
            case ComparisonCondition comparison:
                if (string.IsNullOrWhiteSpace(comparison.Field))
                    errors.Add("Comparison field is required.");
                if (comparison.Operator is not ComparisonOperator.IsNull and not ComparisonOperator.IsNotNull &&
                    comparison.Operator is not ComparisonOperator.Equal and not ComparisonOperator.NotEqual &&
                    comparison.Value is null)
                    errors.Add($"Comparison '{comparison.Field}' requires a value.");
                break;
            default:
                errors.Add($"Unsupported condition type '{condition.GetType().Name}'.");
                break;
        }
    }
}

internal sealed class ConditionJsonConverter : JsonConverter<Condition>
{
    public override Condition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("A condition must be a JSON object.");

        var discriminator = GetString(root, "type") ?? GetString(root, "kind");
        if (string.IsNullOrWhiteSpace(discriminator))
            throw new JsonException("Condition requires a 'type' property ('group' or 'comparison').");

        if (discriminator.Equals("group", StringComparison.OrdinalIgnoreCase))
        {
            var group = GetString(root, "group") ?? GetString(root, "operator");
            if (!Enum.TryParse<ConditionGroupType>(group, true, out var groupType))
                throw new JsonException($"Unknown condition group '{group}'.");
            var children = root.TryGetProperty("conditions", out var conditions)
                ? JsonSerializer.Deserialize<List<Condition>>(conditions.GetRawText(), options)
                : null;
            if (children is null)
                throw new JsonException("Condition group requires a 'conditions' array.");
            return new ConditionGroup { Group = groupType, Conditions = children };
        }

        if (discriminator.Equals("comparison", StringComparison.OrdinalIgnoreCase) ||
            discriminator.Equals("leaf", StringComparison.OrdinalIgnoreCase))
        {
            var field = GetString(root, "field");
            var operatorName = GetString(root, "operator");
            if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(operatorName))
                throw new JsonException("Comparison requires 'field' and 'operator'.");
            if (!Enum.TryParse<ComparisonOperator>(operatorName, true, out var comparisonOperator))
                throw new JsonException($"Unknown comparison operator '{operatorName}'.");
            object? value = null;
            if (root.TryGetProperty("value", out var valueElement))
                value = JsonSerializer.Deserialize<object>(valueElement.GetRawText(), options);
            return new ComparisonCondition { Field = field, Operator = comparisonOperator, Value = value };
        }

        throw new JsonException($"Unknown condition type '{discriminator}'.");
    }

    public override void Write(Utf8JsonWriter writer, Condition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case ConditionGroup group:
                writer.WriteString("type", "group");
                writer.WriteString("group", group.Group.ToString());
                writer.WritePropertyName("conditions");
                JsonSerializer.Serialize(writer, group.Conditions, options);
                break;
            case ComparisonCondition comparison:
                writer.WriteString("type", "comparison");
                writer.WriteString("field", comparison.Field);
                writer.WriteString("operator", comparison.Operator.ToString());
                if (comparison.Value is not null)
                {
                    writer.WritePropertyName("value");
                    JsonSerializer.Serialize(writer, comparison.Value, options);
                }
                break;
            default:
                throw new JsonException($"Unsupported condition type '{value.GetType().Name}'.");
        }
        writer.WriteEndObject();
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

public static class RuleJsonSerializer
{
    internal static JsonSerializerOptions DefaultOptions { get; } = CreateOptions();

    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
