using KobraMax.Core;

namespace KobraMax.Tests;

public class TemperatureOverrideTests
{
    [Theory]
    [InlineData("M140 S80", "M140 S50")]
    [InlineData("M104 S170 T0", "M104 S200 T0")]
    [InlineData("M109 R170", "M109 R200")]
    [InlineData("M109 S240", "M109 S200")]
    [InlineData("M190 S60", "M190 S50")]
    [InlineData("G10 P0 S240", "G10 P0 S200")]
    [InlineData("M568 P0 S240", "M568 P0 S200")]
    public void Ersetzt_Heizbefehle_mit_Override(string zeile, string erwartet)
    {
        var r = TemperatureOverride.Apply(zeile, 200, 50);
        Assert.Equal(erwartet, r.Line);
        Assert.True(r.Changed);
    }

    [Fact]
    public void Loggt_was_die_Datei_wollte()
    {
        Assert.Equal("Düse: Datei wollte 170 °C, nutze 200 °C", TemperatureOverride.Apply("M109 R170", 200, 50).Log);
        Assert.Equal("Bett: Datei wollte 80 °C, nutze 50 °C", TemperatureOverride.Apply("M140 S80", 200, 50).Log);
    }

    [Theory]
    [InlineData("M104 S0")]
    [InlineData("M140 S0")]
    [InlineData("M109 S0.0")]
    public void Heizung_aus_bleibt_unveraendert(string zeile)
    {
        var r = TemperatureOverride.Apply(zeile, 200, 50);
        Assert.Equal(zeile, r.Line);
        Assert.False(r.Changed);
    }

    [Fact]
    public void Kleinbuchstaben_werden_erkannt()
    {
        Assert.Equal("m104 s200", TemperatureOverride.Apply("m104 s170", 200, 50).Line);
    }

    [Fact]
    public void Zeilennummer_und_Pruefsumme_werden_nach_dem_Saeubern_erkannt()
    {
        var zeile = Assert.Single(GcodeFile.Clean(["N5 M109 S170*12"]));
        Assert.Equal("M109 S170", zeile);
        Assert.Equal("M109 S200", TemperatureOverride.Apply(zeile, 200, 50).Line);
        // Auch ungesäubert mit N davor:
        Assert.Equal("N5 M109 S200", TemperatureOverride.Apply("N5 M109 S170", 200, 50).Line);
    }

    [Theory]
    [InlineData("M140 S80")]
    [InlineData("M104 S170 T0")]
    [InlineData("M109 R170")]
    [InlineData("G1 X10 Y20 E0.5 F1200")]
    public void Ohne_Override_bleibt_alles_unveraendert(string zeile)
    {
        var r = TemperatureOverride.Apply(zeile, null, null);
        Assert.Equal(zeile, r.Line);
        Assert.Null(r.Log);
    }

    [Theory]
    [InlineData("G1 X10 S5")]
    [InlineData("M106 S255")]
    [InlineData("G28")]
    [InlineData("M1040 S170")]
    public void Andere_Befehle_bleiben_unveraendert(string zeile)
    {
        Assert.Equal(zeile, TemperatureOverride.Apply(zeile, 200, 50).Line);
    }

    [Fact]
    public void Nur_ein_Heizer_ueberschrieben()
    {
        Assert.Equal("M104 S170", TemperatureOverride.Apply("M104 S170", null, 50).Line);
        Assert.Equal("M140 S50", TemperatureOverride.Apply("M140 S80", null, 50).Line);
    }

    [Fact]
    public void Gleicher_Wert_wird_nicht_geloggt()
    {
        var r = TemperatureOverride.Apply("M104 S200", 200, 50);
        Assert.Equal("M104 S200", r.Line);
        Assert.Null(r.Log);
    }
}
