namespace KobraMax.Core;

/// <summary>Nachrichten vom Drucker-Thread an die Oberfläche.</summary>
public abstract record PrinterEvent;

public sealed record LogEvent(string Text) : PrinterEvent;

public sealed record TemperatureEvent(TemperatureReport Report) : PrinterEvent;

public sealed record ConnectionEvent(bool Connected) : PrinterEvent;

public sealed record ProgressEvent(int Done, int Total) : PrinterEvent
{
    public int Percent => Total == 0 ? 0 : (int)(100L * Done / Total);
}

public enum PrintEndReason
{
    Finished,
    Stopped,
    Error,
    ConnectionLost,
}

public sealed record PrintEndedEvent(PrintEndReason Reason) : PrinterEvent;
