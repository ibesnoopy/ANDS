using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace ANDS.RulesEngine;

public interface IFactContext
{
    bool TryGetValue(string path, out object? value);
}

public sealed class FactContext : IFactContext
{
    private readonly object? _root;

    public FactContext(object? root) => _root = root;

    public bool TryGetValue(string path, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        object? current = _root;
        foreach (var segment in ParsePath(path))
        {
            if (!TryGetSegment(current, segment, out current))
                return false;
        }

        value = current;
        return true;
    }

    private static IEnumerable<string> ParsePath(string path)
    {
        var segment = new System.Text.StringBuilder();
        foreach (var character in path)
        {
            if (character == '.')
            {
                if (segment.Length > 0)
                {
                    yield return segment.ToString();
                    segment.Clear();
                }
            }
            else if (character == '[')
            {
                if (segment.Length > 0)
                {
                    yield return segment.ToString();
                    segment.Clear();
                }
            }
            else if (character == ']')
            {
                if (segment.Length > 0)
                {
                    yield return segment.ToString().Trim('\'', '"');
                    segment.Clear();
                }
            }
            else
            {
                segment.Append(character);
            }
        }
        if (segment.Length > 0)
            yield return segment.ToString();
    }

    private static bool TryGetSegment(object? source, string segment, out object? value)
    {
        value = null;
        if (source is null)
            return false;

        if (source is JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(segment, out var element))
                return false;
            value = element;
            return true;
        }

        if (source is IDictionary<string, object?> genericDictionary)
        {
            var key = genericDictionary.Keys.FirstOrDefault(k =>
                string.Equals(k, segment, StringComparison.OrdinalIgnoreCase));
            if (key is null || !genericDictionary.TryGetValue(key, out value))
                return false;
            return true;
        }

        if (source is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (string.Equals(Convert.ToString(entry.Key, CultureInfo.InvariantCulture), segment,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value = entry.Value;
                    return true;
                }
            }
            return false;
        }

        if (source is IList list && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            if (index < 0 || index >= list.Count)
                return false;
            value = list[index];
            return true;
        }

        var property = source.GetType().GetProperty(segment,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property is not null && property.GetIndexParameters().Length == 0)
        {
            value = property.GetValue(source);
            return true;
        }

        var field = source.GetType().GetField(segment,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (field is not null)
        {
            value = field.GetValue(source);
            return true;
        }

        return false;
    }
}
