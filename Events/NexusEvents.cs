using DshTrayLauncher.Config;
using DshTrayLauncher.Runners;

namespace DshTrayLauncher.Events;

public record RunnerLogEvent(string Message, bool IsError, DateTime Timestamp);
public record RunnerStateChangedEvent(string RunnerId, RunnerState NewState);
public record RunnerCommandEvent(string RunnerId, RunnerCommand Command);
public record UiNotificationEvent(string Title, string Message, bool IsError = false);
public record ToggleLogWindowEvent();
public record OpenBrowserEvent(string Url);

public record ToolProfilesUpdatedEvent(IReadOnlyList<ToolProfileConfig> Profiles, string ActiveToolId);
public record SwitchToolProfileEvent(string ToolId);
public record ReloadConfigEvent();
public record ToggleThemeEvent();
public record ThemeChangedEvent(string Theme);

public enum RunnerCommand
{
    Start,
    Stop,
    Restart
}