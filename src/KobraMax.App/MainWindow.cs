using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using KobraMax.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using VirtualKey = Windows.System.VirtualKey;

namespace KobraMax.App;

/// <summary>
/// Das Hauptfenster. Die Oberfläche wird bewusst in C# statt XAML aufgebaut: so prüft der
/// Compiler jede Zeile (typsicher). Die ganze Bedienlogik steckt im testbaren
/// <see cref="MainViewModel"/> in KobraMax.Core – hier wird nur angezeigt.
/// </summary>
internal sealed class MainWindow : Window
{
    private const string FileFilter = "G-Code (*.gcode, *.gco, *.g)\0*.gcode;*.gco;*.g\0Alle Dateien (*.*)\0*.*\0\0";

    // Segoe Fluent Icons / Segoe MDL2 Assets
    private const string IconPrinter = "\uE749";
    private const string IconRefresh = "\uE72C";
    private const string IconConnect = "\uE703";
    private const string IconOpen = "\uE8E5";
    private const string IconPlay = "\uE768";
    private const string IconPause = "\uE769";
    private const string IconStop = "\uE71A";
    private const string IconHome = "\uE80F";
    private const string IconUp = "\uE70E";
    private const string IconDown = "\uE70D";
    private const string IconLeft = "\uE76B";
    private const string IconRight = "\uE76C";
    private const string IconSend = "\uE724";
    private const string IconClear = "\uE74D";
    private const string IconInfo = "\uE946";

    private readonly StartupOptions _options;
    private readonly MainViewModel _vm;
    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer _tempTimer;
    private readonly DispatcherQueueTimer? _screenshotTimer;
    private readonly Grid _root;
    private bool _keepAwake;
    private bool _logDirty;
    private bool _shutdownDone;
    private bool _closing;
    private bool _demoStarted;

