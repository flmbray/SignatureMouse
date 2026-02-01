using System;
using System.Collections.Generic;
using System.Globalization;

namespace SignatureMouse.Cli;

internal sealed class ArgParser
{
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

    public ArgParser(string[] args)
    {
        Parse(args);
    }

    private void Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                _options[$"__arg{i}"] = arg;
                continue;
            }

            var trimmed = arg.TrimStart('-');
            var split = trimmed.Split('=', 2, StringSplitOptions.TrimEntries);
            var key = split[0];

            if (split.Length == 2)
            {
                _options[key] = split[1];
                continue;
            }

            if (i + 1 < args.Length && (!args[i + 1].StartsWith("-", StringComparison.Ordinal) || LooksLikeNumber(args[i + 1])))
            {
                _options[key] = args[i + 1];
                i++;
            }
            else
            {
                _options[key] = "true";
            }
        }
    }

    public bool Has(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (_options.ContainsKey(key)) return true;
        }
        return false;
    }

    public string? Get(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (_options.TryGetValue(key, out var value)) return value;
        }
        return null;
    }

    public int GetInt(int defaultValue, params string[] keys)
    {
        var value = Get(keys);
        if (value != null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return defaultValue;
    }

    public float GetFloat(float defaultValue, params string[] keys)
    {
        var value = Get(keys);
        if (value != null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return defaultValue;
    }

    public double GetDouble(double defaultValue, params string[] keys)
    {
        var value = Get(keys);
        if (value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return defaultValue;
    }

    private static bool LooksLikeNumber(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }
}
