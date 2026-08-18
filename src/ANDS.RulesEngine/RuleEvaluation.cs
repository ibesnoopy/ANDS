using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ANDS.RulesEngine;

public sealed class RuleEvaluationOptions
{
    public bool StringCaseSensitive { get; init; }
}

internal static class ConditionEvaluator
{
    public static bool Evaluate(Condition condition, IFactContext facts, RuleEvaluationOptions options)
    {
        return condition switch
        {
            ConditionGroup group => EvaluateGroup(group, facts, options),
            ComparisonCondition comparison => EvaluateComparison(comparison, facts, options),
            _ => throw new InvalidOperationException($"Unsupported condition type '{condition.GetType().Name}'.")
        };
    }

    private static bool EvaluateGroup(ConditionGroup group, IFactContext facts, RuleEvaluationOptions options)
    {
        var conditions = group.Conditions ?? throw new InvalidOperationException("Condition group is missing children.");
        return group.Group switch
        {
            ConditionGroupType.All => conditions.All(child => Evaluate(child, facts, options)),
            ConditionGroupType.Any => conditions.Any(child => Evaluate(child, facts, options)),
            ConditionGroupType.None => conditions.All(child => !Evaluate(child, facts, options)),
            _ => throw new InvalidOperationException($"Unsupported group '{group.Group}'.")
        };
    }

    private static bool EvaluateComparison(ComparisonCondition condition, IFactContext facts,
        RuleEvaluationOptions options)
    {
        var found = facts.TryGetValue(condition.Field, out var actual);
        var op = condition.Operator;
        if (op == ComparisonOperator.IsNull)
            return found && actual is null;
        if (op == ComparisonOperator.IsNotNull)
            return found && actual is not null;
        if (!found)
            return false;

        var expected = UnwrapJsonElement(condition.Value);
        return op switch
        {
            ComparisonOperator.Equal => AreEqual(actual, expected, options),
            ComparisonOperator.NotEqual => !AreEqual(actual, expected, options),
            ComparisonOperator.GreaterThan => Compare(actual, expected, options) > 0,
            ComparisonOperator.GreaterThanOrEqual => Compare(actual, expected, options) >= 0,
            ComparisonOperator.LessThan => Compare(actual, expected, options) < 0,
            ComparisonOperator.LessThanOrEqual => Compare(actual, expected, options) <= 0,
            ComparisonOperator.Contains => Contains(actual, expected, options),
            ComparisonOperator.StartsWith => StartsWith(actual, expected, options),
            ComparisonOperator.EndsWith => EndsWith(actual, expected, options),
            ComparisonOperator.In => In(actual, expected, options),
            ComparisonOperator.NotIn => !In(actual, expected, options),
            ComparisonOperator.Matches => Matches(actual, expected, options),
            _ => throw new InvalidOperationException($"Unsupported operator '{op}'.")
        };
    }

    private static bool AreEqual(object? actual, object? expected, RuleEvaluationOptions options)
    {
        if (actual is null || expected is null)
            return actual is null && expected is null;
        if (IsNumericType(actual) && TryGetDecimal(actual, out var actualNumber) &&
            TryGetDecimal(expected, out var expectedNumber))
            return actualNumber == expectedNumber;
        if (actual is bool actualBoolean && TryGetBoolean(expected, out var expectedBoolean))
            return actualBoolean == expectedBoolean;
        if (actual is DateTime actualDate && TryGetDateTime(expected, out var expectedDate))
            return actualDate == expectedDate;
        if (actual is string actualString && expected is string expectedString)
            return string.Equals(actualString, expectedString,
                options.StringCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        return actual.GetType() == expected.GetType() && Equals(actual, expected);
    }

    private static int Compare(object? actual, object? expected, RuleEvaluationOptions options)
    {
        if (actual is null || expected is null)
            throw new InvalidOperationException("Ordering comparisons require non-null values.");
        if (IsNumericType(actual) && TryGetDecimal(actual, out var actualNumber) &&
            TryGetDecimal(expected, out var expectedNumber))
            return actualNumber.CompareTo(expectedNumber);
        if (actual is DateTime actualDate && TryGetDateTime(expected, out var expectedDate))
            return actualDate.CompareTo(expectedDate);
        if (actual is string actualString && expected is string expectedString)
            return string.Compare(actualString, expectedString,
                options.StringCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        throw new InvalidOperationException(
            $"Values of type '{actual.GetType().Name}' and '{expected.GetType().Name}' cannot be ordered.");
    }

    private static bool Contains(object? actual, object? expected, RuleEvaluationOptions options)
    {
        if (actual is string actualString && expected is string expectedString)
            return actualString.Contains(expectedString,
                options.StringCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        if (actual is IEnumerable enumerable && actual is not string)
            return enumerable.Cast<object?>().Any(item => AreEqual(item, expected, options));
        throw new InvalidOperationException("Contains requires a string or collection actual value and a compatible value.");
    }

    private static bool StartsWith(object? actual, object? expected, RuleEvaluationOptions options) =>
        actual is string actualString && expected is string expectedString &&
        actualString.StartsWith(expectedString,
            options.StringCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static bool EndsWith(object? actual, object? expected, RuleEvaluationOptions options) =>
        actual is string actualString && expected is string expectedString &&
        actualString.EndsWith(expectedString,
            options.StringCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static bool In(object? actual, object? expected, RuleEvaluationOptions options)
    {
        if (expected is not IEnumerable enumerable || expected is string)
            throw new InvalidOperationException("In requires an array or collection value.");
        return enumerable.Cast<object?>().Any(item => AreEqual(actual, item, options));
    }

    private static bool Matches(object? actual, object? expected, RuleEvaluationOptions options)
    {
        if (actual is not string actualString || expected is not string pattern)
            throw new InvalidOperationException("Matches requires string actual and pattern values.");
        var regexOptions = options.StringCaseSensitive ? RegexOptions.CultureInvariant : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        return Regex.IsMatch(actualString, pattern, regexOptions);
    }

    private static object? UnwrapJsonElement(object? value)
    {
        if (value is not JsonElement element)
            return value;
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Array => element.EnumerateArray().Select(item => UnwrapJsonElement(item)).ToArray(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => UnwrapJsonElement(p.Value),
                StringComparer.OrdinalIgnoreCase),
            _ => element.GetRawText()
        };
    }

    private static bool TryGetDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                try
                {
                    result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (OverflowException) { break; }
            case string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out result):
                return true;
        }
        result = default;
        return false;
    }

    private static bool IsNumericType(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool TryGetBoolean(object value, out bool result)
    {
        if (value is bool boolean)
        {
            result = boolean;
            return true;
        }
        if (value is string text && bool.TryParse(text, out result))
            return true;
        result = default;
        return false;
    }

    private static bool TryGetDateTime(object value, out DateTime result)
    {
        if (value is DateTime dateTime)
        {
            result = dateTime;
            return true;
        }
        if (value is string text && DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out result))
            return true;
        result = default;
        return false;
    }
}
