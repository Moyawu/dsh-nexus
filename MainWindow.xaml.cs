using System.Windows.Media.Animation;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using DshTrayLauncher.Events;
using DshTrayLauncher.Runners;
using DshTrayLauncher.Services;
using Controls = System.Windows.Controls;
using Media = System.Windows.Media;

namespace DshTrayLauncher;

public partial class MainWindow : Window
{
    private const int MaxLogLines = 4000;

    private readonly IEventBus _eventBus;
    private readonly Action _exitAction;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Queue<string> _lines = new();
    private string _currentRunnerId = "dsh";
    private string _currentRunnerName = "DeepSeek Harness";
    private string _currentUrl = "http://127.0.0.1:3080";
    private string _searchQuery = string.Empty;
    private int _matchedCount;
    private bool _allowRealClose;

    public ObservableCollection<string> DiscoveredUrls { get; } = [];

    public MainWindow(IEventBus eventBus, Action exitAction)
    {
        InitializeComponent();
        Icon = ShortcutCreator.CreateWindowIcon();
        _eventBus = eventBus;
        _exitAction = exitAction;
        UrlsItemsControl.ItemsSource = DiscoveredUrls;

        _subscriptions.Add(_eventBus.Subscribe<RunnerLogEvent>(OnLogReceived));
        _subscriptions.Add(_eventBus.Subscribe<RunnerStateChangedEvent>(OnStateChanged));
        _subscriptions.Add(_eventBus.Subscribe<ToolProfilesUpdatedEvent>(OnProfilesUpdated));
        _subscriptions.Add(_eventBus.Subscribe<SwitchToolProfileEvent>(_ => Dispatcher.BeginInvoke(ClearDiscoveredUrls)));
        _subscriptions.Add(_eventBus.Subscribe<ThemeChangedEvent>(OnThemeChanged));
    }

