using System.Globalization;

namespace KobraMax.App;

/// <summary>Schreibt unerwartete Fehler in "KobraMax-Fehler.log" neben der .exe (hilft bei der Fehlersuche).</summary>
internal static class CrashLog
{
    public static void Write(Exception e)
    {
        try
        {
            var pfad = Path.Combine(AppContext.BaseDirectory, "KobraMax-Fehler.log");
            File.AppendAllText(pfad, $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] {e}\r\n\r\n");
        }
        catch (Exception)
        {
            // Ordner nicht beschreibbar – dann eben nicht.
        }
    }
}
