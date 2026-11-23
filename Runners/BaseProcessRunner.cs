using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DshTrayLauncher.Events;

namespace DshTrayLauncher.Runners;

public abstract partial class BaseProcessRunner : IProcessRunner
{
    protected readonly IEventBus _bus;
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string ServerUrl { get; set; } = "http://127.0.0.1:3080";
    public int Port { get; set; } = 3080;
    public RunnerState State { get; private set; } = RunnerState.Stopped;

    public event Action<LogEntry>? LogReceived;
    public event Action<RunnerState>? StateChanged;

    protected Process? _process;
    protected readonly SemaphoreSlim _syncLock = new(1, 1);
    protected bool _manualStopRequested;
    protected bool _disposed;

    protected BaseProcessRunner(IEventBus bus)
    {
        _bus = bus;
    }

    [GeneratedRegex(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])")]
    private static partial Regex AnsiEscapeRegex();

    [GeneratedRegex(@"https?://(?:127\.0\.0\.1|localhost):\d+")]
    private static partial Regex LocalUrlRegex();

    public virtual string[] ProcessNamesToCleanup => new[] { "node", "cmd" };

    public virtual async Task StartAsync()
    {
        await _syncLock.WaitAsync();
        try
        {
            if (_process is { HasExited: false })
            {
                _bus.Publish(new UiNotificationEvent("Инфо", $"Сервер {DisplayName} уже запущен."));
                return;
            }

            if (!Directory.Exists(WorkingDirectory))
            {
                SetState(RunnerState.Error);
                _bus.Publish(new RunnerLogEvent($"Рабочая директория не найдена: {WorkingDirectory}", true, DateTime.Now));
                return;
            }

            _manualStopRequested = false;
            SetState(RunnerState.Starting);
            _bus.Publish(new RunnerLogEvent($"Запуск {DisplayName} в {WorkingDirectory}...", false, DateTime.Now));

            await FreePortIfOccupiedByNodeAsync(Port);

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {Command}",
                WorkingDirectory = WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.Environment["FORCE_COLOR"] = "0";
            startInfo.Environment["NO_COLOR"] = "1";

            var proc = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) HandleOutputLine(e.Data, isError: false); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) HandleOutputLine(e.Data, isError: true); };
            proc.Exited += (_, _) =>
            {
                var exitCode = -1;
                try { exitCode = proc.ExitCode; } catch { }
                _bus.Publish(new RunnerLogEvent($"Процесс завершился (код: {exitCode}).", !_manualStopRequested && exitCode != 0, DateTime.Now));
                SetState(_manualStopRequested || exitCode == 0 ? RunnerState.Stopped : RunnerState.Error);
            };

            if (!proc.Start())
            {
                SetState(RunnerState.Error);
                _bus.Publish(new RunnerLogEvent($"Не удалось запустить {DisplayName}.", true, DateTime.Now));
                return;
            }

            _process = proc;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            _ = MonitorPortReadinessAsync(proc);
        }
        catch (Exception ex)
        {
            SetState(RunnerState.Error);
            _bus.Publish(new RunnerLogEvent($"Ошибка при запуске {DisplayName}: {ex.Message}", true, DateTime.Now));
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public virtual async Task StopAsync()
    {
        await _syncLock.WaitAsync();
        try
        {
            await StopInternalAsync();
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public virtual async Task RestartAsync()
    {
        await _syncLock.WaitAsync();
        try
        {
            _bus.Publish(new RunnerLogEvent($"Перезапуск {DisplayName}...", false, DateTime.Now));
            await StopInternalAsync();
            await Task.Delay(500);
        }
        finally
        {
            _syncLock.Release();
        }

        await StartAsync();
    }

    private async Task StopInternalAsync()
    {
        _manualStopRequested = true;
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _bus.Publish(new RunnerLogEvent($"Остановка {DisplayName}...", false, DateTime.Now));
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch { }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }
        await FreePortIfOccupiedByNodeAsync(Port);
        SetState(RunnerState.Stopped);
    }

    private async Task MonitorPortReadinessAsync(Process trackedProcess)
    {
        for (int i = 0; i < 40; i++)
        {
            if (_disposed || trackedProcess.HasExited || State == RunnerState.Running) return;
            if (IsPortListening(Port))
            {
                SetState(RunnerState.Running);
                _bus.Publish(new RunnerLogEvent($"Сервер {DisplayName} готов и слушает {ServerUrl}", false, DateTime.Now));
                return;
            }
            await Task.Delay(500);
        }
    }

    private void HandleOutputLine(string rawLine, bool isError)
    {
        var cleanLine = AnsiEscapeRegex().Replace(rawLine, string.Empty).TrimEnd();
        if (string.IsNullOrWhiteSpace(cleanLine)) return;

        var urlMatch = LocalUrlRegex().Match(cleanLine);
        if (urlMatch.Success)
        {
            ServerUrl = urlMatch.Value;
            if (Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) && uri.Port > 0)
                Port = uri.Port;

            if (State == RunnerState.Starting) SetState(RunnerState.Running);
        }

        bool treatAsError = isError &&
            (cleanLine.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
             cleanLine.Contains("ERR!", StringComparison.OrdinalIgnoreCase) ||
             cleanLine.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
             cleanLine.Contains("EADDRINUSE", StringComparison.OrdinalIgnoreCase));

        _bus.Publish(new RunnerLogEvent(cleanLine, treatAsError, DateTime.Now));
    }

    private static bool IsPortListening(int port)
    {
        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            return listeners.Any(ep => ep.Port == port);
        }
        catch { return false; }
    }

    private async Task FreePortIfOccupiedByNodeAsync(int port)
    {
        if (!IsPortListening(port)) return;
        try
        {
            using var netstat = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netstat.exe",
                    Arguments = "-ano -p TCP",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                }
            };
            netstat.Start();
            string output = await netstat.StandardOutput.ReadToEndAsync();
            await netstat.WaitForExitAsync();

            var pids = new HashSet<int>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.Contains($":{port} ") || !line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && int.TryParse(parts[^1], out int pid) && pid > 0) pids.Add(pid);
            }

            foreach (int pid in pids)
            {
                try
                {
                    using var existing = Process.GetProcessById(pid);
                    if (ProcessNamesToCleanup.Any(n => existing.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    {
                        _bus.Publish(new RunnerLogEvent($"Освобождение порта {port} (завершение {existing.ProcessName}.exe PID={pid})...", false, DateTime.Now));
                        existing.Kill(entireProcessTree: true);
                        await existing.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    protected void SetState(RunnerState newState)
    {
        if (State == newState) return;
        State = newState;
        StateChanged?.Invoke(newState);
        _bus.Publish(new RunnerStateChangedEvent(Id, newState));
    }

    protected void EmitLog(string message, bool isError)
    {
        LogReceived?.Invoke(new LogEntry { Timestamp = DateTime.Now, Message = message, IsError = isError });
        _bus.Publish(new RunnerLogEvent(message, isError, DateTime.Now));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _manualStopRequested = true;
        if (_process is not null)
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
            _process.Dispose();
            _process = null;
        }
        _syncLock.Dispose();
    }
}