    public void ShowAndActivate()
    {
        bool wasHidden = !IsVisible;
        if (wasHidden)
        {
            Opacity = 0;
            Show();
        }

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        if (wasHidden)
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeIn);
        }

        if (AutoScrollCheckBox.IsChecked == true)
            LogTextBox.ScrollToEnd();
    }

    public void PrepareForExit()
    {
        _allowRealClose = true;
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowRealClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void OnThemeChanged(ThemeChangedEvent ev)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ApplyTheme(ev.Theme));
            return;
        }

        ApplyTheme(ev.Theme);
    }

    private void ApplyTheme(string theme)
    {
        bool isLight = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        ThemeToggleButton.Content = isLight ? "Тема: Light" : "Тема: Dark";

        if (isLight)
        {
            Background = Brush(241, 245, 249);
            Foreground = Brush(15, 23, 42);
            TopBarBorder.Background = Brush(255, 255, 255);
            TopBarBorder.BorderBrush = Brush(203, 213, 225);
            StatusText.Foreground = Brush(15, 23, 42);
            UrlText.Foreground = Brush(71, 85, 105);

            UrlPanel.Background = Brush(226, 232, 240);
            UrlPanel.BorderBrush = Brush(203, 213, 225);
            UrlPanelHintText.Foreground = Brush(51, 65, 85);

            SearchBorder.Background = Brush(255, 255, 255);
            SearchBorder.BorderBrush = Brush(203, 213, 225);
            SearchLabelText.Foreground = Brush(71, 85, 105);
            SearchTextBox.Background = Brush(248, 250, 252);
            SearchTextBox.Foreground = Brush(15, 23, 42);
            SearchTextBox.CaretBrush = Brush(15, 23, 42);
            SearchTextBox.BorderBrush = Brush(203, 213, 225);
            SearchCountText.Foreground = Brush(71, 85, 105);

            LogBorder.Background = Brush(255, 255, 255);
            LogBorder.BorderBrush = Brush(203, 213, 225);
            LogTextBox.Foreground = Brush(30, 41, 59);

            AutoScrollCheckBox.Foreground = Brush(30, 41, 59);
            PathInfoText.Foreground = Brush(71, 85, 105);
        }
        else
        {
            Background = Brush(17, 20, 29);
            Foreground = Brush(226, 232, 240);
            TopBarBorder.Background = Brush(23, 28, 40);
            TopBarBorder.BorderBrush = Brush(38, 47, 69);
            StatusText.Foreground = Brush(248, 250, 252);
            UrlText.Foreground = Brush(148, 163, 184);

            UrlPanel.Background = Brush(19, 25, 38);
            UrlPanel.BorderBrush = Brush(36, 49, 77);
            UrlPanelHintText.Foreground = Brush(148, 163, 184);

            SearchBorder.Background = Brush(23, 28, 40);
            SearchBorder.BorderBrush = Brush(38, 47, 69);
            SearchLabelText.Foreground = Brush(148, 163, 184);
            SearchTextBox.Background = Brush(11, 14, 20);
            SearchTextBox.Foreground = Brush(226, 232, 240);
            SearchTextBox.CaretBrush = Brush(226, 232, 240);
            SearchTextBox.BorderBrush = Brush(46, 56, 82);
            SearchCountText.Foreground = Brush(148, 163, 184);

            LogBorder.Background = Brush(11, 14, 20);
            LogBorder.BorderBrush = Brush(35, 43, 62);
            LogTextBox.Foreground = Brush(209, 213, 219);

            AutoScrollCheckBox.Foreground = Brush(203, 213, 225);
            PathInfoText.Foreground = Brush(100, 116, 139);
        }
    }

    private static Media.SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(Media.Color.FromRgb(r, g, b));

    private void OnProfilesUpdated(ToolProfilesUpdatedEvent ev)
    {
        var active = ev.Profiles.FirstOrDefault(p => p.Id == ev.ActiveToolId);
        if (active is null)
            return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ApplyProfileInfo(active.Id, active.Name, active.DefaultUrl, active.WorkingDirectory));
            return;
        }

        ApplyProfileInfo(active.Id, active.Name, active.DefaultUrl, active.WorkingDirectory);
    }

    private void ApplyProfileInfo(string id, string name, string defaultUrl, string workingDir)
    {
        _currentRunnerId = id;
        _currentRunnerName = name;
        _currentUrl = defaultUrl;
        UrlText.Text = $"  •  {_currentUrl}";
        if (!string.IsNullOrWhiteSpace(workingDir))
            PathInfoText.Text = $"Каталог: {workingDir}";
    }

    private void OnLogReceived(RunnerLogEvent ev)
    {
        var entry = new LogEntry
        {
            Timestamp = ev.Timestamp,
            Message = ev.Message,
            IsError = ev.IsError
        };

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendLogEntry(entry));
            return;
        }

        AppendLogEntry(entry);
    }

    private bool MatchesFilter(string line) =>
        string.IsNullOrEmpty(_searchQuery) ||
        line.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase);

    private void RebuildLogView()
    {
        var sb = new StringBuilder();
        _matchedCount = 0;

        foreach (string item in _lines)
        {
            if (MatchesFilter(item))
            {
                sb.AppendLine(item);
                _matchedCount++;
            }
        }

        LogTextBox.Text = sb.ToString();
        UpdateSearchCounter();

        if (AutoScrollCheckBox.IsChecked == true && IsVisible)
            LogTextBox.ScrollToEnd();
    }

    private void UpdateSearchCounter()
    {
        SearchCountText.Text = string.IsNullOrEmpty(_searchQuery)
            ? $"Строк: {_lines.Count}"
            : $"Найдено: {_matchedCount} из {_lines.Count}";
    }

    private void AppendLogEntry(LogEntry entry)
    {
        var urls = LogUrlParser.ExtractUrls(entry.Message);
        if (urls.Count > 0)
        {
            _currentUrl = urls[0];
            UrlText.Text = $"  •  {_currentUrl}";

            foreach (string url in urls)
            {
                if (!DiscoveredUrls.Contains(url))
                    DiscoveredUrls.Add(url);
            }

            UrlPanel.Visibility = DiscoveredUrls.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        string line = entry.FormattedLine;
        _lines.Enqueue(line);

        if (_lines.Count > MaxLogLines)
        {
            while (_lines.Count > MaxLogLines - 300)
                _lines.Dequeue();

            RebuildLogView();
            return;
        }

        if (MatchesFilter(line))
        {
            _matchedCount++;
            LogTextBox.AppendText(line + Environment.NewLine);
            if (AutoScrollCheckBox.IsChecked == true && IsVisible)
                LogTextBox.ScrollToEnd();
        }

        UpdateSearchCounter();
    }

    private void OnStateChanged(RunnerStateChangedEvent ev)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateStatusUi(ev));
            return;
        }

        UpdateStatusUi(ev);
    }

    private void UpdateStatusUi(RunnerStateChangedEvent ev)
    {
        _currentRunnerId = ev.RunnerId;
        UrlText.Text = $"  •  {_currentUrl}";

        string name = string.IsNullOrWhiteSpace(_currentRunnerName)
            ? ev.RunnerId.ToUpperInvariant()
            : _currentRunnerName;

        switch (ev.NewState)
        {
            case RunnerState.Running:
                StatusText.Text = $"{name} работает";
                StatusDot.Fill = Brush(34, 197, 94);
                break;

            case RunnerState.Starting:
                StatusText.Text = $"Запуск {name}...";
                StatusDot.Fill = Brush(245, 158, 11);
                break;

            case RunnerState.Error:
                StatusText.Text = "Ошибка запуска / сбой процесса";
                StatusDot.Fill = Brush(239, 68, 68);
                break;

            default:
                StatusText.Text = "Сервер остановлен";
                StatusDot.Fill = Brush(148, 163, 184);
                break;
        }
    }

    private void ClearDiscoveredUrls()
    {
        DiscoveredUrls.Clear();
        UrlPanel.Visibility = Visibility.Collapsed;
    }

    private void SearchTextBox_TextChanged(object sender, Controls.TextChangedEventArgs e)
    {
        _searchQuery = SearchTextBox.Text;
        RebuildLogView();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchTextBox.Clear();

    private void ToggleTheme_Click(object sender, RoutedEventArgs e) =>
        _eventBus.Publish(new ToggleThemeEvent());

    private void UrlButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Controls.Button { Tag: string url })
            _eventBus.Publish(new OpenBrowserEvent(url));
    }

    private void UrlButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Controls.Button { Tag: string url })
        {
            System.Windows.Clipboard.SetText(url);
            _eventBus.Publish(new UiNotificationEvent("Ссылка скопирована", url));
            e.Handled = true;
        }
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e) =>
        _eventBus.Publish(new OpenBrowserEvent(_currentUrl));

    private void Restart_Click(object sender, RoutedEventArgs e) =>
        _eventBus.Publish(new RunnerCommandEvent(_currentRunnerId, RunnerCommand.Restart));

    private void CreateShortcut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = ShortcutCreator.CreateDesktopShortcut();
            _eventBus.Publish(new UiNotificationEvent("Ярлык создан", $"Ярлык сохранён на Рабочем столе:\n{path}"));
        }
        catch (Exception ex)
        {
            _eventBus.Publish(new UiNotificationEvent("Ошибка создания ярлыка", ex.Message, IsError: true));
        }
    }

    private void HideToTray_Click(object sender, RoutedEventArgs e) => Hide();

    private void CopyLogs_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(LogTextBox.Text))
            System.Windows.Clipboard.SetText(LogTextBox.Text);
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        _lines.Clear();
        _matchedCount = 0;
        LogTextBox.Clear();
        ClearDiscoveredUrls();
        UpdateSearchCounter();
    }

    private void ExitApp_Click(object sender, RoutedEventArgs e) => _exitAction();
}