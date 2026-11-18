using KobraMax.Core;

namespace KobraMax.Tests;

public sealed class PrinterSessionTests : IDisposable
{
    private readonly FakeMarlin _drucker = new();
    private readonly EventLog _events = new();
    private readonly ManualTimeProvider _zeit = new();
    private readonly PrinterSession _session;

    public PrinterSessionTests()
    {
        _session = new PrinterSession(_events.Add, _zeit, TimeSpan.Zero);
    }

    public void Dispose()
    {
        _session.Disconnect();
        _session.WaitForExit(TimeSpan.FromSeconds(2));
    }

    private void Verbinde()
    {
        Assert.True(_session.Connect(_drucker));
        Warte.Bis(() => _events.All.OfType<ConnectionEvent>().Any(c => c.Connected), "verbunden");
    }

    private int Anzahl(string befehl) => _drucker.Received.Count(r => r == befehl);

    [Fact]
    public void Verbinden_leert_Startmeldungen_und_meldet_verbunden()
    {
        Verbinde();
        Assert.True(_drucker.Opened);
        Assert.DoesNotContain(_events.Logs, l => l.Contains("Marlin", StringComparison.Ordinal));
    }

    [Fact]
    public void Verbindungsfehler_wird_verstaendlich_gemeldet()
    {
        _drucker.OpenError = new UnauthorizedAccessException("Zugriff verweigert");
        _session.Connect(_drucker);
        Warte.Bis(() => _events.All.OfType<ConnectionEvent>().Any(c => !c.Connected), "getrennt");
        Assert.Contains("Verbindung fehlgeschlagen: " + ErrorText.PortBelegt, _events.Logs);
        Assert.False(_session.IsActive);
    }

    [Fact]
    public void Trennen_gibt_den_Port_frei()
    {
        Verbinde();
        _session.Disconnect();
        Assert.True(_session.WaitForExit(TimeSpan.FromSeconds(2)));
        Assert.True(_drucker.Disposed);
        Assert.False(_session.IsActive);
        Assert.False(_events.All.OfType<ConnectionEvent>().Last().Connected);
    }

    [Fact]
    public void Manuelle_Befehle_werden_geloggt_M105_nicht()
    {
        Verbinde();
        _session.Enqueue("G28");
        _session.Enqueue("M105");
        Warte.Bis(() => Anzahl("M105") == 1 && _events.All.OfType<TemperatureEvent>().Any(), "Temperatur");
        Assert.Contains("> G28", _events.Logs);
        Assert.DoesNotContain("> M105", _events.Logs);
        Assert.DoesNotContain("< ok", _events.Logs);
    }

    [Fact]
    public void Unbekannter_Befehl_wird_geloggt()
    {
        Verbinde();
        _session.Enqueue("m105");
        Warte.Bis(() => _events.Logs.Contains("< echo:Unknown command: \"m105\""), "Unknown command");
    }

    [Fact]
    public void PingPong_naechste_Zeile_erst_nach_ok()
    {
        Verbinde();
        _drucker.HoldOk = true;
        _session.StartPrint(["G1 X1", "G1 X2", "G1 X3"]);
        Warte.Bis(() => Anzahl("G1 X1") == 1, "Zeile 1");
        Thread.Sleep(150);
        Assert.Equal(0, Anzahl("G1 X2"));

        _drucker.Release();
        Warte.Bis(() => Anzahl("G1 X2") == 1, "Zeile 2");
        Thread.Sleep(150);
        Assert.Equal(0, Anzahl("G1 X3"));

        _drucker.HoldOk = false;
        _drucker.Release();
        Warte.Bis(() => _events.All.OfType<PrintEndedEvent>().Any(), "Druckende");
        Assert.Equal(PrintEndReason.Finished, _events.All.OfType<PrintEndedEvent>().Single().Reason);
        Assert.Contains("Druck fertig!", _events.Logs);
        Assert.Equal(new ProgressEvent(3, 3), _events.All.OfType<ProgressEvent>().Last());
        Assert.DoesNotContain("> G1 X1", _events.Logs); // Druckzeilen werden nicht geloggt
    }

