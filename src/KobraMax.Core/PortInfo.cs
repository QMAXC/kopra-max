namespace KobraMax.Core;

/// <summary>Ein serieller Port, z. B. ("COM3", "USB-SERIAL CH340").</summary>
public sealed record PortInfo(string Name, string Description)
{
    public string Display => string.IsNullOrWhiteSpace(Description) ? Name : $"{Name} – {Description}";

    /// <summary>Welcher Port soll vorausgewählt sein? Der mit "CH340" (Kobra Max), sonst der erste. -1 = keiner.</summary>
    public static int ChooseDefault(IReadOnlyList<PortInfo> ports)
    {
        for (var i = 0; i < ports.Count; i++)
        {
            if (ports[i].Description.Contains("CH340", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return ports.Count > 0 ? 0 : -1;
    }

    /// <summary>Sortiert COM2 vor COM10.</summary>
    public static IReadOnlyList<PortInfo> Sort(IEnumerable<PortInfo> ports) =>
        ports.OrderBy(p => Nummer(p.Name)).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private static int Nummer(string name)
    {
        var ziffern = new string(name.Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(ziffern, out var n) ? n : int.MaxValue;
    }
}
