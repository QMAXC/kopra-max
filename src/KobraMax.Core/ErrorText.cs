namespace KobraMax.Core;

/// <summary>Übersetzt technische Fehler in verständliche deutsche Meldungen.</summary>
public static class ErrorText
{
    public const string PortBelegt = "Port belegt – ist das Programm noch woanders offen?";
    public const string KeineAntwort = "Drucker antwortet nicht – eingeschaltet? Kabel ab- und wieder anstecken, dann Aktualisieren.";
    public const string PortFehlt = "Port nicht gefunden – Kabel eingesteckt? Dann Aktualisieren drücken.";

    private const int ErrorFileNotFound = 2;
    private const int ErrorAccessDenied = 5;
    private const int ErrorGenFailure = 31;
    private const int ErrorSemTimeout = 121;
    private const int ErrorDeviceNotConnected = 1167;

    public static string Describe(Exception e)
    {
        switch (e)
        {
            case UnauthorizedAccessException:
                return PortBelegt;
            case TimeoutException:
                return KeineAntwort;
            case FileNotFoundException:
                return PortFehlt;
            case IOException io:
                return Win32Code(io) switch
                {
                    ErrorAccessDenied => PortBelegt,
                    ErrorFileNotFound => PortFehlt,
                    ErrorSemTimeout or ErrorGenFailure or ErrorDeviceNotConnected => KeineAntwort,
                    _ when io.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) => PortFehlt,
                    _ => $"{KeineAntwort} ({io.Message})",
                };
            default:
                return e.Message;
        }
    }

    /// <summary>Holt den Windows-Fehlercode aus einem HRESULT der Form 0x8007xxxx.</summary>
    private static int? Win32Code(Exception e) =>
        (e.HResult & 0xFFFF0000) == 0x80070000 ? e.HResult & 0xFFFF : null;
}
