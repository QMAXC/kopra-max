using Microsoft.UI.Xaml;

namespace KobraMax.App;

public partial class App : Application
{
    private readonly StartupOptions _options = StartupOptions.FromCommandLine();
    private MainWindow? _window;

    public App()
    {
        Diag.Enabled = _options.ScreenshotPath is not null;
        Diag.Log("App-Start: " + Environment.CommandLine);

        // Muss vor dem Laden der Ressourcen passieren.
        if (_options.Theme == "dark")
        {
            RequestedTheme = ApplicationTheme.Dark;
        }
        else if (_options.Theme == "light")
        {
            RequestedTheme = ApplicationTheme.Light;
        }

        InitializeComponent();
        Diag.Log("Ressourcen geladen");
        UnhandledException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            Diag.Log("Unbehandelt: " + e.Exception);
            e.Handled = _options.ScreenshotPath is null; // im Normalbetrieb lieber weiterlaufen als mitten im Druck abstürzen
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                CrashLog.Write(ex);
            }
        };
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Diag.Log("OnLaunched");
        _window = new MainWindow(_options);
        _window.Activate();
        Diag.Log("Fenster aktiviert");
    }
}
