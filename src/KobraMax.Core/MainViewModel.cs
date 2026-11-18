using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace KobraMax.Core;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
}

/// <summary>
/// Die ganze Bedienlogik der App ohne Oberfläche (dadurch testbar).
/// Alle Methoden und Eigenschaften gehören dem UI-Thread; Meldungen des Drucker-Threads
/// werden über <c>post</c> auf den UI-Thread gebracht.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    public const int MaxNozzle = 260;
    public const int MaxBed = 110;
    public const int MaxLogLines = 500;
    public const string FromFileText = "Druck: Wert aus Datei";
    public static readonly IReadOnlyList<int> BaudRates = [115200, 250000];
    public static readonly IReadOnlyList<int> StepSizes = [1, 10, 50];

    private readonly Func<IReadOnlyList<PortInfo>> _listPorts;
    private readonly Func<string, int, ISerialTransport> _openTransport;
    private readonly Action<Action> _post;
    private readonly List<string> _log = [];
    private IReadOnlyList<string> _fileLines = [];

    public MainViewModel(
        Func<IReadOnlyList<PortInfo>> listPorts,
        Func<string, int, ISerialTransport> openTransport,
        Action<Action> post,
        TimeProvider? time = null,
        TimeSpan? resetDelay = null)
    {
        _listPorts = listPorts;
        _openTransport = openTransport;
        _post = post;
        Session = new PrinterSession(e => _post(() => Handle(e)), time, resetDelay);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Die Portliste hat sich geändert (eigenes Ereignis, damit die Auswahl nicht springt).</summary>
    public event EventHandler? PortsChanged;

    public event EventHandler? LogChanged;

    public PrinterSession Session { get; }

    // ------------------------------------------------------------ Anzeige

    public IReadOnlyList<PortInfo> Ports { get; private set; } = [];

    public int DefaultPortIndex { get; private set; } = -1;

    public IReadOnlyList<string> LogLines => _log;

    public ConnectionState Connection { get; private set => Set(ref field, value); } = ConnectionState.Disconnected;

    public bool IsConnected => Connection == ConnectionState.Connected;

    public string StatusText => Connection switch
    {
        ConnectionState.Connected => "verbunden",
        ConnectionState.Connecting => "verbinde …",
        _ => "getrennt",
    };

    public string ConnectButtonText => Connection == ConnectionState.Disconnected ? "Verbinden" : "Trennen";

    public bool ConnectButtonEnabled => Connection != ConnectionState.Connecting;

    /// <summary>Letzte Temperaturmeldung des Druckers (null = noch keine).</summary>
    public TemperatureReport? Temperature { get; private set => Set(ref field, value); }

    /// <summary>Eigene Düsentemperatur für den Druck (null = Wert aus Datei).</summary>
    public int? NozzleOverride { get; private set => Set(ref field, value); }

    /// <summary>Eigene Betttemperatur für den Druck (null = Wert aus Datei).</summary>
    public int? BedOverride { get; private set => Set(ref field, value); }

    public string NozzleText { get; private set => Set(ref field, value); } = "Düse: -- / -- °C";

    public string BedText { get; private set => Set(ref field, value); } = "Bett: -- / -- °C";

    public string NozzleTargetText { get; private set => Set(ref field, value); } = FromFileText;

    public string BedTargetText { get; private set => Set(ref field, value); } = FromFileText;

    public string FileText { get; private set => Set(ref field, value); } = "keine Datei geladen";

    public string ProgressText { get; private set => Set(ref field, value); } = "";

    /// <summary>0 bis 100.</summary>
    public double ProgressValue { get; private set => Set(ref field, value); }

    public bool IsPrinting { get; private set => Set(ref field, value); }

    public bool IsPaused { get; private set => Set(ref field, value); }

    public string PauseButtonText => IsPaused ? "Weiter" : "Pause";

    public bool HasFile => _fileLines.Count > 0;

    // ------------------------------------------------------------ Verbindung

    public void RefreshPorts()
    {
        try
        {
            Ports = PortInfo.Sort(_listPorts());
        }
        catch (Exception e)
        {
            Ports = [];
            Log("Ports konnten nicht gelesen werden: " + e.Message);
        }

        DefaultPortIndex = PortInfo.ChooseDefault(Ports);
        PortsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Knopf "Verbinden/Trennen".</summary>
    public void ToggleConnection(int portIndex, int baudRate)
    {
        if (Session.IsActive)
        {
            Session.Disconnect();
            return;
        }

        if (portIndex < 0 || portIndex >= Ports.Count)
        {
            Log("Kein Port gefunden. Kabel eingesteckt? Drucker an? Dann Aktualisieren drücken.");
            return;
        }

        var port = Ports[portIndex].Name;
        ISerialTransport transport;
        try
        {
            transport = _openTransport(port, baudRate);
        }
        catch (Exception e)
        {
            Log("Verbindung fehlgeschlagen: " + ErrorText.Describe(e));
            return;
        }

        Log($"Öffne {port} mit {baudRate.ToString(CultureInfo.InvariantCulture)} Baud …");
        if (Session.Connect(transport))
        {
            Connection = ConnectionState.Connecting;
        }
        else
        {
            transport.Dispose();
        }
    }

    // ------------------------------------------------------------ Befehle

    public void Send(string befehl)
    {
        if (IsConnected)
        {
            Session.Enqueue(befehl);
        }
        else
        {
            Log("Nicht verbunden.");
        }
    }

    /// <summary>Konsole: Eingabe in Großbuchstaben senden. Gibt true zurück, wenn das Feld geleert werden soll.</summary>
    public bool SendInput(string eingabe)
    {
        // Der Drucker kennt nur Großbuchstaben: "m105" -> "Unknown command".
        var b = eingabe.Trim().ToUpperInvariant();
        if (b.Length == 0)
        {
            return false;
        }

        Send(b);
        return true;
    }

    public void Heat(Heater heizer, string eingabe)
    {
        if (!int.TryParse(eingabe.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var wert))
        {
            Log("Bitte eine ganze Zahl als Temperatur eingeben, z. B. 200.");
            return;
        }

        var grenze = heizer == Heater.Nozzle ? MaxNozzle : MaxBed;
        if (wert < 1 || wert > grenze)
        {
            Log($"Bitte einen Wert zwischen 1 und {grenze} °C eingeben.");
            return;
        }

        if (!IsConnected)
        {
            Log("Nicht verbunden.");
            return;
        }

        var text = $"Druck: {wert} °C";
        if (heizer == Heater.Nozzle)
        {
            Session.NozzleTarget = wert;
            NozzleOverride = wert;
            NozzleTargetText = text;
        }
        else
        {
            Session.BedTarget = wert;
            BedOverride = wert;
            BedTargetText = text;
        }

        Send($"{Code(heizer)} S{wert}");
    }

    /// <summary>Heizung aus. Danach gelten beim Druck wieder die Werte aus der Datei.</summary>
    public void HeaterOff(Heater heizer)
    {
        if (heizer == Heater.Nozzle)
        {
            Session.NozzleTarget = null;
            NozzleOverride = null;
            NozzleTargetText = FromFileText;
        }
        else
        {
            Session.BedTarget = null;
            BedOverride = null;
            BedTargetText = FromFileText;
        }

        Send($"{Code(heizer)} S0");
    }

    public void Home() => Move("G28");

    /// <param name="achse">'X', 'Y' oder 'Z'.</param>
    /// <param name="richtung">+1 oder -1.</param>
    /// <param name="schritt">Schrittweite in mm.</param>
    public void Jog(char achse, int richtung, int schritt)
    {
        var weg = richtung * schritt;
        var tempo = achse == 'Z' ? 600 : 3000;
        Move("G91", $"G1 {achse}{weg.ToString(CultureInfo.InvariantCulture)} F{tempo}", "G90");
    }

    private void Move(params string[] befehle)
    {
        if (Session.IsPrinting)
        {
            Log("Während des Drucks ist Bewegen gesperrt.");
            return;
        }

        if (!IsConnected)
        {
            Log("Nicht verbunden.");
            return;
        }

        foreach (var b in befehle)
        {
            Session.Enqueue(b);
        }
    }

    /// <summary>Vom 3-Sekunden-Timer: Temperatur abfragen, wenn nichts anderes ansteht.</summary>
    public void PollTemperature()
    {
        if (IsConnected && !Session.HasQueuedCommands)
        {
            Session.Enqueue("M105");
        }
    }

    // ------------------------------------------------------------ Druck

    public void LoadFile(string pfad)
    {
        if (Session.IsPrinting)
        {
            Log("Es läuft gerade ein Druck.");
            return;
        }

        if (GcodeFile.IsProjectFile(pfad))
        {
            Log("Das ist eine .3mf-Projektdatei, kein G-Code. Im Slicer bitte „G-Code exportieren“ wählen und die .gcode-Datei öffnen.");
            return;
        }

        IReadOnlyList<string> zeilen;
        try
        {
            zeilen = GcodeFile.Load(pfad);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log("Datei konnte nicht gelesen werden: " + e.Message);
            return;
        }

        LoadLines(Path.GetFileName(pfad), zeilen);
    }

    /// <summary>Bereits gesäuberte Zeilen als Druckauftrag übernehmen (z. B. die Demo-Datei).</summary>
    public void LoadLines(string name, IReadOnlyList<string> zeilen)
    {
        if (Session.IsPrinting)
        {
            Log("Es läuft gerade ein Druck.");
            return;
        }

        if (zeilen.Count == 0)
        {
            Log("Die Datei enthält keine G-Code-Befehle.");
            return;
        }

        _fileLines = zeilen;
        FileText = name;
        ProgressText = $"{zeilen.Count} Zeilen";
        ProgressValue = 0;
        Log($"Geladen: {name} ({zeilen.Count} Zeilen)");
        OnPropertyChanged(nameof(HasFile));
    }

    public void StartPrint()
    {
        if (!IsConnected)
        {
            Log("Nicht verbunden.");
        }
        else if (_fileLines.Count == 0)
        {
            Log("Erst eine Datei öffnen.");
        }
        else if (!Session.StartPrint(_fileLines))
        {
            Log("Es läuft schon ein Druck.");
        }
        else
        {
            IsPrinting = true;
            IsPaused = false;
            ProgressValue = 0;
            Log("Druck gestartet.");
            var duese = Session.NozzleTarget;
            var bett = Session.BedTarget;
            if (duese is not null || bett is not null)
            {
                Log($"Eigene Temperaturen gelten: Düse {Beschreibe(duese)}, Bett {Beschreibe(bett)}.");
            }
        }

        static string Beschreibe(int? t) => t is int v ? $"{v} °C" : "aus Datei";
    }

    public void TogglePause()
    {
        if (Session.TogglePause() is bool pausiert)
        {
            IsPaused = pausiert;
            Log(pausiert ? "Pausiert." : "Weiter geht's.");
        }
    }

    public void StopPrint()
    {
        if (Session.Stop())
        {
            IsPaused = false;
            Log("Druck gestoppt, Heizung aus.");
        }
    }

    /// <summary>
    /// Beim Schließen: läuft ein Druck, wird er gestoppt (Heizung aus), dann wird der Port freigegeben.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (Session.Stop())
        {
            Log("Druck gestoppt, Heizung aus.");
            for (var i = 0; i < 50 && Session.IsActive && !Session.IsIdle; i++)
            {
                await Task.Delay(100).ConfigureAwait(true);
            }
        }

        Session.Disconnect();
        await Task.Run(() => Session.WaitForExit(TimeSpan.FromSeconds(4))).ConfigureAwait(true);
    }

    // ------------------------------------------------------------ Meldungen vom Drucker

    private void Handle(PrinterEvent e)
    {
        switch (e)
        {
            case LogEvent l:
                Log(l.Text);
                break;
            case TemperatureEvent { Report: var r }:
                Temperature = r;
                NozzleText = string.Create(CultureInfo.InvariantCulture, $"Düse: {r.NozzleIst:F0} / {r.NozzleSoll:F0} °C");
                BedText = string.Create(CultureInfo.InvariantCulture, $"Bett: {r.BedIst:F0} / {r.BedSoll:F0} °C");
                break;
            case ConnectionEvent c:
                Connection = c.Connected ? ConnectionState.Connected : ConnectionState.Disconnected;
                Log(c.Connected ? "Verbunden." : "Getrennt.");
                if (c.Connected)
                {
                    Session.Enqueue("M105");
                }

                break;
            case ProgressEvent p:
                ProgressValue = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
                ProgressText = $"{p.Done} / {p.Total} Zeilen ({p.Percent} %)";
                break;
            case PrintEndedEvent:
                IsPrinting = false;
                IsPaused = false;
                break;
        }
    }

    public void ClearLog()
    {
        _log.Clear();
        LogChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Log(string text)
    {
        _log.Add(text);
        if (_log.Count > MaxLogLines)
        {
            _log.RemoveRange(0, 100);
        }

        LogChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Code(Heater h) => h == Heater.Nozzle ? "M104" : "M140";

    private void Set<T>(ref T feld, T wert, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(feld, wert))
        {
            return;
        }

        feld = wert;
        OnPropertyChanged(name);
        switch (name)
        {
            case nameof(Connection):
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(ConnectButtonEnabled));
                break;
            case nameof(IsPaused):
                OnPropertyChanged(nameof(PauseButtonText));
                break;
        }
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
