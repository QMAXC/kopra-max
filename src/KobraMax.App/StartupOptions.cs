namespace KobraMax.App;

/// <summary>
/// Startparameter:
///   --demo               simulierter Drucker, startet einen Beispieldruck (zum Ausprobieren)
///   --screenshot=DATEI   (nur mit --demo) nach ein paar Sekunden ein PNG speichern und beenden
///   --theme=light|dark   helles oder dunkles Design erzwingen
/// </summary>
internal sealed record StartupOptions(bool Demo, string? ScreenshotPath, string? Theme)
{
    public static StartupOptions FromCommandLine()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        string? Wert(string name) => args
            .Where(a => a.StartsWith($"--{name}=", StringComparison.OrdinalIgnoreCase))
            .Select(a => a[(name.Length + 3)..].Trim('"'))
            .LastOrDefault();

        var screenshot = Wert("screenshot");
        var demo = screenshot is not null || args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        return new StartupOptions(demo, screenshot, Wert("theme")?.ToLowerInvariant());
    }
}
