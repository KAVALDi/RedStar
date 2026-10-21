using Content.Client._RedStar.Beekeeping.UI;
using Content.Client.Administration.Managers;
using Content.Shared._RedStar.Beekeeping;
using Content.Shared.Administration;
using Content.Shared.Verbs;
using Robust.Client.Graphics;

namespace Content.Client._RedStar.Beekeeping;

/// <summary>
/// Admin bee debug view on the client. The "beedebug" command shows every hive on the map; the hive's debug
/// verb opens a window about that one hive and shows it on the map. While either is open the server sends
/// <see cref="BeeDebugStateEvent"/> once a second.
/// </summary>
public sealed partial class BeeDebugSystem : EntitySystem
{
    [Dependency] private IClientAdminManager _adminManager = default!;
    [Dependency] private IOverlayManager _overlay = default!;

    private readonly Dictionary<NetEntity, BeeDebugWindow> _windows = new();
    private bool _showAll;
    private bool _requested;

    /// <summary>
    /// The latest data, by hive.
    /// </summary>
    public readonly Dictionary<NetEntity, BeeHiveDebugData> Hives = new();

    public bool ShowAll
    {
        get => _showAll;
        set
        {
            _showAll = value;
            UpdateRequest();
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var window in _windows.Values)
        {
            window.Close();
        }

        _windows.Clear();
        _overlay.RemoveOverlay<BeeDebugOverlay>();
    }

    /// <summary>
    /// Whether a hive is drawn on the map: all of them with the command, otherwise only those with a window.
    /// </summary>
    public bool IsShown(NetEntity hive)
    {
        return _showAll || _windows.ContainsKey(hive);
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<BeehiveComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!_adminManager.HasFlag(AdminFlags.Debug))
            return;

        var hive = GetNetEntity(ent);
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("beekeeping-debug-window"),
            Category = VerbCategory.Debug,
            // First among the hive's debug actions.
            Priority = 1,
            ClientExclusive = true,
            Act = () => OpenWindow(hive),
        });
    }

    private void OpenWindow(NetEntity hive)
    {
        if (_windows.TryGetValue(hive, out var existing))
        {
            existing.MoveToFront();
            return;
        }

        var window = new BeeDebugWindow();
        window.OnClose += () =>
        {
            _windows.Remove(hive);
            UpdateRequest();
        };

        _windows[hive] = window;
        window.SetHive(hive);
        if (Hives.TryGetValue(hive, out var data))
            window.UpdateData(data);

        window.OpenCentered();
        UpdateRequest();
    }

    [SubscribeNetworkEvent]
    private void OnDebugState(BeeDebugStateEvent ev)
    {
        Hives.Clear();
        foreach (var hive in ev.Hives)
        {
            Hives[hive.Hive] = hive;
        }

        foreach (var (hive, window) in _windows)
        {
            if (Hives.TryGetValue(hive, out var data))
                window.UpdateData(data);
            else
                window.ShowGone();
        }
    }

    /// <summary>
    /// Asks the server for data while anything is open, and stops asking when everything is closed.
    /// </summary>
    private void UpdateRequest()
    {
        var wanted = _showAll || _windows.Count > 0;
        if (wanted)
        {
            if (!_overlay.HasOverlay<BeeDebugOverlay>())
                _overlay.AddOverlay(new BeeDebugOverlay(this));
        }
        else
        {
            _overlay.RemoveOverlay<BeeDebugOverlay>();
            Hives.Clear();
        }

        if (wanted == _requested)
            return;

        _requested = wanted;
        RaiseNetworkEvent(new RequestBeeDebugEvent(wanted));
    }
}
