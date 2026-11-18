using System.Collections.Concurrent;
using System.Globalization;
using KobraMax.Core;

namespace KobraMax.Tests;

/// <summary>Simulierter Marlin-Drucker: antwortet auf Befehle wie der echte Kobra Max.</summary>
internal sealed class FakeMarlin : ISerialTransport
{
    private readonly BlockingCollection<string> _ausgabe = new();
    private readonly ConcurrentQueue<string> _received = new();
    private readonly Lock _gate = new();
    private readonly List<string> _zurueckgehalten = [];

    public double NozzleIst { get; set; } = 25;
    public double NozzleSoll { get; set; }
    public double BedIst { get; set; } = 22;
    public double BedSoll { get; set; }

    /// <summary>Wenn true, hält der Drucker das "ok" zurück, bis <see cref="Release"/> gerufen wird.</summary>
    public bool HoldOk { get; set; }

    /// <summary>Zeilen, auf die der Drucker mit einem Fehler antwortet.</summary>
    public HashSet<string> FailOn { get; } = new(StringComparer.Ordinal);

    public bool Opened { get; private set; }
    public bool Disposed { get; private set; }
    public Exception? OpenError { get; set; }

    public IReadOnlyList<string> Received => _received.ToArray();

    public void Open()
    {
        if (OpenError is not null)
        {
            throw OpenError;
        }

        Opened = true;
        _ausgabe.Add("start");
        _ausgabe.Add("echo:Marlin 2.0");
    }

    public void DiscardInput()
    {
        while (_ausgabe.TryTake(out _))
        {
        }
    }

    public void WriteLine(string zeile)
    {
        _received.Enqueue(zeile);
        foreach (var antwort in Antworten(zeile))
        {
            lock (_gate)
            {
                if (HoldOk && antwort.StartsWith("ok", StringComparison.Ordinal))
                {
                    _zurueckgehalten.Add(antwort);
                    continue;
                }
            }

            _ausgabe.Add(antwort);
        }
    }

    /// <summary>Gibt zurückgehaltene "ok" frei.</summary>
    public void Release()
    {
        lock (_gate)
        {
            foreach (var a in _zurueckgehalten)
            {
                _ausgabe.Add(a);
            }

            _zurueckgehalten.Clear();
        }
    }

    /// <summary>Eine unaufgeforderte Zeile vom Drucker.</summary>
    public void Emit(string zeile) => _ausgabe.Add(zeile);

    public string? ReadLine() => _ausgabe.TryTake(out var z, 20) ? z + "\r" : null;

    public void Dispose() => Disposed = true;

    public string TempLine() => string.Create(CultureInfo.InvariantCulture,
        $"T:{NozzleIst:F2} /{NozzleSoll:F2} (200.00) B:{BedIst:F2} /{BedSoll:F2} (3000.00) @:0 B@:0");

    private IEnumerable<string> Antworten(string zeile)
    {
        if (FailOn.Contains(zeile))
        {
            yield return "Error:Printer halted. kill() called!";
            yield break;
        }

        var teile = zeile.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var befehl = teile.Length > 0 ? teile[0] : "";
        if (befehl.Any(char.IsLower))
        {
            yield return $"echo:Unknown command: \"{zeile}\"";
            yield return "ok";
            yield break;
        }

        double? s = null;
        foreach (var t in teile.Skip(1))
        {
            if ((t[0] == 'S' || t[0] == 'R') && double.TryParse(t[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                s = v;
                break;
            }
        }

        switch (befehl)
        {
            case "M105":
                yield return "ok " + TempLine();
                yield break;
            case "M104" or "M109":
                NozzleSoll = s ?? NozzleSoll;
                break;
            case "M140" or "M190":
                BedSoll = s ?? BedSoll;
                break;
        }

        if (befehl is "M109" or "M190")
        {
            yield return "echo:busy: processing";
            yield return " " + TempLine();
        }

        yield return "ok";
    }
}

/// <summary>Uhr, die nur weiterläuft, wenn der Test es sagt.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks = 1_000_000;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public void Advance(TimeSpan t) => Interlocked.Add(ref _ticks, t.Ticks);
}

/// <summary>Sammelt Ereignisse des Drucker-Threads (thread-sicher).</summary>
internal sealed class EventLog
{
    private readonly ConcurrentQueue<PrinterEvent> _events = new();

    public void Add(PrinterEvent e) => _events.Enqueue(e);

    public IReadOnlyList<PrinterEvent> All => _events.ToArray();

    public IEnumerable<string> Logs => All.OfType<LogEvent>().Select(l => l.Text);
}

internal static class Warte
{
    public static void Bis(Func<bool> bedingung, string was, int ms = 5000)
    {
        var ende = Environment.TickCount64 + ms;
        while (!bedingung())
        {
            if (Environment.TickCount64 > ende)
            {
                throw new TimeoutException("Gewartet auf: " + was);
            }

            Thread.Sleep(5);
        }
    }
}
