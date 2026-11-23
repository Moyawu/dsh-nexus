namespace DshTrayLauncher.Runners;

public enum RunnerState
{
    Stopped,
    Starting,
    Running,
    Error
}

public interface IProcessRunner : IDisposable
{
    string Id { get; }
    string DisplayName { get; }
    string WorkingDirectory { get; }
    string Command { get; }
    string ServerUrl { get; }
    int Port { get; }
    RunnerState State { get; }

    event Action<LogEntry>? LogReceived;
    event Action<RunnerState>? StateChanged;

    Task StartAsync();
    Task StopAsync();
    Task RestartAsync();
}
