using System;
using System.Threading.Tasks;
using DshTrayLauncher.Events;
using DshTrayLauncher.Runners;

namespace DshTrayLauncher.Runners;

public sealed class DshRunner : BaseProcessRunner
{
    public DshRunner(IEventBus bus) : base(bus)
    {
        Id = "dsh_core";
        DisplayName = "DeepSeek Harness";
        WorkingDirectory = @"S:\Projects\dhs\deepseek-harness";
        Command = "pnpm start:web --no-open";
        Port = 3080;
    }
}