    [Fact]
    public void Error_bricht_den_Druck_ab()
    {
        Verbinde();
        _drucker.FailOn.Add("G1 X2");
        _session.StartPrint(["G1 X1", "G1 X2", "G1 X3"]);
        Warte.Bis(() => _events.All.OfType<PrintEndedEvent>().Any(), "Druckende");
        Assert.Equal(PrintEndReason.Error, _events.All.OfType<PrintEndedEvent>().Single().Reason);
        Assert.Contains("Druck wegen Fehler abgebrochen.", _events.Logs);
        Assert.Contains("< Error:Printer halted. kill() called!", _events.Logs);
        Thread.Sleep(100);
        Assert.Equal(0, Anzahl("G1 X3"));
        Assert.False(_session.IsPrinting);
    }

    [Fact]
    public void Manueller_Befehl_kommt_vor_der_naechsten_Druckzeile()
    {
        Verbinde();
        _drucker.HoldOk = true;
        _session.StartPrint(["G1 X1", "G1 X2"]);
        Warte.Bis(() => Anzahl("G1 X1") == 1, "Zeile 1");
        _session.Enqueue("M117 HALLO");
        _drucker.HoldOk = false;
        _drucker.Release();
        Warte.Bis(() => Anzahl("G1 X2") == 1, "Zeile 2");
        var r = _drucker.Received.ToList();
        Assert.True(r.IndexOf("M117 HALLO") < r.IndexOf("G1 X2"), string.Join(" | ", r));
        Assert.Equal(r.IndexOf("G1 X1") + 1, r.IndexOf("M117 HALLO"));
    }

    [Fact]
    public void Override_wird_beim_Druck_angewendet_und_S0_bleibt()
    {
        Verbinde();
        _session.NozzleTarget = 200;
        _session.BedTarget = 50;
        _session.StartPrint(["M140 S80", "M104 S170", "M109 R170", "G1 X1", "M104 S0", "M140 S0"]);
        Warte.Bis(() => _events.All.OfType<PrintEndedEvent>().Any(), "Druckende");
        var r = _drucker.Received;
        Assert.Contains("M140 S50", r);
        Assert.Contains("M104 S200", r);
        Assert.Contains("M109 R200", r);
        Assert.Contains("M104 S0", r);
        Assert.Contains("M140 S0", r);
        Assert.DoesNotContain("M104 S170", r);
        Assert.Contains("Düse: Datei wollte 170 °C, nutze 200 °C", _events.Logs);
    }

    [Fact]
    public void Waechter_korrigiert_hoechstens_alle_5_Sekunden()
    {
        Verbinde();
        _session.NozzleTarget = 200;
        _drucker.HoldOk = true; // Druck bleibt auf der ersten Zeile stehen
        _session.StartPrint(["G1 X1", "G1 X2"]);
        Warte.Bis(() => Anzahl("G1 X1") == 1, "Zeile 1");

        // Drucker meldet mehrfach Soll 170 (z. B. weil die Datei es so wollte)
        _drucker.NozzleSoll = 170;
        for (var i = 0; i < 5; i++)
        {
            _drucker.Emit(_drucker.TempLine());
        }

        Warte.Bis(() => _events.All.OfType<TemperatureEvent>().Count() >= 5, "Temperaturen");
        _drucker.HoldOk = false;
        _drucker.Release();
        Warte.Bis(() => Anzahl("M104 S200") == 1, "Korrektur");
        Thread.Sleep(100);
        Assert.Equal(1, Anzahl("M104 S200"));
        Assert.Contains("Wächter: Ziel war 170 °C, stelle zurück auf 200 °C", _events.Logs);
    }

