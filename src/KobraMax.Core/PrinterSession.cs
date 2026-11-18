using System.Collections.Concurrent;
using static System.FormattableString;

namespace KobraMax.Core;

/// <summary>
/// Spricht in einem eigenen Thread mit dem Drucker (Ping-Pong: ein Befehl, dann auf "ok" warten).
/// Alle öffentlichen Methoden dürfen von jedem Thread aus aufgerufen werden.
/// Meldungen kommen über den Callback – auf dem Drucker-Thread!
/// </summary>
public sealed class PrinterSession
{
    public static readonly TimeSpan DefaultResetDelay = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Wird nach "Stopp" gesendet: Heizungen aus, Düse 10 mm hoch.</summary>
    public static readonly IReadOnlyList<string> StopSequence = ["M104 S0", "M140 S0", "G91", "G1 Z10 F600", "G90"];

    private readonly Action<PrinterEvent> _report;
    private readonly TimeProvider _time;
    private readonly TimeSpan _resetDelay;
    private readonly ConcurrentQueue<string> _commands = new();

    // Zustand, geschützt durch _gate
    private readonly Lock _gate = new();
    private IReadOnlyList<string> _lines = [];
    private int _pos;
    private bool _printing;
    private bool _paused;
    private int? _nozzleTarget;
    private int? _bedTarget;
    private long? _lastNozzleFix;
    private long? _lastBedFix;
    private long _lastProgress;

    private Thread? _worker;
    private CancellationTokenSource? _cts;
    private volatile bool _active;
    private volatile bool _sending;

    public PrinterSession(Action<PrinterEvent> report, TimeProvider? time = null, TimeSpan? resetDelay = null)
    {
        _report = report;
        _time = time ?? TimeProvider.System;
        _resetDelay = resetDelay ?? DefaultResetDelay;
    }

    /// <summary>Verbindung offen oder im Aufbau.</summary>
    public bool IsActive => _active;

    public bool IsPrinting
    {
        get { lock (_gate) { return _printing; } }
    }

    public bool IsPaused
    {
        get { lock (_gate) { return _paused; } }
    }

    /// <summary>Keine manuellen Befehle mehr in der Warteschlange und keiner unterwegs.</summary>
    public bool IsIdle => _commands.IsEmpty && !_sending;

    public bool HasQueuedCommands => !_commands.IsEmpty;

    /// <summary>Eigene Düsentemperatur. null = Wert aus der Datei verwenden.</summary>
    public int? NozzleTarget
    {
        get { lock (_gate) { return _nozzleTarget; } }
        set { lock (_gate) { _nozzleTarget = value; } }
    }

    /// <summary>Eigene Betttemperatur. null = Wert aus der Datei verwenden.</summary>
    public int? BedTarget
    {
        get { lock (_gate) { return _bedTarget; } }
        set { lock (_gate) { _bedTarget = value; } }
    }

    /// <summary>Manueller Befehl. Wird vor der nächsten Druckzeile gesendet.</summary>
    public void Enqueue(string befehl) => _commands.Enqueue(befehl);

    /// <summary>Startet den Drucker-Thread. Gibt false zurück, wenn schon verbunden.</summary>
    public bool Connect(ISerialTransport transport)
    {
        if (_active)
        {
            return false;
        }

        _commands.Clear(); // keine alten Befehle aus einer früheren Verbindung senden
        _active = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _worker = new Thread(() => Run(transport, token)) { IsBackground = true, Name = "Drucker" };
        _worker.Start();
        return true;
    }

    /// <summary>Trennen anstoßen (blockiert nicht). Der Port wird vom Drucker-Thread geschlossen.</summary>
    public void Disconnect() => _cts?.Cancel();

    /// <summary>Wartet, bis der Drucker-Thread beendet und der Port freigegeben ist.</summary>
    public bool WaitForExit(TimeSpan timeout)
    {
        var w = _worker;
        return w is null || w == Thread.CurrentThread || w.Join(timeout);
    }

    public bool StartPrint(IReadOnlyList<string> zeilen)
    {
        lock (_gate)
        {
            if (_printing)
            {
                return false;
            }

            _lines = zeilen;
            _pos = 0;
            _paused = false;
            _printing = true;
            _lastNozzleFix = null;
            _lastBedFix = null;
            _lastProgress = 0;
            return true;
        }
    }

    /// <summary>Pause an/aus. Gibt den neuen Zustand zurück (null = es läuft kein Druck).</summary>
    public bool? TogglePause()
    {
        lock (_gate)
        {
            if (!_printing)
            {
                return null;
            }

            _paused = !_paused;
            return _paused;
        }
    }

    /// <summary>Druck abbrechen, dann Heizungen aus und Düse hoch.</summary>
    public bool Stop()
    {
        lock (_gate)
        {
            if (!_printing)
            {
                return false;
            }

            _printing = false;
            _paused = false;
        }

        foreach (var b in StopSequence)
        {
            _commands.Enqueue(b);
        }

        _report(new PrintEndedEvent(PrintEndReason.Stopped));
        return true;
    }

    // ------------------------------------------------------------ Drucker-Thread

