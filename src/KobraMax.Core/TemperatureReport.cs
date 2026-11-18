using System.Globalization;
using System.Text.RegularExpressions;

namespace KobraMax.Core;

/// <summary>Eine Temperaturmeldung des Druckers, z. B. <c>ok T:230.16 /230.00 B:60.01 /60.00</c>.</summary>
public readonly partial record struct TemperatureReport(double NozzleIst, double NozzleSoll, double BedIst, double BedSoll)
{
    [GeneratedRegex(@"T:\s*([\d.]+)\s*/\s*([\d.]+).*?B:\s*([\d.]+)\s*/\s*([\d.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex Muster();

    /// <summary>Liest Düse/Bett (ist/soll) aus einer Druckerzeile. Gibt false zurück, wenn die Zeile keine Temperaturen enthält.</summary>
    public static bool TryParse(string zeile, out TemperatureReport report)
    {
        report = default;
        var m = Muster().Match(zeile);
        if (!m.Success)
        {
            return false;
        }

        // Immer Punkt als Dezimaltrenner – auf deutschen Windows-Rechnern wäre sonst "230.16" falsch.
        Span<double> werte = stackalloc double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(m.Groups[i + 1].ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out werte[i]))
            {
                return false;
            }
        }

        report = new TemperatureReport(werte[0], werte[1], werte[2], werte[3]);
        return true;
    }
}
