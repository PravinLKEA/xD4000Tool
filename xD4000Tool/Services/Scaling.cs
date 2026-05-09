using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace xD4000Tool.Services;

public static class Scaling
{
    public static double GetScale(string units)
    {
        if (string.IsNullOrWhiteSpace(units)) return 1.0;
        var m = Regex.Match(units.Trim(), @"^([0-9]+(?:\.[0-9]+)?)\s*");
        if (!m.Success) return 1.0;
        if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;
        return 1.0;
    }

    public static string GetUnitLabel(string units)
    {
        if (string.IsNullOrWhiteSpace(units)) return "";
        var m = Regex.Match(units.Trim(), @"^([0-9]+(?:\.[0-9]+)?)\s*(.*)$");
        if (m.Success) return m.Groups[2].Value.Trim();
        return units.Trim();
    }
}
