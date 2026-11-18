using System.Globalization;
using KobraMax.Core;

namespace KobraMax.Tests;

public class ParserTests
{
    [Fact]
    public void Temperaturzeile_wird_gelesen()
    {
        Assert.True(TemperatureReport.TryParse("ok T:230.16 /230.00 (203.50) B:60.01 /60.00 (3427.88) @:42 B@:23", out var r));
        Assert.Equal(new TemperatureReport(230.16, 230.00, 60.01, 60.00), r);
    }

    [Fact]
    public void Temperatur_auch_mit_deutscher_Kultur()
    {
        var alt = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.True(TemperatureReport.TryParse(" T:200.5 /200.0 B:50.2 /50.0 @:0", out var r));
            Assert.Equal(200.5, r.NozzleIst);
        }
        finally
        {
            CultureInfo.CurrentCulture = alt;
        }
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("echo:busy: processing")]
    [InlineData("echo:Unknown command: \"m105\"")]
    public void Keine_Temperaturzeile(string zeile)
    {
        Assert.False(TemperatureReport.TryParse(zeile, out _));
    }

    [Fact]
    public void GcodeDatei_wird_gesaeubert()
    {
        var roh = new[]
        {
            "; Kommentar",
            "",
            "   ",
            "G28 ; nach Hause",
            "N12 G1 X10 Y10*87",
            "M104 S200\r",
            ";LAYER:1",
        };
        Assert.Equal(["G28", "G1 X10 Y10", "M104 S200"], GcodeFile.Clean(roh));
    }

    [Fact]
    public void GcodeDatei_wird_geladen()
    {
        var pfad = Path.Combine(Path.GetTempPath(), $"kobra_{Guid.NewGuid():N}.gcode");
        File.WriteAllText(pfad, "; Ä Kommentar\r\nG28\r\nM109 S210 ; warten\r\n");
        try
        {
            Assert.Equal(["G28", "M109 S210"], GcodeFile.Load(pfad));
        }
        finally
        {
            File.Delete(pfad);
        }
    }

    [Theory]
    [InlineData("teil.3mf", true)]
    [InlineData("teil.gcode.3MF", true)]
    [InlineData("teil.gcode", false)]
    public void Projektdatei_wird_erkannt(string pfad, bool erwartet)
    {
        Assert.Equal(erwartet, GcodeFile.IsProjectFile(pfad));
    }

    [Fact]
    public void CH340_wird_vorausgewaehlt()
    {
        var ports = PortInfo.Sort([new("COM10", "Bluetooth"), new("COM1", "Kommunikationsanschluss"), new("COM3", "USB-SERIAL CH340")]);
        Assert.Equal(["COM1", "COM3", "COM10"], ports.Select(p => p.Name));
        Assert.Equal(1, PortInfo.ChooseDefault(ports));
        Assert.Equal("COM3 – USB-SERIAL CH340", ports[1].Display);
        Assert.Equal(0, PortInfo.ChooseDefault([new("COM1", "")]));
        Assert.Equal(-1, PortInfo.ChooseDefault([]));
    }

    [Fact]
    public void Fehler_werden_verstaendlich()
    {
        Assert.Equal(ErrorText.PortBelegt, ErrorText.Describe(new UnauthorizedAccessException("Access to the path 'COM3' is denied.")));
        Assert.Equal(ErrorText.KeineAntwort, ErrorText.Describe(new IOException("Das Zeitlimit für die Semaphore wurde erreicht.", unchecked((int)0x80070079))));
        Assert.Equal(ErrorText.PortFehlt, ErrorText.Describe(new FileNotFoundException("Could not find file 'COM9'.")));
        Assert.Equal(ErrorText.PortFehlt, ErrorText.Describe(new IOException("The port 'COM9' does not exist.")));
    }
}

public class DemoPrinterTests
{
    [Fact]
    public void Demo_heizt_auf_und_antwortet_wie_Marlin()
    {
        var zeit = new ManualTimeProvider();
        using var demo = new DemoPrinter(TimeSpan.Zero, zeit);
        demo.Open();
        demo.DiscardInput();
        demo.WriteLine("M104 S200");
        Assert.Equal("ok", demo.ReadLine());
        zeit.Advance(TimeSpan.FromSeconds(30));
        demo.WriteLine("M105");
        Assert.True(TemperatureReport.TryParse(demo.ReadLine()!, out var r));
        Assert.InRange(r.NozzleIst, 199, 200.5);
        Assert.Equal(200, r.NozzleSoll);
        demo.WriteLine("m105");
        Assert.StartsWith("echo:Unknown command", demo.ReadLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void Beispieldatei_ist_sauberer_GCode()
    {
        var zeilen = DemoPrinter.SampleGcode();
        Assert.True(zeilen.Count > 1000);
        Assert.Equal(zeilen, GcodeFile.Clean(zeilen));
    }
}
