using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DshTrayLauncher.Config;

public sealed class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private NexusConfig _currentConfig = new();

    public string ConfigDirectory { get; }
    public string ConfigFilePath { get; }

    public ConfigManager()
    {
        ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DshNexus");
        ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");
    }

    public NexusConfig Load()
    {
        Directory.CreateDirectory(ConfigDirectory);

        if (!File.Exists(ConfigFilePath))
        {
            _currentConfig = CreateDefaultConfig();
            Save(_currentConfig);
            return _currentConfig;
        }

        try
        {
            string json = File.ReadAllText(ConfigFilePath);
            var loaded = JsonSerializer.Deserialize<NexusConfig>(json, JsonOptions);
            if (loaded is null || loaded.Tools.Count == 0)
            {
                _currentConfig = CreateDefaultConfig();
                Save(_currentConfig);
            }
            else
            {
                _currentConfig = loaded;
            }
        }
        catch
        {
            _currentConfig = CreateDefaultConfig();
        }

        return _currentConfig;
    }

    public void Save(NexusConfig config)
    {
        _currentConfig = config;
        Directory.CreateDirectory(ConfigDirectory);
        string json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);
    }

    public void OpenInEditor()
    {
        if (!File.Exists(ConfigFilePath))
            Save(CreateDefaultConfig());

        Process.Start(new ProcessStartInfo
        {
            FileName = ConfigFilePath,
            UseShellExecute = true
        });
    }

    private static NexusConfig CreateDefaultConfig()
    {
        return new NexusConfig
        {
            ActiveToolId = "dsh",
            Tools =
            [
                new ToolProfileConfig
                {
                    Id = "dsh",
                    Name = "DeepSeek Harness",
                    RunnerType = "dsh",
                    WorkingDirectory = @"S:\Projects\dhs\deepseek-harness",
                    Command = "pnpm start:web --no-open",
                    Port = 3080,
                    DefaultUrl = "http://127.0.0.1:3080"
                },
                new ToolProfileConfig
                {
                    Id = "ollama",
                    Name = "Ollama Local LLM",
                    RunnerType = "ollama",
                    WorkingDirectory = @"C:\",
                    Command = "ollama serve",
                    Port = 11434,
                    DefaultUrl = "http://127.0.0.1:11434"
                },
                new ToolProfileConfig
                {
                    Id = "custom-npm",
                    Name = "Custom Web App (Пример)",
                    RunnerType = "npm",
                    WorkingDirectory = @"S:\Projects",
                    Command = "npm run dev",
                    Port = 5173,
                    DefaultUrl = "http://127.0.0.1:5173"
                }
            ]
        };
    }
}