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
        if (!TryParsePath(path, out var segments))
            return false;
        foreach (var segment in segments)
        {
            if (!TryGetSegment(current, segment, out current))
                return false;
        }

        value = current;
        return true;
    }

    private static bool TryParsePath(string path, out IReadOnlyList<string> segments)
    {
        var parsed = new List<string>();
        var segment = new System.Text.StringBuilder();
        var inBracket = false;
        foreach (var character in path)
        {
            if (inBracket)
            {
                if (character == '[')
                {
                    segments = Array.Empty<string>();
                    return false;
                }
                if (character == ']')
                {
                    var bracketValue = segment.ToString().Trim('\'', '"');
                    if (bracketValue.Length == 0)
                    {
                        segments = Array.Empty<string>();
                        return false;
                    }
                    parsed.Add(bracketValue);
                    segment.Clear();
                    inBracket = false;
                }
                else
                    segment.Append(character);
            }
            else if (character == '.')
            {
                if (segment.Length > 0)
                {
                    parsed.Add(segment.ToString());
                    segment.Clear();
                }
            }
            else if (character == '[')
            {
                if (segment.Length > 0)
                {
                    parsed.Add(segment.ToString());
                    segment.Clear();
                }
                inBracket = true;
            }
            else if (character == ']')
            {
                segments = Array.Empty<string>();
                return false;
            }
            else
                segment.Append(character);
        }
        if (inBracket)
        {
            segments = Array.Empty<string>();
            return false;
        }
        if (segment.Length > 0)
            parsed.Add(segment.ToString());
        segments = parsed;
        return parsed.Count > 0;
    }

    private static bool TryGetSegment(object? source, string segment, out object? value)
    {
        value = null;
        if (source is null)
            return false;

        if (source is JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var jsonProperty in json.EnumerateObject())
            {
                if (string.Equals(jsonProperty.Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    value = jsonProperty.Value;
                    return true;
                }
            }
            return false;
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
