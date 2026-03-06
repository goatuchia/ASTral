using System.Text.Json;

namespace ASTral.Models;

/// <summary>
/// Shared helpers for extracting typed values from symbol dictionaries
/// backed by <see cref="JsonElement"/> values.
/// </summary>
internal static class JsonElementHelpers
{
    /// <summary>
    /// Gets a string value from the dictionary, returning an empty string when
    /// the key is missing or the element is not a string.
    /// </summary>
    internal static string GetString(Dictionary<string, JsonElement> dict, string key)
    {
        return dict.TryGetValue(key, out var elem) && elem.ValueKind == JsonValueKind.String
            ? elem.GetString() ?? ""
            : "";
    }

    /// <summary>
    /// Gets an integer value from the dictionary, returning zero when
    /// the key is missing or the element is not a number.
    /// </summary>
    internal static int GetInt(Dictionary<string, JsonElement> dict, string key)
    {
        return dict.TryGetValue(key, out var elem) && elem.ValueKind == JsonValueKind.Number
            ? elem.GetInt32()
            : 0;
    }

    /// <summary>
    /// Gets a list of strings from a JSON array element, returning an empty list
    /// when the key is missing or the element is not an array.
    /// </summary>
    internal static List<string> GetStringList(Dictionary<string, JsonElement> dict, string key)
    {
        if (!dict.TryGetValue(key, out var elem) || elem.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<string>();
        foreach (var item in elem.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                result.Add(item.GetString() ?? "");
        }

        return result;
    }
}
