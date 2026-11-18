using System.Collections.Concurrent;
using KobraMax.Core;

namespace KobraMax.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly ConcurrentQueue<Action> _ui = new();
    private readonly FakeMarlin _drucker = new();
    private readonly MainViewModel _vm;

    public MainViewModelTests()
    {
        _vm = new MainViewModel(
            () => [new PortInfo("COM1", "Kommunikationsanschluss"), new PortInfo("COM3", "USB-SERIAL CH340")],
            (_, _) => _drucker,
            _ui.Enqueue,
            new ManualTimeProvider(),
            TimeSpan.Zero);
        _vm.RefreshPorts();
    }

    public void Dispose()
    {
        _vm.Session.Disconnect();
        _vm.Session.WaitForExit(TimeSpan.FromSeconds(2));
    }

    /// <summary>Spielt den UI-Thread: arbeitet Meldungen ab, bis die Bedingung stimmt.</summary>
    private void PumpeBis(Func<bool> bedingung, string was)
    {
        Warte.Bis(() =>
        {
            while (_ui.TryDequeue(out var a))
            {
                a();
            }

            return bedingung();
        }, was);
    }

    private void Verbinde()
    {
        _vm.ToggleConnection(_vm.DefaultPortIndex, 115200);
        Assert.Equal(ConnectionState.Connecting, _vm.Connection);
        PumpeBis(() => _vm.IsConnected, "verbunden");
    }

    private string LetzteMeldung => _vm.LogLines[^1];

    [Fact]
    public void CH340_ist_vorausgewaehlt()
    {
        Assert.Equal(1, _vm.DefaultPortIndex);
        Assert.Equal("COM3 – USB-SERIAL CH340", _vm.Ports[1].Display);
    }

    [Theory]
    [InlineData("abc", "Bitte eine ganze Zahl als Temperatur eingeben, z. B. 200.")]
    [InlineData("200.5", "Bitte eine ganze Zahl als Temperatur eingeben, z. B. 200.")]
    [InlineData("0", "Bitte einen Wert zwischen 1 und 260 °C eingeben.")]
    [InlineData("261", "Bitte einen Wert zwischen 1 und 260 °C eingeben.")]
    [InlineData("200", "Nicht verbunden.")]
    public void Heizen_prueft_Eingabe(string eingabe, string meldung)
    {
        _vm.Heat(Heater.Nozzle, eingabe);
        Assert.Equal(meldung, LetzteMeldung);
        Assert.Null(_vm.Session.NozzleTarget);
        Assert.Equal(MainViewModel.FromFileText, _vm.NozzleTargetText);
    }

    [Fact]
    public void Bett_Grenze_ist_110()
    {
        _vm.Heat(Heater.Bed, "111");
        Assert.Equal("Bitte einen Wert zwischen 1 und 110 °C eingeben.", LetzteMeldung);
    }

    [Fact]
    public void Heizen_setzt_Override_und_Aus_loescht_ihn()
    {
        Verbinde();
        _vm.Heat(Heater.Nozzle, " 210 ");
        Assert.Equal(210, _vm.Session.NozzleTarget);
        Assert.Equal("Druck: 210 °C", _vm.NozzleTargetText);
        PumpeBis(() => _drucker.Received.Contains("M104 S210"), "M104 S210");

        _vm.HeaterOff(Heater.Nozzle);
        Assert.Null(_vm.Session.NozzleTarget);
        Assert.Equal(MainViewModel.FromFileText, _vm.NozzleTargetText);
        PumpeBis(() => _drucker.Received.Contains("M104 S0"), "M104 S0");
    }

    [Fact]
    public void Temperatur_wird_angezeigt()
    {
        Verbinde();
        _drucker.NozzleIst = 199.6;
        _drucker.NozzleSoll = 200;
        _vm.PollTemperature();
        PumpeBis(() => _vm.NozzleText == "Düse: 200 / 200 °C", "Anzeige");
        Assert.Equal("Bett: 22 / 0 °C", _vm.BedText);
    }

    [Fact]
    public void Konsole_sendet_Grossbuchstaben()
    {
        Verbinde();
        Assert.True(_vm.SendInput("  m105 "));
        Assert.False(_vm.SendInput("   "));
        PumpeBis(() => _drucker.Received.Contains("M105"), "M105");
        Assert.DoesNotContain("m105", _drucker.Received);
    }

    [Fact]
    public void Fahren_relativ_und_gesperrt_waehrend_Druck()
    {
        Verbinde();
        _vm.Jog('Z', -1, 10);
        _vm.Jog('X', 1, 50);
        PumpeBis(() => _drucker.Received.Count(r => r == "G90") == 2, "Fahrbefehle");
        Assert.Contains("G1 Z-10 F600", _drucker.Received);
        Assert.Contains("G1 X50 F3000", _drucker.Received);

        var pfad = Path.Combine(Path.GetTempPath(), $"kobra_{Guid.NewGuid():N}.gcode");
        File.WriteAllLines(pfad, ["G28", "G1 X1", "G1 X2"]);
        try
        {
            _drucker.HoldOk = true;
            _vm.LoadFile(pfad);
            Assert.Equal("3 Zeilen", _vm.ProgressText);
            _vm.StartPrint();
            Assert.True(_vm.IsPrinting);
            _vm.Home();
            Assert.Equal("Während des Drucks ist Bewegen gesperrt.", LetzteMeldung);

            _vm.StopPrint();
            Assert.Equal("Druck gestoppt, Heizung aus.", LetzteMeldung);
            _drucker.HoldOk = false;
            _drucker.Release();
            PumpeBis(() => !_vm.IsPrinting, "Druckende");
        }
        finally
        {
            File.Delete(pfad);
        }
    }

    [Fact]
    public void Druck_zeigt_Fortschritt()
    {
        Verbinde();
        var pfad = Path.Combine(Path.GetTempPath(), $"kobra_{Guid.NewGuid():N}.gcode");
        File.WriteAllLines(pfad, ["; Kopf", "G28", "N3 G1 X1*33", "G1 X2"]);
        try
        {
            _vm.LoadFile(pfad);
            _vm.StartPrint();
            PumpeBis(() => !_vm.IsPrinting, "Druckende");
            Assert.Equal("3 / 3 Zeilen (100 %)", _vm.ProgressText);
            Assert.Equal(100, _vm.ProgressValue);
            Assert.Contains("Druck fertig!", _vm.LogLines);
            Assert.Contains("G1 X1", _drucker.Received);
        }
        finally
        {
            File.Delete(pfad);
        }
    }

    [Fact]
    public void Datei_3mf_wird_abgelehnt()
    {
        _vm.LoadFile(@"H:\3D\teil.3mf");
        Assert.False(_vm.HasFile);
        Assert.Contains(".3mf", LetzteMeldung, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_ohne_Verbindung_oder_Datei()
    {
        _vm.StartPrint();
        Assert.Equal("Nicht verbunden.", LetzteMeldung);
        Verbinde();
        _vm.StartPrint();
        Assert.Equal("Erst eine Datei öffnen.", LetzteMeldung);
    }

    [Fact]
    public async Task Schliessen_waehrend_Druck_stoppt_und_gibt_Port_frei()
    {
        Verbinde();
        var pfad = Path.Combine(Path.GetTempPath(), $"kobra_{Guid.NewGuid():N}.gcode");
        File.WriteAllLines(pfad, Enumerable.Range(1, 100_000).Select(i => $"G1 X{i % 100}"));
        try
        {
            _vm.LoadFile(pfad);
            _vm.StartPrint();
            await _vm.ShutdownAsync();
            Assert.True(_drucker.Disposed);
            Assert.Contains("M104 S0", _drucker.Received);
            Assert.Contains("M140 S0", _drucker.Received);
        }
        finally
        {
            File.Delete(pfad);
        }
    }

    [Fact]
    public void Log_bleibt_unter_500_Zeilen()
    {
        for (var i = 0; i < 1234; i++)
        {
            _vm.Log($"Zeile {i}");
        }

        Assert.True(_vm.LogLines.Count <= MainViewModel.MaxLogLines);
        Assert.Equal("Zeile 1233", LetzteMeldung);
    }
}
