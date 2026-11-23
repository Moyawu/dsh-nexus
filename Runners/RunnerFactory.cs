using System;
using DshTrayLauncher.Config;
using DshTrayLauncher.Events;

namespace DshTrayLauncher.Runners;

public sealed class RunnerFactory
{
    public static IProcessRunner Create(ToolProfileConfig profile, IEventBus eventBus)
    {
        return profile.RunnerType switch
        {
            "dsh" => new DshRunner(eventBus)
            {
                Id = profile.Id,
                DisplayName = profile.Name,
                WorkingDirectory = profile.WorkingDirectory,
                Command = profile.Command,
                Port = profile.Port,
                ServerUrl = profile.DefaultUrl
            },
            "ollama" => new OllamaRunner(eventBus)
            {
                Id = profile.Id,
                DisplayName = profile.Name,
                WorkingDirectory = profile.WorkingDirectory,
                Command = profile.Command,
                Port = profile.Port,
                ServerUrl = profile.DefaultUrl
            },
            "npm" => new GenericNpmRunner(eventBus, profile.Id, profile.Name, profile.WorkingDirectory, profile.Command, profile.Port)
            {
                ServerUrl = profile.DefaultUrl
            },
            _ => throw new ArgumentException($"Unknown runner type: {profile.RunnerType}")
        };
    }
}