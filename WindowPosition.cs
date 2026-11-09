using System;
using System.Globalization;

namespace TaskbarBatteryWidget;

internal readonly record struct WindowPosition(double Left, double Top)
{
    public string Serialize() => FormattableString.Invariant($"{Left:R},{Top:R}");

    public static bool TryParse(string text, out WindowPosition position)
    {
        position = default;
        string[] coordinates = text.Trim().Split(',');
        if (coordinates.Length != 2 ||
            !double.TryParse(coordinates[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double left) ||
            !double.TryParse(coordinates[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double top) ||
            !double.IsFinite(left) || !double.IsFinite(top))
            return false;

        position = new WindowPosition(left, top);
        return true;
    }
}
