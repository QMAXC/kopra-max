using System.Collections.Concurrent;
using System.Globalization;

namespace KobraMax.Core;

/// <summary>
/// Ein simulierter Kobra Max zum Ausprobieren ohne Drucker (Start mit <c>KobraMax.exe --demo</c>)
/// und für die automatischen Screenshots. Heizt langsam auf und antwortet wie Marlin.
/// </summary>
public sealed class DemoPrinter : ISerialTransport
{
    public const string PortName = "COM3";
    public const string PortDescription = "USB-SERIAL CH340 (Demo)";

    private const double Raum = 22.0;
    private readonly BlockingCollection<string> _ausgabe = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _lineDelay;
    private readonly Lock _gate = new();
    private long _letzteZeit;
    private double _duese = 24.3;
    private double _bett = 23.1;
    private double _dueseSoll;
    private double _bettSoll;

    public DemoPrinter(TimeSpan? lineDelay = null, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _lineDelay = lineDelay ?? TimeSpan.FromMilliseconds(4);
        _letzteZeit = _time.GetTimestamp();
    }

    /// <summary>Wie schnell geheizt wird (Zeitkonstante Düse; das Bett braucht doppelt so lang).</summary>
    public TimeSpan HeatTimeConstant { get; init; } = TimeSpan.FromSeconds(2.5);

    public void Open()
    {
        _ausgabe.Add("start");
        _ausgabe.Add("echo:Marlin 2.0.8 (Demo)");
    }

    public void DiscardInput()
    {
        while (_ausgabe.TryTake(out _))
        {
        }
    }

    public void WriteLine(string zeile)
    {
        Simuliere();
        var teile = zeile.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var befehl = teile.Length > 0 ? teile[0] : "";
        if (befehl.Any(char.IsLower))
        {
            _ausgabe.Add($"echo:Unknown command: \"{zeile}\"");
            _ausgabe.Add("ok");
            return;
        }

        var s = teile.Skip(1).FirstOrDefault(t => t.Length > 1 && (t[0] == 'S' || t[0] == 'R'));
        var wert = s is not null && double.TryParse(s.AsSpan(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : (double?)null;
        lock (_gate)
        {
            switch (befehl)
            {
                case "M104" or "M109":
                    _dueseSoll = wert ?? _dueseSoll;
                    break;
                case "M140" or "M190":
                    _bettSoll = wert ?? _bettSoll;
                    break;
            }
        }

        switch (befehl)
        {
            case "M105":
                _ausgabe.Add("ok " + TempZeile());
                return;
            case "G0" or "G1":
                Thread.Sleep(_lineDelay);
                break;
            case "G28":
                Thread.Sleep(TimeSpan.FromMilliseconds(600));
                break;
        }

        _ausgabe.Add("ok");
    }

    public string? ReadLine() => _ausgabe.TryTake(out var z, 200) ? z : null;

    public void Dispose() => _ausgabe.Dispose();

    private string TempZeile()
    {
        Simuliere();
        lock (_gate)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"T:{_duese:F2} /{_dueseSoll:F2} (0.00) B:{_bett:F2} /{_bettSoll:F2} (0.00) @:{(_dueseSoll > _duese ? 127 : 0)} B@:{(_bettSoll > _bett ? 127 : 0)}");
        }
    }

    private void Simuliere()
    {
        lock (_gate)
        {
            var jetzt = _time.GetTimestamp();
            var dt = _time.GetElapsedTime(_letzteZeit, jetzt).TotalSeconds;
            _letzteZeit = jetzt;
            var tau = HeatTimeConstant.TotalSeconds;
            _duese = Naehere(_duese, _dueseSoll > 0 ? _dueseSoll : Raum, dt, tau);
            _bett = Naehere(_bett, _bettSoll > 0 ? _bettSoll : Raum, dt, tau * 2);
        }
    }

    private static double Naehere(double ist, double ziel, double dt, double tau) =>
        ziel + (ist - ziel) * Math.Exp(-dt / Math.Max(tau, 0.001));

    /// <summary>Ein kleiner Würfel als Beispiel-Druckauftrag (bereits gesäubert).</summary>
    public static IReadOnlyList<string> SampleGcode()
    {
        var z = new List<string> { "M140 S60", "M104 S200", "G90", "M83", "G28", "G92 E0" };
        for (var schicht = 1; schicht <= 100; schicht++)
        {
            z.Add(string.Create(CultureInfo.InvariantCulture, $"G1 Z{schicht * 0.2:F2} F600"));
            for (var i = 0; i < 40; i++)
            {
                var x = 190 + (i % 2 == 0 ? 0 : 20);
                var y = 190 + (i * 0.5);
                z.Add(string.Create(CultureInfo.InvariantCulture, $"G1 X{x} Y{y:F2} E0.65 F2400"));
            }
        }

        z.AddRange(["M104 S0", "M140 S0", "G91", "G1 Z10 F600", "G90", "M84"]);
        return z;
    }
}
