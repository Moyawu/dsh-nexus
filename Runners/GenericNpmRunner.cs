using System;
using System.Threading.Tasks;
using DshTrayLauncher.Events;
using DshTrayLauncher.Runners;

namespace DshTrayLauncher.Runners;

public sealed class GenericNpmRunner : BaseProcessRunner
{
    public GenericNpmRunner(IEventBus bus, string id, string displayName, string workingDir, string cmd, int port) 
        : base(bus)
    {
        Id = id;
        DisplayName = displayName;
        WorkingDirectory = workingDir;
        Command = cmd;
        Port = port;
    }
}
