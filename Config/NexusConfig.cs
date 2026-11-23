namespace DshTrayLauncher.Config;

public sealed class NexusConfig
{
    public string ActiveToolId { get; set; } = "dsh";
    public string Theme { get; set; } = "Dark";
    public List<ToolProfileConfig> Tools { get; set; } = [];
}

public sealed class ToolProfileConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RunnerType { get; set; } = "npm";
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public int Port { get; set; } = 3080;
    public string DefaultUrl { get; set; } = "http://127.0.0.1:3080";
}