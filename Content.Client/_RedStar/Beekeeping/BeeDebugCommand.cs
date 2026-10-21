using Robust.Shared.Console;

namespace Content.Client._RedStar.Beekeeping;

/// <summary>
/// Shows every hive's debug view on the map. The server only sends data to admins with the Debug flag.
/// </summary>
public sealed partial class BeeDebugCommand : LocalizedEntityCommands
{
    [Dependency] private BeeDebugSystem _beeDebug = default!;

    public override string Command => "beedebug";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _beeDebug.ShowAll = !_beeDebug.ShowAll;
        shell.WriteLine(Loc.GetString("cmd-beedebug-status", ("status", _beeDebug.ShowAll)));
    }
}
