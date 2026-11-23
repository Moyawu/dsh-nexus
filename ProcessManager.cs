using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;

namespace DshTrayLauncher;

public enum DshServerState
{
    Stopped,
    Starting,
    Running,
    Error
}

public sealed partial class ProcessManager : IDisposable
{
    private Process? _process;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _manualStopRequested;
    private bool _disposed;

    public string WorkingDirectory { get; set; } = @"S:\Projects\dhs\deepseek-harness";
    public string StartCommand { get; set; } = "pnpm start:web --no-open";
    public string ServerUrl { get; private set; } = "http://127.0.0.1:3080";
    public int ServerPort { get; set; } = 3080;

    public DshServerState State { get; private set; } = DshServerState.Stopped;

    public event Action<LogEntry>? LogReceived;
    public event Action<DshServerState>? StateChanged;

    [GeneratedRegex(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])")]
    private static partial Regex AnsiEscapeRegex();

    [GeneratedRegex(@"https?://(?:127\.0\.1|localhost):\d+")]
    private static partial Regex LocalUrlRegex();

    public async Task StartAsync()
    {
        await _syncLock.WaitAsync();
        try
        {
            if (_process is { HasExited: false })
            {
                EmitLog("Сервер DeepSeek Harness уже запущен.", isError: false);
                return;
            }

            if (!Directory.Exists(WorkingDirectory))
            {
                SetState(DshServerState.Error);
                EmitLog($"Рабочая директория не найдена: {WorkingDirectory}", isError: true);
                return;
            }

            _manualStopRequested = false;
            SetState(DshServerState.Starting);
            EmitLog($"Запуск DeepSeek Harness в {WorkingDirectory}...", isError: false);

            await FreePortIfOccupiedByNodeAsync(ServerPort);

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {StartCommand}",
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

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    HandleOutputLine(e.Data, isError: false);
            };

            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    HandleOutputLine(e.Data, isError: true);
            };

            proc.Exited += (_, _) =>
            {
                var exitCode = -1;
                try { exitCode = proc.ExitCode; } catch { /* ignore */ }

                EmitLog($"Процесс DSH завершился (код выхода: {exitCode}).", isError: !_manualStopRequested && exitCode != 0);
                SetState(_manualStopRequested || exitCode == 0 ? DshServerState.Stopped : DshServerState.Error);
            };

            if (!proc.Start())
            {
                SetState(DshServerState.Error);
                EmitLog("Не удалось запустить процесс cmd.exe / pnpm.", isError: true);
                return;
            }

            _process = proc;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            _ = MonitorPortReadinessAsync(proc);
        }
        catch (Exception ex)
        {
            SetState(DshServerState.Error);
            EmitLog($"Ошибка при запуске DSH: {ex.Message}", isError: true);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task StopAsync()
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

    public async Task RestartAsync()
    {
        await _syncLock.WaitAsync();
        try
        {
            EmitLog("Перезапуск DeepSeek Harness...", isError: false);
            await StopInternalAsync();
            await Task.Delay(500);
        }
        finally
        {
            _syncLock.Release();
        }
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
                    EmitLog("Остановка дерева процессов DeepSeek Harness...", isError: false);
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex)
            {
                EmitLog($"Предупреждение при остановке процесса: {ex.Message}", isError: true);
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        await FreePortIfOccupiedByNodeAsync(ServerPort);
        SetState(DshServerState.Stopped);
    }

    private async Task MonitorPortReadinessAsync(Process trackedProcess)
    {
        for (int i = 0; i < 40; i++)
        {
            if (_disposed || trackedProcess.HasExited || State == DshServerState.Running)
                return;

            if (IsPortListening(ServerPort))
            {
                SetState(DshServerState.Running);
                EmitLog($"Веб-сервер DSH готов и слушает {ServerUrl}", isError: false);
                return;
            }

            await Task.Delay(500);
        }
    }

    private void HandleOutputLine(string rawLine, bool isError)
    {
        var cleanLine = AnsiEscapeRegex().Replace(rawLine, string.Empty).TrimEnd();
        if (string.IsNullOrWhiteSpace(cleanLine))
            return;

        var urlMatch = LocalUrlRegex().Match(cleanLine);
        if (urlMatch.Success)
        {
            ServerUrl = urlMatch.Value;
            if (Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) && uri.Port > 0)
                ServerPort = uri.Port;

            if (State == DshServerState.Starting)
                SetState(DshServerState.Running);
        }

        // pnpm/node часто пишут информационные логи в stderr; помечаем как ошибку только реальные сбои
        bool treatAsError = isError &&
            (cleanLine.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
             cleanLine.Contains("ERR!", StringComparison.OrdinalIgnoreCase) ||
             cleanLine.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
             cleanLine.Contains("EADDRINUSE", StringComparison.OrdinalIgnoreCase));

        EmitLog(cleanLine, treatAsError);
    }

    private static bool IsPortListening(int port)
    {
        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            return listeners.Any(ep => ep.Port == port);
        }
        catch
        {
            return false;
        }
    }

    private async Task FreePortIfOccupiedByNodeAsync(int port)
    {
        if (!IsPortListening(port))
            return;

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
                if (!line.Contains($":{port} ") || !line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && int.TryParse(parts[^1], out int pid) && pid > 0)
                    pids.Add(pid);
            }

            foreach (int pid in pids)
            {
                try
                {
                    using var existing = Process.GetProcessById(pid);
                    if (existing.ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase) ||
                        existing.ProcessName.Equals("cmd", StringComparison.OrdinalIgnoreCase))
                    {
                        EmitLog($"Освобождение занятого порта {port} (завершение {existing.ProcessName}.exe PID={pid})...", isError: false);
                        existing.Kill(entireProcessTree: true);
                        await existing.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                    }
                }
                catch { /* процесс уже завершился */ }
            }
        }
        catch { /* ignore */ }
    }

    private void SetState(DshServerState newState)
    {
        if (State == newState)
            return;

        State = newState;
        StateChanged?.Invoke(newState);
    }

    private void EmitLog(string message, bool isError)
    {
        LogReceived?.Invoke(new LogEntry
        {
            Timestamp = DateTime.Now,
            Message = message,
            IsError = isError
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _manualStopRequested = true;

        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
            _process?.Dispose();
        }
        catch { /* ignore */ }

        _syncLock.Dispose();
    }
}
