using System;
using System.Threading.Tasks;
using DshTrayLauncher.Events;
using DshTrayLauncher.Runners;

namespace DshTrayLauncher.Runners;

public sealed class OllamaRunner : BaseProcessRunner
{
    public OllamaRunner(IEventBus bus) : base(bus)
    {
        Id = "ollama_core";
        DisplayName = "Ollama";
        WorkingDirectory = @"S:\Projects\dhs\deepseek-harness";
        Command = "ollama serve";
        Port = 11434;
    }

    public override string[] ProcessNamesToCleanup => new[] { "ollama", "node", "cmd" };
}
