using System.Globalization;

namespace KobraMax.App;

/// <summary>Ablauf-Protokoll für den Screenshot-Modus (KobraMax-trace.log neben der .exe).</summary>
internal static class Diag
{
    public static bool Enabled { get; set; }

    public static void Log(string text)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "KobraMax-trace.log"),
                $"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {text}\r\n");
        }
        catch (IOException)
        {
        }
    }
}
