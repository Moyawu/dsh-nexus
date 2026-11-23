using System.IO;
using System.Threading;
using System.Windows;
using DshTrayLauncher.Config;
using DshTrayLauncher.Events;
using DshTrayLauncher.Runners;

namespace DshTrayLauncher;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Global\DshTrayLauncher_SingleInstance_Mutex";
    private const string ShowEventName = @"Global\DshTrayLauncher_ShowWindow_Event";

    private readonly List<IDisposable> _subscriptions = [];
    private readonly ConfigManager _configManager = new();
    private NexusConfig _config = new();
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private CancellationTokenSource? _ipcCts;
    private IProcessRunner? _runner;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private bool _isShuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => string.Equals(a, "--export-assets", StringComparison.OrdinalIgnoreCase)))
        {
            ShortcutCreator.ExportAssetIcons(@"S:\Projects\dsh-tray-launcher\assets\icons");
            Shutdown();
            return;
        }

        _instanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try
            {
                using var existingEvent = EventWaitHandle.OpenExisting(ShowEventName);
                existingEvent.Set();
            }
            catch { }

            Shutdown();
            return;
        }

        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _ipcCts = new CancellationTokenSource();
        StartShowWindowListener(_ipcCts.Token);

        _config = _configManager.Load();
        var activeProfile = _config.Tools.FirstOrDefault(t => t.Id == _config.ActiveToolId)
            ?? _config.Tools[0];

        var bus = new EventBus();
        _runner = RunnerFactory.Create(activeProfile, bus);

        _mainWindow = new MainWindow(bus, ShutdownApplication);
        MainWindow = _mainWindow;

        _trayIcon = new TrayIcon(bus, () => _configManager.OpenInEditor(), ShutdownApplication);

        _subscriptions.Add(bus.Subscribe<ToggleLogWindowEvent>(_ => Dispatcher.BeginInvoke(ToggleMainWindow)));
        _subscriptions.Add(bus.Subscribe<OpenBrowserEvent>(ev => ShortcutCreator.OpenBrowser(ev.Url)));

        _subscriptions.Add(bus.Subscribe<ToggleThemeEvent>(_ =>
        {
            _config.Theme = string.Equals(_config.Theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? "Dark"
                : "Light";
            _configManager.Save(_config);
            bus.Publish(new ThemeChangedEvent(_config.Theme));
        }));

        _subscriptions.Add(bus.Subscribe<RunnerCommandEvent>(async ev =>
        {
            if (_runner is null)
                return;

            if (!string.IsNullOrEmpty(ev.RunnerId) && ev.RunnerId != _runner.Id)
                return;

            switch (ev.Command)
            {
                case RunnerCommand.Start:
                    await _runner.StartAsync();
                    break;
                case RunnerCommand.Stop:
                    await _runner.StopAsync();
                    break;
                case RunnerCommand.Restart:
                    await _runner.RestartAsync();
                    break;
            }
        }));

        _subscriptions.Add(bus.Subscribe<SwitchToolProfileEvent>(async ev =>
        {
            var nextProfile = _config.Tools.FirstOrDefault(t => t.Id == ev.ToolId);
            if (nextProfile is null)
                return;

            if (_runner is not null)
            {
                await _runner.StopAsync();
                _runner.Dispose();
                _runner = null;
            }

            _config.ActiveToolId = nextProfile.Id;
            _configManager.Save(_config);

            _runner = RunnerFactory.Create(nextProfile, bus);
            bus.Publish(new ToolProfilesUpdatedEvent(_config.Tools, _config.ActiveToolId));
            await _runner.StartAsync();
        }));

        _subscriptions.Add(bus.Subscribe<ReloadConfigEvent>(_ =>
        {
            _config = _configManager.Load();
            bus.Publish(new ToolProfilesUpdatedEvent(_config.Tools, _config.ActiveToolId));
            bus.Publish(new ThemeChangedEvent(_config.Theme));
            bus.Publish(new UiNotificationEvent("Конфигурация обновлена", "Профили из config.json успешно перечитаны."));
        }));

        bus.Publish(new ToolProfilesUpdatedEvent(_config.Tools, _config.ActiveToolId));
        bus.Publish(new ThemeChangedEvent(_config.Theme));

        try
        {
            if (!File.Exists(ShortcutCreator.GetDesktopShortcutPath()))
                ShortcutCreator.CreateDesktopShortcut();
        }
        catch { }

        _trayIcon.ShowBalloon(
            "DSH Nexus",
            $"Запуск {activeProfile.Name} в фоне. Нажмите на иконку в трее, чтобы открыть логи.");

        await _runner.StartAsync();
    }

    private void StartShowWindowListener(CancellationToken token)
    {
        Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                if (_showWindowEvent?.WaitOne(500) == true && !token.IsCancellationRequested)
                    Dispatcher.BeginInvoke(() => _mainWindow?.ShowAndActivate());
            }
        }, token);
    }

    private void ToggleMainWindow()
    {
        if (_mainWindow is null)
            return;

        if (_mainWindow.IsVisible)
            _mainWindow.Hide();
        else
            _mainWindow.ShowAndActivate();
    }

    public async void ShutdownApplication()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;

        _ipcCts?.Cancel();
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();

        _mainWindow?.PrepareForExit();
        _trayIcon?.Dispose();

        if (_runner is not null)
        {
            await _runner.StopAsync();
            _runner.Dispose();
        }

        _showWindowEvent?.Dispose();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();

        Shutdown();
    }
}