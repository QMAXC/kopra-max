using System.Globalization;
using System.Text.RegularExpressions;

namespace KobraMax.Core;

public enum Heater
{
    Nozzle,
    Bed,
}

/// <summary>Ergebnis von <see cref="TemperatureOverride.Apply"/>: die (evtl. geänderte) Zeile und ggf. eine Log-Meldung.</summary>
public readonly record struct OverrideResult(string Line, string? Log)
{
    public bool Changed => Log is not null;
}

/// <summary>
/// Ersetzt Heizbefehle aus der G-Code-Datei durch die eigenen Temperaturen ("Override").
/// Reine Funktion ohne Zustand.
/// </summary>
public static partial class TemperatureOverride
{
    private static readonly HashSet<string> DuesenBefehle = new(StringComparer.Ordinal) { "M104", "M109", "G10", "M568" };
    private static readonly HashSet<string> BettBefehle = new(StringComparer.Ordinal) { "M140", "M190" };

    [GeneratedRegex(@"^\s*(?:N\d+\s*)?([GMT]\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BefehlMuster();

    // Erster S- oder R-Parameter (z. B. "M109 R170" kommt real vor), nicht Teil eines Wortes.
    [GeneratedRegex(@"(?<![A-Za-z])([SR])([\d.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WertMuster();

    /// <summary>Welcher Heizer wird von dieser Zeile gesetzt? null = kein Heizbefehl.</summary>
    public static Heater? HeaterOf(string zeile)
    {
        var m = BefehlMuster().Match(zeile);
        if (!m.Success)
        {
            return null;
        }

        var befehl = m.Groups[1].Value.ToUpperInvariant();
        if (DuesenBefehle.Contains(befehl))
        {
            return Heater.Nozzle;
        }

        return BettBefehle.Contains(befehl) ? Heater.Bed : null;
    }

    /// <param name="zeile">Eine bereits gesäuberte G-Code-Zeile.</param>
    /// <param name="duese">Eigene Düsentemperatur oder null (= Wert aus Datei).</param>
    /// <param name="bett">Eigene Betttemperatur oder null (= Wert aus Datei).</param>
    public static OverrideResult Apply(string zeile, int? duese, int? bett)
    {
        var heizer = HeaterOf(zeile);
        if (heizer is null)
        {
            return new OverrideResult(zeile, null);
        }

        var (wert, name) = heizer == Heater.Nozzle ? (duese, "Düse") : (bett, "Bett");
        if (wert is not int eigen)
        {
            return new OverrideResult(zeile, null);
        }

        var m = WertMuster().Match(zeile);
        if (!m.Success)
        {
            return new OverrideResult(zeile, null);
        }

        var alt = m.Groups[2];
        if (!double.TryParse(alt.ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var altWert) || altWert == 0)
        {
            return new OverrideResult(zeile, null); // "Heizung aus" (S0) am Druckende muss bleiben
        }

        if (altWert == eigen)
        {
            return new OverrideResult(zeile, null); // Datei will ohnehin den eigenen Wert – nichts zu tun
        }

        var neuText = eigen.ToString(CultureInfo.InvariantCulture);
        var neu = string.Concat(zeile.AsSpan(0, alt.Index), neuText, zeile.AsSpan(alt.Index + alt.Length));
        return new OverrideResult(neu, $"{name}: Datei wollte {alt.Value} °C, nutze {neuText} °C");
    }
}