    private void Run(ISerialTransport t, CancellationToken ct)
    {
        var verbunden = false;
        try
        {
            try
            {
                t.Open();
            }
            catch (Exception e)
            {
                Log("Verbindung fehlgeschlagen: " + ErrorText.Describe(e));
                return;
            }

            Log("Verbinde … (Drucker startet neu, 3 Sekunden)");
            if (ct.WaitHandle.WaitOne(_resetDelay))
            {
                return;
            }

            t.DiscardInput();
            verbunden = true;
            _report(new ConnectionEvent(true));

            while (!ct.IsCancellationRequested)
            {
                var (befehl, ausDatei) = NextCommand();
                if (befehl is null)
                {
                    ReadOne(t); // Leerlauf: Meldungen des Druckers anzeigen
                }
                else
                {
                    Send(t, befehl, ausDatei, ct);
                }
            }
        }
        catch (Exception e)
        {
            Log((verbunden ? "Verbindung verloren: " : "Verbindung fehlgeschlagen: ") + ErrorText.Describe(e));
        }
        finally
        {
            try
            {
                t.Dispose(); // gibt den Port frei
            }
            catch (Exception)
            {
                // Port war schon weg (z. B. Kabel gezogen) – nichts mehr zu tun.
            }

            bool druckteNoch;
            lock (_gate)
            {
                druckteNoch = _printing;
                _printing = false;
                _paused = false;
            }

            _sending = false;
            _active = false;
            if (druckteNoch)
            {
                _report(new PrintEndedEvent(PrintEndReason.ConnectionLost));
            }

            _report(new ConnectionEvent(false));
        }
    }

    private (string? Befehl, bool AusDatei) NextCommand()
    {
        if (_commands.TryDequeue(out var manuell))
        {
            return (manuell, false);
        }

        string roh;
        int fertig, gesamt;
        int? duese, bett;
        bool fortschrittMelden;
        lock (_gate)
        {
            if (!_printing || _paused)
            {
                return (null, false);
            }

            if (_pos >= _lines.Count)
            {
                _printing = false;
                roh = "";
                fertig = gesamt = -1;
                duese = bett = null;
                fortschrittMelden = false;
            }
            else
            {
                roh = _lines[_pos++];
                fertig = _pos;
                gesamt = _lines.Count;
                duese = _nozzleTarget;
                bett = _bedTarget;
                var jetzt = _time.GetTimestamp();
                fortschrittMelden = fertig == 1 || fertig == gesamt || _time.GetElapsedTime(_lastProgress, jetzt) >= ProgressInterval;
                if (fortschrittMelden)
                {
                    _lastProgress = jetzt;
                }
            }
        }

        if (fertig < 0)
        {
            Log("Druck fertig!");
            _report(new PrintEndedEvent(PrintEndReason.Finished));
            return (null, false);
        }

        var ergebnis = TemperatureOverride.Apply(roh, duese, bett);
        if (ergebnis.Log is not null)
        {
            Log(ergebnis.Log);
        }

        if (fortschrittMelden)
        {
            _report(new ProgressEvent(fertig, gesamt));
        }

        return (ergebnis.Line, true);
    }

    private void Send(ISerialTransport t, string befehl, bool ausDatei, CancellationToken ct)
    {
        // Druckzeilen nicht einzeln loggen (zu viele), manuelle Befehle schon – außer der Temperaturabfrage.
        if (!ausDatei && befehl != "M105")
        {
            Log("> " + befehl);
        }

        _sending = true;
        try
        {
            t.WriteLine(befehl);
            while (!ct.IsCancellationRequested)
            {
                if (ReadOne(t))
                {
                    return;
                }
            }
        }
        finally
        {
            _sending = false;
        }
    }

    /// <summary>Liest eine Zeile. Gibt true zurück, wenn der gesendete Befehl erledigt ist.</summary>
    private bool ReadOne(ISerialTransport t)
    {
        var z = t.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(z))
        {
            return false;
        }

        if (TemperatureReport.TryParse(z, out var temp))
        {
            _report(new TemperatureEvent(temp));
            Watchdog(temp);
        }
        else if (z != "ok")
        {
            Log("< " + z);
        }

        if (z.StartsWith("Error", StringComparison.Ordinal))
        {
            bool abgebrochen;
            lock (_gate)
            {
                abgebrochen = _printing;
                _printing = false;
                _paused = false;
            }

            if (abgebrochen)
            {
                Log("Druck wegen Fehler abgebrochen.");
                _report(new PrintEndedEvent(PrintEndReason.Error));
            }

            return true;
        }

        return z.StartsWith("ok", StringComparison.Ordinal);
    }

    /// <summary>Stellt beim Druck die eigenen Temperaturen wieder her, falls die Datei sie geändert hat.</summary>
    private void Watchdog(TemperatureReport r)
    {
        var jetzt = _time.GetTimestamp();
        var korrekturen = new List<(string Befehl, string Log)>(2);
        lock (_gate)
        {
            if (!_printing)
            {
                return;
            }

            Pruefe(_nozzleTarget, r.NozzleSoll, "M104", ref _lastNozzleFix);
            Pruefe(_bedTarget, r.BedSoll, "M140", ref _lastBedFix);
        }

        foreach (var (befehl, text) in korrekturen)
        {
            Log(text);
            _commands.Enqueue(befehl);
        }

        void Pruefe(int? ziel, double soll, string code, ref long? letzte)
        {
            if (ziel is not int z || soll <= 0 || Math.Abs(soll - z) <= 1)
            {
                return;
            }

            if (letzte is long l && _time.GetElapsedTime(l, jetzt) <= WatchdogInterval)
            {
                return;
            }

            letzte = jetzt;
            korrekturen.Add((Invariant($"{code} S{z}"), Invariant($"Wächter: Ziel war {soll:F0} °C, stelle zurück auf {z} °C")));
        }
    }

    private void Log(string text) => _report(new LogEvent(text));
}