    [Fact]
    public void Waechter_darf_nach_5_Sekunden_wieder()
    {
        Verbinde();
        _session.BedTarget = 50;
        _drucker.HoldOk = true;
        _session.StartPrint(["G1 X1", "G1 X2", "G1 X3"]);
        Warte.Bis(() => Anzahl("G1 X1") == 1, "Zeile 1");
        _drucker.BedSoll = 80;
        _drucker.Emit(_drucker.TempLine());
        _drucker.Emit(_drucker.TempLine());
        Warte.Bis(() => _events.All.OfType<TemperatureEvent>().Count() >= 2, "Temperaturen");
        Assert.Single(_events.Logs, l => l.StartsWith("Wächter", StringComparison.Ordinal));

        _zeit.Advance(TimeSpan.FromSeconds(6));
        _drucker.Emit(_drucker.TempLine());
        Warte.Bis(() => _events.Logs.Count(l => l.StartsWith("Wächter", StringComparison.Ordinal)) == 2, "zweite Korrektur");
    }

    [Fact]
    public void Waechter_schweigt_ohne_Druck_und_bei_Soll_0()
    {
        Verbinde();
        _session.NozzleTarget = 200;
        _drucker.NozzleSoll = 170;
        _session.Enqueue("M105");
        Warte.Bis(() => _events.All.OfType<TemperatureEvent>().Any(), "Temperatur");

        _drucker.HoldOk = true;
        _session.StartPrint(["G1 X1", "G1 X2"]);
        Warte.Bis(() => Anzahl("G1 X1") == 1, "Zeile 1");
        _drucker.NozzleSoll = 0;
        _drucker.Emit(_drucker.TempLine());
        Warte.Bis(() => _events.All.OfType<TemperatureEvent>().Count() >= 2, "Temperatur 2");
        Assert.DoesNotContain(_events.Logs, l => l.StartsWith("Wächter", StringComparison.Ordinal));
    }

    [Fact]
    public void Stopp_schaltet_Heizung_aus_und_faehrt_hoch()
    {
        Verbinde();
        _drucker.HoldOk = true;
        _session.StartPrint(["G1 X1", "G1 X2", "G1 X3"]);
        Warte.Bis(() => Anzahl("G1 X1") == 1, "Zeile 1");
        Assert.True(_session.Stop());
        _drucker.HoldOk = false;
        _drucker.Release();
        Warte.Bis(() => Anzahl("G90") == 1, "Stopp-Sequenz");
        var r = _drucker.Received;
        Assert.Equal(["G1 X1", .. PrinterSession.StopSequence], r);
        Assert.Equal(PrintEndReason.Stopped, _events.All.OfType<PrintEndedEvent>().Single().Reason);
        Assert.False(_session.Stop());
    }

    [Fact]
    public void Pause_haelt_an_und_Weiter_macht_weiter()
    {
        Verbinde();
        _drucker.HoldOk = true;
        _session.StartPrint(["G1 X1", "G1 X2"]);
        Warte.Bis(() => Anzahl("G1 X1") == 1, "Zeile 1");
        Assert.True(_session.TogglePause());
        _drucker.HoldOk = false;
        _drucker.Release();
        Thread.Sleep(150);
        Assert.Equal(0, Anzahl("G1 X2"));
        Assert.False(_session.TogglePause());
        Warte.Bis(() => Anzahl("G1 X2") == 1, "Zeile 2");
    }

    [Fact]
    public void Trennen_waehrend_Druck_meldet_Druckende()
    {
        Verbinde();
        _drucker.HoldOk = true;
        _session.StartPrint(["G1 X1", "G1 X2"]);
        Warte.Bis(() => Anzahl("G1 X1") == 1, "Zeile 1");
        _session.Disconnect();
        Assert.True(_session.WaitForExit(TimeSpan.FromSeconds(2)));
        Assert.Equal(PrintEndReason.ConnectionLost, _events.All.OfType<PrintEndedEvent>().Single().Reason);
        Assert.True(_drucker.Disposed);
    }
}
