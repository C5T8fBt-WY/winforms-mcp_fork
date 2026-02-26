using System;
using System.Collections.Generic;
using System.Text.Json;

namespace C5T8fBtWY.WinFormsMcp.Server.Utilities;

/// <summary>
/// Static helper methods for extracting typed arguments from JsonElement.
/// Eliminates duplicated argument parsing code across HandlerBase and ScriptRunner.
/// </summary>
public static class ArgHelpers
{
    /// <summary>
    /// Get a string value from a JSON object, or null if not present.
    /// </summary>
    /// <param name="args">The JSON element containing the arguments.</param>
    /// <param name="key">The property name to look up.</param>
    /// <returns>The string value, or null if not present or not a string.</returns>
    public static string? GetString(JsonElement args, string key)
    {
        if (args.ValueKind == JsonValueKind.Null || args.ValueKind == JsonValueKind.Undefined)
            return null;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    /// <summary>
    /// Get an integer value from a JSON object, with a default if not present.
    /// </summary>
    /// <param name="args">The JSON element containing the arguments.</param>
    /// <param name="key">The property name to look up.</param>
    /// <param name="defaultValue">The value to return if the property is not present or not a number.</param>
    /// <returns>The integer value, or the default value.</returns>
    public static int GetInt(JsonElement args, string key, int defaultValue = 0)
    {
        if (args.ValueKind == JsonValueKind.Null || args.ValueKind == JsonValueKind.Undefined)
            return defaultValue;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : defaultValue;
    }

    /// <summary>
    /// Get a double value from a JSON object, with a default if not present.
    /// </summary>
    /// <param name="args">The JSON element containing the arguments.</param>
    /// <param name="key">The property name to look up.</param>
    /// <param name="defaultValue">The value to return if the property is not present or not a number.</param>
    /// <returns>The double value, or the default value.</returns>
    public static double GetDouble(JsonElement args, string key, double defaultValue = 0)
    {
        if (args.ValueKind == JsonValueKind.Null || args.ValueKind == JsonValueKind.Undefined)
            return defaultValue;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetDouble()
            : defaultValue;
    }

    /// <summary>
    /// Get a boolean value from a JSON object, with a default if not present.
    /// </summary>
    /// <param name="args">The JSON element containing the arguments.</param>
    /// <param name="key">The property name to look up.</param>
    /// <param name="defaultValue">The value to return if the property is not present or not a boolean.</param>
    /// <returns>The boolean value, or the default value.</returns>
    public static bool GetBool(JsonElement args, string key, bool defaultValue = false)
    {
        if (args.ValueKind == JsonValueKind.Null || args.ValueKind == JsonValueKind.Undefined)
            return defaultValue;

        if (!args.TryGetProperty(key, out var prop))
            return defaultValue;

        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Get an enum value from a JSON object by parsing its string representation.
    /// </summary>
    /// <typeparam name="T">The enum type to parse into.</typeparam>
    /// <param name="args">The JSON element containing the arguments.</param>
    /// <param name="key">The property name to look up.</param>
    /// <returns>The parsed enum value, or null if not present or not parseable.</returns>
    public static T? GetEnum<T>(JsonElement args, string key) where T : struct, Enum
    {
        var stringValue = GetString(args, key);
        if (stringValue == null)
            return null;

        return Enum.TryParse<T>(stringValue, ignoreCase: true, out var result) ? result : null;
    }

    /// <summary>
    /// Get a nested JSON object from a JSON object.
    /// </summary>
    /// <param name="args">The JSON element containing the arguments.</param>
    /// <param name="key">The property name to look up.</param>
    /// <returns>The nested JSON element, or null if not present or not an object.</returns>
    public static JsonElement? GetObject(JsonElement args, string key)
    {
        if (args.ValueKind == JsonValueKind.Null || args.ValueKind == JsonValueKind.Undefined)
            return null;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Object
            ? prop
            : null;
    }

    /// <summary>
    /// Get an array from a JSON object as an enumerable of JsonElements.
    /// </summary>
    /// <param name="args">The JSON element containing the arguments.</param>
    /// <param name="key">The property name to look up.</param>
    /// <returns>An enumerable of the array elements, or null if not present or not an array.</returns>
    public static IEnumerable<JsonElement>? GetArray(JsonElement args, string key)
    {
        if (args.ValueKind == JsonValueKind.Null || args.ValueKind == JsonValueKind.Undefined)
            return null;

        if (!args.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return null;

        return EnumerateArray(prop);
    }

    // Helper to materialize the array enumeration (avoids using struct enumerator directly)
    private static IEnumerable<JsonElement> EnumerateArray(JsonElement arrayElement)
    {
        foreach (var item in arrayElement.EnumerateArray())
        {
            yield return item;
        }
    }

    /// <summary>
    /// Get a uint value from a JSON object, with a default if not present.
    /// </summary>
    /// <param name="args">The JSON element containing the arguments.</param>
    /// <param name="key">The property name to look up.</param>
    /// <param name="defaultValue">The value to return if the property is not present or not a number.</param>
    /// <returns>The uint value, or the default value.</returns>
    public static uint GetUInt(JsonElement args, string key, uint defaultValue = 0)
    {
        if (args.ValueKind == JsonValueKind.Null || args.ValueKind == JsonValueKind.Undefined)
            return defaultValue;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetUInt32()
            : defaultValue;
    }

    /// <summary>
    /// Get a long value from a JSON object, with a default if not present.
    /// </summary>
    /// <param name="args">The JSON element containing the arguments.</param>
    /// <param name="key">The property name to look up.</param>
    /// <param name="defaultValue">The value to return if the property is not present or not a number.</param>
    /// <returns>The long value, or the default value.</returns>
    public static long GetLong(JsonElement args, string key, long defaultValue = 0)
    {
        if (args.ValueKind == JsonValueKind.Null || args.ValueKind == JsonValueKind.Undefined)
            return defaultValue;

        return args.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt64()
            : defaultValue;
    }
}