    // Verbindung
    private readonly ComboBox _portBox = new() { MinWidth = 320, PlaceholderText = "kein Port gefunden" };
    private readonly ComboBox _baudBox = new() { MinWidth = 110 };
    private readonly Button _connectButton = new() { MinWidth = 120 };
    private readonly Border _statusPill = new();
    private readonly Ellipse _statusDot = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _statusText = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, FontSize = 13 };

    // Temperatur
    private readonly HeaterView _nozzle;
    private readonly HeaterView _bed;

    // Bewegung
    private readonly ComboBox _stepBox = new() { MinWidth = 90 };

    // Druck
    private readonly TextBlock _fileText = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _percentText = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 64, TextAlignment = TextAlignment.Right };
    private readonly Button _startButton = new();
    private readonly Button _pauseButton = new() { MinWidth = 104 };
    private readonly Button _stopButton = new() { MinWidth = 104 };
    private readonly TextBlock _progressText = new();
    private readonly ProgressBar _progressBar = new() { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center };

    // Konsole
    private readonly ScrollViewer _logScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MinHeight = 60 };
    private readonly TextBlock _logText = new()
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = 12,
        LineHeight = 18,
        IsTextSelectionEnabled = true,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBox _commandInput = new() { PlaceholderText = "G-Code-Befehl, z. B. M105 oder G28 – Enter sendet" };

    public MainWindow(StartupOptions options)
    {
        _options = options;
        Title = "Kobra Max Steuerung";
        ExtendsContentIntoTitleBar = true;
        if (_options.ScreenshotPath is null)
        {
            SystemBackdrop = new MicaBackdrop();
        }

        _vm = _options.Demo
            ? new MainViewModel(
                () => [new PortInfo(DemoPrinter.PortName, DemoPrinter.PortDescription)],
                (_, _) => new DemoPrinter(),
                Post,
                resetDelay: TimeSpan.FromMilliseconds(800))
            : new MainViewModel(PortDiscovery.List, (port, baud) => new SerialPortTransport(port, baud), Post);

        _nozzle = new HeaterView("Düse", "200", MainViewModel.MaxNozzle);
        _bed = new HeaterView("Bett", "60", MainViewModel.MaxBed);

        var titleBar = BuildTitleBar();
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Place(_root, titleBar, 0, 0);
        Place(_root, BuildContent(), 1, 0);
        if (_options.ScreenshotPath is not null && Resource("SolidBackgroundFillColorBaseBrush") is Brush hintergrund)
        {
            _root.Background = hintergrund; // Mica lässt sich nicht fotografieren
        }

        Content = _root;
        SetTitleBar(titleBar);

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var icon = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "KobraMax.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon); // Taskleiste und Alt+Tab
        }

        SizeWindow();
        AppWindow.Closing += OnClosing;

        _vm.PropertyChanged += (_, _) => Render();
        _vm.PortsChanged += (_, _) => RenderPorts();
        _vm.LogChanged += (_, _) => ScheduleLogRefresh();

        _vm.RefreshPorts();
        Render();
        _vm.Log(_options.Demo
            ? "Demo-Modus: simulierter Drucker, es wird nichts an echte Geräte gesendet."
            : "Willkommen! Drucker per USB anschließen, Port wählen und auf „Verbinden“ klicken.");

        _tempTimer = DispatcherQueue.CreateTimer();
        _tempTimer.Interval = TimeSpan.FromSeconds(_options.Demo ? 1 : 3);
        _tempTimer.Tick += (_, _) => _vm.PollTemperature();
        _tempTimer.Start();

        if (_options.Demo)
        {
            _vm.ToggleConnection(_vm.DefaultPortIndex, MainViewModel.BaudRates[0]);
        }
        else
        {
            CreateDesktopShortcut();
        }

        if (_options.ScreenshotPath is { } pfad)
        {
            // Als Feld halten – ein nur lokal referenzierter Timer wird vom GC eingesammelt und feuert nie.
            _screenshotTimer = DispatcherQueue.CreateTimer();
            _screenshotTimer.Interval = TimeSpan.FromSeconds(9);
            _screenshotTimer.IsRepeating = false;
            _screenshotTimer.Tick += async (_, _) => await TakeScreenshotAndExitAsync(pfad);
            _vm.LogChanged += (_, _) => Diag.Log(_vm.LogLines.Count > 0 ? "log: " + _vm.LogLines[^1] : "log geleert");
            _screenshotTimer.Start();
        }
    }

    private void Post(Action aktion) => DispatcherQueue.TryEnqueue(() => aktion());

    // ------------------------------------------------------------ Aufbau

    private Grid BuildTitleBar()
    {
        var g = new Grid { Height = 44, Padding = new Thickness(16, 0, 140, 0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        var icon = new FontIcon { Glyph = IconPrinter, FontSize = 16, Foreground = Resource("AccentTextFillColorPrimaryBrush") as Brush };
        var titel = new TextBlock { Text = "Kobra Max Steuerung", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var row = Row(10, icon, titel);
        if (_options.Demo)
        {
            row.Children.Add(Badge("DEMO"));
        }

        g.Children.Add(row);
        return g;
    }

    private Grid BuildContent()
    {
        var g = new Grid { Padding = new Thickness(16, 0, 16, 16), RowSpacing = 12 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var mitte = new Grid { ColumnSpacing = 12 };
        mitte.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mitte.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mitte.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(mitte, BuildHeaterCard(_nozzle, Heater.Nozzle), 0, 0);
        Place(mitte, BuildHeaterCard(_bed, Heater.Bed), 0, 1);
        Place(mitte, Card("Bewegung", BuildMovement()), 0, 2);

        Place(g, BuildConnectionCard(), 0, 0);
        Place(g, mitte, 1, 0);
        Place(g, Card("Druck", BuildPrint()), 2, 0);
        Place(g, BuildConsoleCard(), 3, 0);
        return g;
    }

    private Border BuildConnectionCard()
    {
        foreach (var b in MainViewModel.BaudRates)
        {
            _baudBox.Items.Add($"{b.ToString(CultureInfo.InvariantCulture)} Baud");
        }

        _baudBox.SelectedIndex = 0;
        _connectButton.Click += (_, _) => _vm.ToggleConnection(_portBox.SelectedIndex, MainViewModel.BaudRates[Math.Max(0, _baudBox.SelectedIndex)]);
        ToolTipService.SetToolTip(_baudBox, "115200 passt für den Kobra Max. 250000 nur, wenn 115200 nicht klappt.");

        _statusPill.Style = Resource("PillStyle") as Style;
        _statusPill.Child = Row(8, _statusDot, _statusText);
        _statusPill.VerticalAlignment = VerticalAlignment.Center;

        var refresh = IconButton(IconRefresh, "Aktualisieren", _vm.RefreshPorts);
        ToolTipService.SetToolTip(refresh, "Ports neu suchen (z. B. nach dem Einstecken des Kabels)");

        var g = new Grid { ColumnSpacing = 8 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var header = new TextBlock { Text = "Verbindung", Style = Resource("CardHeaderStyle") as Style, Margin = new Thickness(0, 0, 8, 0) };
        Place(g, header, 0, 0);
        Place(g, Row(8, _portBox, _baudBox, refresh, _connectButton), 0, 1);
        Place(g, _statusPill, 0, 2);
        return new Border { Style = Resource("CardStyle") as Style, Child = g, Padding = new Thickness(16, 10, 16, 10) };
    }

    private Border BuildHeaterCard(HeaterView h, Heater heizer)
    {
        h.Input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                _vm.Heat(heizer, h.Input.Text);
                e.Handled = true;
            }
        };

        var heizen = TextButton("Heizen", () => _vm.Heat(heizer, h.Input.Text));
        var aus = TextButton("Aus", () => _vm.HeaterOff(heizer));
        ToolTipService.SetToolTip(heizen, "Heizt jetzt und nutzt den Wert auch beim Druck (statt des Werts aus der Datei).");
        ToolTipService.SetToolTip(aus, "Heizung aus. Beim Druck gelten dann wieder die Werte aus der Datei.");

        var zahl = Row(0, h.Current, new TextBlock { Text = " °C", FontSize = 18, Margin = new Thickness(0, 0, 0, 5), VerticalAlignment = VerticalAlignment.Bottom });
        zahl.VerticalAlignment = VerticalAlignment.Bottom;
        h.Target.Margin = new Thickness(10, 0, 0, 7);
        h.Target.VerticalAlignment = VerticalAlignment.Bottom;
        var anzeige = Row(0, zahl, h.Target);

        var eingabe = Row(6, h.Input, new TextBlock { Text = "°C", Style = Resource("SecondaryTextStyle") as Style }, heizen, aus);
        eingabe.Margin = new Thickness(0, 4, 0, 0);

        var info = new Border
        {
            Background = Resource("SubtleFillColorSecondaryBrush") as Brush,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5, 8, 6),
            Margin = new Thickness(0, 10, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = Row(8, new FontIcon { Glyph = IconInfo, FontSize = 12, Foreground = Resource("TextFillColorSecondaryBrush") as Brush }, h.Override),
        };

        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Place(panel, new StackPanel { Spacing = 8, Children = { anzeige, h.Bar, eingabe } }, 0, 0);
        Place(panel, info, 1, 0);
        return Card(h.Name, panel, h.Badge);
    }

    private Grid BuildMovement()
    {
        var g = new Grid { RowSpacing = 6, ColumnSpacing = 6 };
        for (var i = 0; i < 4; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        Place(g, JogButton(IconUp, "Y+", 'Y', 1), 0, 1);
        Place(g, JogButton(IconLeft, "X−", 'X', -1), 1, 0);
        var home = PadButton(IconHome, "Home", _vm.Home);
        ToolTipService.SetToolTip(home, "Alle Achsen auf Null fahren (G28)");
        Place(g, home, 1, 1);
        Place(g, JogButton(IconRight, "X+", 'X', 1), 1, 2);
        Place(g, JogButton(IconDown, "Y−", 'Y', -1), 2, 1);

        var zPlus = JogButton(IconUp, "Z+", 'Z', 1);
        var zMinus = JogButton(IconDown, "Z−", 'Z', -1);
        zPlus.Margin = zMinus.Margin = new Thickness(14, 0, 0, 0);
        Place(g, zPlus, 0, 3);
        Place(g, new TextBlock { Text = "Z", Style = Resource("SecondaryTextStyle") as Style, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) }, 1, 3);
        Place(g, zMinus, 2, 3);

        foreach (var s in MainViewModel.StepSizes)
        {
            _stepBox.Items.Add($"{s} mm");
        }

        _stepBox.SelectedIndex = 1; // 10 mm
        var schritt = Row(8, new TextBlock { Text = "Schritt", Style = Resource("SecondaryTextStyle") as Style }, _stepBox);
        schritt.Margin = new Thickness(0, 4, 0, 0);
        Place(g, schritt, 3, 0, 4);
        return g;
    }

    private Grid BuildPrint()
    {
        _startButton.Content = IconContent(IconPlay, "Start");
        _startButton.MinWidth = 104;
        _startButton.Style = Resource("AccentButtonStyle") as Style;
        _startButton.Click += (_, _) => _vm.StartPrint();
        _pauseButton.Click += (_, _) => _vm.TogglePause();
        _stopButton.Content = IconContent(IconStop, "Stopp");
        _stopButton.Click += (_, _) => _vm.StopPrint();
        ToolTipService.SetToolTip(_stopButton, "Druck abbrechen, Heizung aus, Düse 10 mm hoch");
        _progressText.Style = Resource("SecondaryTextStyle") as Style;

        var g = new Grid { RowSpacing = 10, ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Place(g, Row(12, IconButton(IconOpen, "Datei öffnen …", OpenFile), _fileText), 0, 0);
        Place(g, Row(8, _startButton, _pauseButton, _stopButton), 0, 1);

        var fortschritt = new Grid { ColumnSpacing = 12 };
        fortschritt.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fortschritt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fortschritt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(fortschritt, _progressBar, 0, 0);
        Place(fortschritt, _progressText, 0, 1);
        Place(fortschritt, _percentText, 0, 2);
        Place(g, fortschritt, 1, 0, 2);
        return g;
    }

    private Border BuildConsoleCard()
    {
        _logScroll.Content = _logText;
        var log = new Border { Style = Resource("ConsoleStyle") as Style, Child = _logScroll };

        var g = new Grid { RowSpacing = 8, ColumnSpacing = 8 };
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _commandInput.KeyDown += OnCommandKeyDown;
        _commandInput.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        Place(g, log, 0, 0, 2);
        Place(g, _commandInput, 1, 0);
        Place(g, IconButton(IconSend, "Senden", SendCommand), 1, 1);

        var leeren = new Button { Content = new FontIcon { Glyph = IconClear, FontSize = 14 }, Padding = new Thickness(8, 4, 8, 4) };
        leeren.Click += (_, _) => _vm.ClearLog();
        ToolTipService.SetToolTip(leeren, "Konsole leeren");
        return Card("Konsole", g, leeren);
    }

    // ------------------------------------------------------------ Aktionen

    private void OnCommandKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            SendCommand();
            e.Handled = true;
        }
    }

    private void SendCommand()
    {
        if (_vm.SendInput(_commandInput.Text))
        {
            _commandInput.Text = "";
        }
    }

    private void OpenFile()
    {
        if (_options.Demo)
        {
            _vm.LoadLines("demo_wuerfel.gcode", DemoPrinter.SampleGcode());
            return;
        }

        // Testdateien liegen auf den Schul-PCs in H:\3D – sonst im Ordner der .exe.
        var start = Directory.Exists(@"H:\3D") ? @"H:\3D" : AppContext.BaseDirectory;
        var pfad = NativeMethods.OpenFile(_hwnd, start, FileFilter, "G-Code-Datei öffnen");
        if (pfad is not null)
        {
            _vm.LoadFile(pfad);
        }
    }

    private void CreateDesktopShortcut()
    {
        try
        {
            if (DesktopShortcut.EnsureOnce() is not null)
            {
                _vm.Log("Desktop-Verknüpfung „Kobra Max Steuerung“ wurde angelegt.");
            }
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException)
        {
            _vm.Log("Desktop-Verknüpfung konnte nicht angelegt werden: " + e.Message);
        }
    }

    /// <summary>Im Demo-Modus: sobald verbunden, heizen und einen Beispieldruck starten.</summary>
    private void RunDemoScenario()
    {
        if (!_options.Demo || _demoStarted || !_vm.IsConnected)
        {
            return;
        }

        _demoStarted = true;
        _vm.Heat(Heater.Nozzle, _nozzle.Input.Text);
        _vm.Heat(Heater.Bed, _bed.Input.Text);
        _vm.LoadLines("demo_wuerfel.gcode", DemoPrinter.SampleGcode());
        _vm.StartPrint();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shutdownDone)
        {
            return;
        }

        args.Cancel = true;
        if (!_closing)
        {
            _closing = true;
            _ = CloseAsync(askFirst: true);
        }
    }

    private async Task CloseAsync(bool askFirst)
    {
        try
        {
            if (askFirst && _vm.Session.IsPrinting)
            {
                var frage = new ContentDialog
                {
                    Title = "Druck läuft noch",
                    Content = "Wenn du das Programm jetzt schließt, wird der Druck abgebrochen und die Heizung ausgeschaltet.",
                    PrimaryButtonText = "Abbrechen und schließen",
                    CloseButtonText = "Weiter drucken",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot,
                };
                if (await frage.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }
            }

            _tempTimer.Stop();
            await _vm.ShutdownAsync(); // gibt den Port sauber frei
            NativeMethods.KeepAwake(false);
            _shutdownDone = true;
            Close();
        }
        finally
        {
            _closing = false;
        }
    }

    private async Task TakeScreenshotAndExitAsync(string pfad)
    {
        try
        {
            Diag.Log($"Screenshot: Fenster {AppWindow.Size.Width}x{AppWindow.Size.Height}, Inhalt {_root.ActualWidth}x{_root.ActualHeight}");
            var bild = new RenderTargetBitmap();
            await bild.RenderAsync(_root);
            Diag.Log($"gerendert {bild.PixelWidth}x{bild.PixelHeight}");
            var pixel = (await bild.GetPixelsAsync()).ToArray();
            var dpi = 96.0 * NativeMethods.ScaleFor(_hwnd);

            using var speicher = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, speicher);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)bild.PixelWidth, (uint)bild.PixelHeight, dpi, dpi, pixel);
            await encoder.FlushAsync();

            var voll = System.IO.Path.GetFullPath(pfad);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(voll) ?? ".");
            speicher.Seek(0);
            await using (var datei = File.Create(voll))
            {
                await speicher.AsStreamForRead().CopyToAsync(datei);
            }

            Diag.Log("gespeichert: " + voll);
            var beenden = CloseAsync(askFirst: false);
            await Task.WhenAny(beenden, Task.Delay(TimeSpan.FromSeconds(8)));
            Diag.Log("beendet");
            Environment.Exit(0);
        }
        catch (Exception e)
        {
            CrashLog.Write(e);
            Diag.Log("Fehler: " + e);
            Environment.Exit(1);
        }
    }

    // ------------------------------------------------------------ Anzeige

    private void Render()
    {
        var (dot, hintergrund) = _vm.Connection switch
        {
            ConnectionState.Connected => ("SystemFillColorSuccessBrush", "SystemFillColorSuccessBackgroundBrush"),
            ConnectionState.Connecting => ("SystemFillColorCautionBrush", "SystemFillColorCautionBackgroundBrush"),
            _ => ("SystemFillColorCriticalBrush", "SystemFillColorCriticalBackgroundBrush"),
        };
        _statusDot.Fill = Resource(dot) as Brush;
        _statusPill.Background = Resource(hintergrund) as Brush;
        _statusText.Text = _vm.Connection switch
        {
            ConnectionState.Connected => "Verbunden",
            ConnectionState.Connecting => "Verbinde …",
            _ => "Getrennt",
        };

        _connectButton.Content = IconContent(IconConnect, _vm.ConnectButtonText);
        _connectButton.IsEnabled = _vm.ConnectButtonEnabled;
        _connectButton.Style = _vm.Connection == ConnectionState.Disconnected ? Resource("AccentButtonStyle") as Style : null;
        _portBox.IsEnabled = _baudBox.IsEnabled = _vm.Connection == ConnectionState.Disconnected;

        var t = _vm.Temperature;
        _nozzle.Show(t?.NozzleIst, t?.NozzleSoll, _vm.NozzleOverride);
        _bed.Show(t?.BedIst, t?.BedSoll, _vm.BedOverride);

        _fileText.Text = _vm.FileText;
        _fileText.Opacity = _vm.HasFile ? 1.0 : 0.6;
        _progressText.Text = _vm.LinesText;
        _progressBar.Value = _vm.ProgressValue;
        _percentText.Text = _vm.HasFile ? $"{(int)_vm.ProgressValue} %" : "";
        _pauseButton.Content = IconContent(_vm.IsPaused ? IconPlay : IconPause, _vm.PauseButtonText);
        _pauseButton.IsEnabled = _stopButton.IsEnabled = _vm.IsPrinting;
        _startButton.IsEnabled = !_vm.IsPrinting;
        _progressBar.ShowPaused = _vm.IsPaused;

        if (_vm.IsPrinting != _keepAwake)
        {
            _keepAwake = _vm.IsPrinting;
            NativeMethods.KeepAwake(_keepAwake); // Rechner darf während des Drucks nicht schlafen
        }

        RunDemoScenario();
    }

    private void RenderPorts()
    {
        _portBox.Items.Clear();
        foreach (var p in _vm.Ports)
        {
            _portBox.Items.Add(p.Display);
        }

        _portBox.SelectedIndex = _vm.DefaultPortIndex;
    }

    private void ScheduleLogRefresh()
    {
        if (_logDirty)
        {
            return;
        }

        _logDirty = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _logDirty = false;
            _logText.Text = string.Join("\n", _vm.LogLines);
            _logScroll.UpdateLayout();
            _logScroll.ChangeView(null, _logScroll.ScrollableHeight, null, true);
        });
    }

    private void SizeWindow()
    {
        var scale = NativeMethods.ScaleFor(_hwnd);
        var (breite, hoehe) = _options.ScreenshotPath is null ? (1060, 880) : (1000, 740);
        var w = (int)(breite * scale);
        var h = (int)(hoehe * scale);
        if (_options.ScreenshotPath is null)
        {
            // Kleine Schul-Laptops: nicht größer als der Bildschirm.
            var bereich = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            w = Math.Min(w, bereich.Width);
            h = Math.Min(h, bereich.Height);
            AppWindow.Move(new PointInt32(bereich.X + ((bereich.Width - w) / 2), bereich.Y + ((bereich.Height - h) / 2)));
        }
        else
        {
            AppWindow.Move(new PointInt32(0, 0));
        }

        AppWindow.Resize(new SizeInt32(w, h));
    }

    // ------------------------------------------------------------ Bausteine

    private static Border Card(string titel, UIElement inhalt, UIElement? rechts = null)
    {
        var g = new Grid { RowSpacing = 10 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(g, new TextBlock { Text = titel, Style = Resource("CardHeaderStyle") as Style }, 0, 0);
        if (rechts is not null)
        {
            Place(g, rechts, 0, 1);
        }

        Place(g, inhalt, 1, 0, 2);
        return new Border { Style = Resource("CardStyle") as Style, Child = g };
    }

    private static Border Badge(string text) => new()
    {
        Background = Resource("AccentFillColorDefaultBrush") as Brush,
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 1, 6, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Resource("TextOnAccentFillColorPrimaryBrush") as Brush,
        },
    };

    internal static object? Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out var wert) ? wert : null;

    private static StackPanel IconContent(string glyph, string text) =>
        Row(8, new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text });

    private static Button IconButton(string glyph, string text, Action klick)
    {
        var b = new Button { Content = IconContent(glyph, text) };
        b.Click += (_, _) => klick();
        return b;
    }

    private static Button TextButton(string text, Action klick)
    {
        var b = new Button { Content = text, MinWidth = 72 };
        b.Click += (_, _) => klick();
        return b;
    }

    private static Button PadButton(string glyph, string text, Action klick)
    {
        var b = new Button
        {
            Width = 52,
            Height = 44,
            Padding = new Thickness(0),
            Content = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new FontIcon { Glyph = glyph, FontSize = 13 },
                    new TextBlock { Text = text, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };
        b.Click += (_, _) => klick();
        return b;
    }

    private Button JogButton(string glyph, string text, char achse, int richtung) =>
        PadButton(glyph, text, () => _vm.Jog(achse, richtung, MainViewModel.StepSizes[Math.Max(0, _stepBox.SelectedIndex)]));

    private static StackPanel Row(double abstand, params UIElement[] elemente)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = abstand };
        foreach (var e in elemente)
        {
            if (e is FrameworkElement { VerticalAlignment: VerticalAlignment.Stretch } fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
            }

            p.Children.Add(e);
        }

        return p;
    }

    private static void Place(Grid g, UIElement e, int row, int col, int colSpan = 1)
    {
        Grid.SetRow((FrameworkElement)e, row);
        Grid.SetColumn((FrameworkElement)e, col);
        Grid.SetColumnSpan((FrameworkElement)e, colSpan);
        g.Children.Add(e);
    }

    /// <summary>Anzeige-Bausteine für einen Heizer (Düse oder Bett).</summary>
    private sealed class HeaterView
    {
        private readonly int _max;

        public HeaterView(string name, string vorgabe, int max)
        {
            Name = name;
            _max = max;
            Input = new TextBox { Text = vorgabe, Width = 72, TextAlignment = TextAlignment.Right };
            ToolTipService.SetToolTip(Input, $"1 bis {max} °C, nur ganze Zahlen");
            Current = new TextBlock { Text = "--", Style = Resource("BigNumberStyle") as Style };
            Target = new TextBlock { Text = "Ziel --", Style = Resource("SecondaryTextStyle") as Style, FontSize = 13 };
            Bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0 };
            Override = new TextBlock { Style = Resource("SecondaryTextStyle") as Style, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
            BadgeText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold };
            Badge = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 3),
                Child = BadgeText,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        public string Name { get; }

        public TextBox Input { get; }

        public TextBlock Current { get; }

        public TextBlock Target { get; }

        public ProgressBar Bar { get; }

        public TextBlock Override { get; }

        public Border Badge { get; }

        private TextBlock BadgeText { get; }

        public void Show(double? ist, double? soll, int? eigen)
        {
            Current.Text = ist is double i ? i.ToString("F0", CultureInfo.InvariantCulture) : "--";
            Target.Text = soll switch
            {
                null => "Ziel --",
                <= 0 => "Heizung aus",
                double s => $"Ziel {s.ToString("F0", CultureInfo.InvariantCulture)} °C",
            };

            Bar.Value = ist is double a && soll is > 0 ? Math.Clamp(100 * a / soll.Value, 0, 100) : 0;
            Bar.Opacity = soll is > 0 ? 1.0 : 0.35;

            var heizt = soll is > 0 && ist is double b && b < soll.Value - 2;
            var bereit = soll is > 0 && !heizt;
            BadgeText.Text = heizt ? "heizt" : bereit ? "bereit" : "aus";
            Badge.Background = Resource(heizt ? "SystemFillColorCautionBackgroundBrush" : bereit ? "SystemFillColorSuccessBackgroundBrush" : "SubtleFillColorSecondaryBrush") as Brush;
            BadgeText.Foreground = Resource(heizt ? "SystemFillColorCautionBrush" : bereit ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush") as Brush;

            Override.Text = eigen is int e
                ? $"Beim Druck: {e} °C (eigener Wert)"
                : $"Beim Druck: Wert aus der Datei (max. {_max} °C)";
        }
    }
}
