using System.Runtime.InteropServices;
using DshTrayLauncher.Config;
using DshTrayLauncher.Events;
using DshTrayLauncher.Runners;
using DshTrayLauncher.Services;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace DshTrayLauncher;

public sealed class TrayIcon : IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly Action _openConfigAction;
    private readonly Action _exitAction;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _statusItem;
    private readonly WinForms.ToolStripMenuItem _toolsMenuItem;

    private string _currentRunnerId = "dsh";
    private string _currentRunnerName = "DeepSeek Harness";
    private string _currentUrl = "http://127.0.0.1:3080";
    private string _lastLogLine = "Логов пока нет";
    private RunnerState _currentState = RunnerState.Stopped;
    private Drawing.Icon? _currentIcon;
    private bool _disposed;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public TrayIcon(
        IEventBus eventBus,
        Action openConfigAction,
        Action exitAction)
    {
        _eventBus = eventBus;
        _openConfigAction = openConfigAction;
        _exitAction = exitAction;

        var menu = new WinForms.ContextMenuStrip();

        _statusItem = new WinForms.ToolStripMenuItem("● DSH: Инициализация...")
        {
            Enabled = false
        };

        var toggleItem = new WinForms.ToolStripMenuItem("Развернуть / Скрыть окно логов");
        toggleItem.Font = new Drawing.Font(toggleItem.Font, Drawing.FontStyle.Bold);
        toggleItem.Click += (_, _) => _eventBus.Publish(new ToggleLogWindowEvent());

        var openBrowserItem = new WinForms.ToolStripMenuItem("Открыть Web UI в браузере");
        openBrowserItem.Click += (_, _) => _eventBus.Publish(new OpenBrowserEvent(_currentUrl));

        var restartItem = new WinForms.ToolStripMenuItem("Перезапустить инструмент");
        restartItem.Click += (_, _) => _eventBus.Publish(new RunnerCommandEvent(_currentRunnerId, RunnerCommand.Restart));

        _toolsMenuItem = new WinForms.ToolStripMenuItem("Инструменты");

        var openConfigItem = new WinForms.ToolStripMenuItem("Открыть config.json");
        openConfigItem.Click += (_, _) => _openConfigAction();

        var reloadConfigItem = new WinForms.ToolStripMenuItem("Перезагрузить конфиг");
        reloadConfigItem.Click += (_, _) => _eventBus.Publish(new ReloadConfigEvent());

        var copyUrlItem = new WinForms.ToolStripMenuItem("Скопировать URL сервера");
        copyUrlItem.Click += (_, _) =>
        {
            WinForms.Clipboard.SetText(_currentUrl);
            ShowBalloon("URL скопирован", _currentUrl);
        };

        var copyLastLogItem = new WinForms.ToolStripMenuItem("Скопировать последний статус / лог");
        copyLastLogItem.Click += (_, _) =>
        {
            string payload = $"{_statusItem.Text} | {_lastLogLine}";
            WinForms.Clipboard.SetText(payload);
            ShowBalloon("Статус скопирован", payload);
        };

        var shortcutItem = new WinForms.ToolStripMenuItem("Создать ярлык на Рабочем столе");
        shortcutItem.Click += (_, _) =>
        {
            try
            {
                string path = ShortcutCreator.CreateDesktopShortcut();
                ShowBalloon("Ярлык создан", $"Ярлык сохранён:\n{path}");
            }
            catch (Exception ex)
            {
                ShowBalloon("Ошибка создания ярлыка", ex.Message, WinForms.ToolTipIcon.Error);
            }
        };

        var exitItem = new WinForms.ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => _exitAction();

        menu.Items.Add(_statusItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(toggleItem);
        menu.Items.Add(openBrowserItem);
        menu.Items.Add(restartItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_toolsMenuItem);
        menu.Items.Add(openConfigItem);
        menu.Items.Add(reloadConfigItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(copyUrlItem);
        menu.Items.Add(copyLastLogItem);
        menu.Items.Add(shortcutItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new WinForms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Text = "DSH Nexus"
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                _eventBus.Publish(new ToggleLogWindowEvent());
        };

        _notifyIcon.DoubleClick += (_, _) => _eventBus.Publish(new ToggleLogWindowEvent());

        _subscriptions.Add(_eventBus.Subscribe<RunnerStateChangedEvent>(OnServerStateChanged));
        _subscriptions.Add(_eventBus.Subscribe<RunnerLogEvent>(OnLogReceived));
        _subscriptions.Add(_eventBus.Subscribe<UiNotificationEvent>(OnNotificationReceived));
        _subscriptions.Add(_eventBus.Subscribe<ToolProfilesUpdatedEvent>(OnProfilesUpdated));

        UpdateVisualState(_currentRunnerId, RunnerState.Stopped);
    }

    public void ShowBalloon(string title, string text, WinForms.ToolTipIcon icon = WinForms.ToolTipIcon.Info)
    {
        if (!_disposed)
            _notifyIcon.ShowBalloonTip(2500, title, text, icon);
    }

    private void OnProfilesUpdated(ToolProfilesUpdatedEvent ev)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => RebuildToolsMenu(ev.Profiles, ev.ActiveToolId));
            return;
        }

        RebuildToolsMenu(ev.Profiles, ev.ActiveToolId);
    }

    private void RebuildToolsMenu(IReadOnlyList<ToolProfileConfig> profiles, string activeToolId)
    {
        if (_disposed)
            return;

        _toolsMenuItem.DropDownItems.Clear();

        foreach (var profile in profiles)
        {
            bool isActive = profile.Id == activeToolId;
            if (isActive)
            {
                _currentRunnerId = profile.Id;
                _currentRunnerName = profile.Name;
                _currentUrl = profile.DefaultUrl;
            }

            var item = new WinForms.ToolStripMenuItem(profile.Name)
            {
                Checked = isActive
            };

            string targetId = profile.Id;
            item.Click += (_, _) => _eventBus.Publish(new SwitchToolProfileEvent(targetId));
            _toolsMenuItem.DropDownItems.Add(item);
        }

        UpdateVisualState(_currentRunnerId, _currentState);
    }

    private void OnLogReceived(RunnerLogEvent ev)
    {
        if (!string.IsNullOrWhiteSpace(ev.Message))
            _lastLogLine = ev.Message.Trim();

        var urls = LogUrlParser.ExtractUrls(ev.Message);
        if (urls.Count > 0)
            _currentUrl = urls[0];
    }

    private void OnNotificationReceived(UiNotificationEvent ev)
    {
        var icon = ev.IsError ? WinForms.ToolTipIcon.Error : WinForms.ToolTipIcon.Info;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ShowBalloon(ev.Title, ev.Message, icon));
            return;
        }

        ShowBalloon(ev.Title, ev.Message, icon);
    }

    private void OnServerStateChanged(RunnerStateChangedEvent ev)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => UpdateVisualState(ev.RunnerId, ev.NewState));
            return;
        }

        UpdateVisualState(ev.RunnerId, ev.NewState);
    }

    private void UpdateVisualState(string runnerId, RunnerState state)
    {
        if (_disposed)
            return;

        _currentRunnerId = runnerId;
        _currentState = state;

        string name = string.IsNullOrWhiteSpace(_currentRunnerName)
            ? runnerId.ToUpperInvariant()
            : _currentRunnerName;

        (string label, string shortStatus, Drawing.Color dotColor) = state switch
        {
            RunnerState.Running => ($"● {name}: Работает ({_currentUrl})", "Работает", Drawing.Color.FromArgb(255, 34, 197, 94)),
            RunnerState.Starting => ($"● {name}: Запуск...", "Запуск", Drawing.Color.FromArgb(255, 245, 158, 11)),
            RunnerState.Error => ($"● {name}: Ошибка запуска", "Ошибка", Drawing.Color.FromArgb(255, 239, 68, 68)),
            _ => ($"● {name}: Остановлен", "Остановлен", Drawing.Color.FromArgb(255, 148, 163, 184))
        };

        _statusItem.Text = label;

        string tooltip = $"DSH Nexus • {name}: {shortStatus} ({_currentUrl})";
        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;

        using var bmp = ShortcutCreator.CreateLauncherBitmap(32, dotColor);
        IntPtr hIcon = bmp.GetHicon();
        var newIcon = Drawing.Icon.FromHandle(hIcon);
        var oldIcon = _currentIcon;

        _currentIcon = newIcon;
        _notifyIcon.Icon = newIcon;

        if (oldIcon is not null)
        {
            IntPtr oldHandle = oldIcon.Handle;
            oldIcon.Dispose();
            DestroyIcon(oldHandle);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        if (_currentIcon is not null)
        {
            IntPtr h = _currentIcon.Handle;
            _currentIcon.Dispose();
            DestroyIcon(h);
            _currentIcon = null;
        }
    }
}