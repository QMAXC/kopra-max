using System.Text;
using System.Text.RegularExpressions;

namespace KobraMax.Core;

/// <summary>Laden und Säubern von G-Code-Dateien.</summary>
public static partial class GcodeFile
{
    // Zeilennummern (N123) vorne und Prüfsummen (*45) hinten – der Drucker braucht sie nicht.
    [GeneratedRegex(@"^N\d+\s*|\*\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex NummerUndPruefsumme();

    /// <summary>Kommentare (ab ";") und Leerzeilen weg, N123 und *45 entfernen.</summary>
    public static IReadOnlyList<string> Clean(IEnumerable<string> rohZeilen)
    {
        var ergebnis = new List<string>();
        foreach (var roh in rohZeilen)
        {
            var semikolon = roh.IndexOf(';', StringComparison.Ordinal);
            var z = (semikolon >= 0 ? roh[..semikolon] : roh).Trim();
            z = NummerUndPruefsumme().Replace(z, "").Trim();
            if (z.Length > 0)
            {
                ergebnis.Add(z);
            }
        }

        return ergebnis;
    }

    public static IReadOnlyList<string> Load(string pfad) =>
        Clean(File.ReadLines(pfad, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)));

    /// <summary>.3mf ist eine gezippte Projektdatei des Slicers, kein G-Code.</summary>
    public static bool IsProjectFile(string pfad) =>
        pfad.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase);
}
