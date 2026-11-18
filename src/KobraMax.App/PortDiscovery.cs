using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using KobraMax.Core;

namespace KobraMax.App;

/// <summary>Findet COM-Ports samt Beschreibung (z. B. "USB-SERIAL CH340") über WMI.</summary>
internal static partial class PortDiscovery
{
    [GeneratedRegex(@"^(?<name>.*?)\s*\((?<port>COM\d+)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeraeteName();

    public static IReadOnlyList<PortInfo> List()
    {
        var beschreibung = Beschreibungen();
        var namen = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
        namen.UnionWith(beschreibung.Keys);
        return namen.Select(n => new PortInfo(n.ToUpperInvariant(), beschreibung.GetValueOrDefault(n, ""))).ToList();
    }

    private static Dictionary<string, string> Beschreibungen()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var suche = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            using var ergebnisse = suche.Get();
            foreach (var obj in ergebnisse)
            {
                using (obj)
                {
                    if (obj["Name"] is string name && GeraeteName().Match(name) is { Success: true } m)
                    {
                        map[m.Groups["port"].Value] = m.Groups["name"].Value;
                    }
                }
            }
        }
        catch (Exception e) when (e is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // WMI gesperrt? Dann gibt es eben nur die Portnamen.
        }

        return map;
    }
}
