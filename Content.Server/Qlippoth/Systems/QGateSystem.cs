using Content.Server.Chat.Systems;
using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Content.Shared.Interaction;
using Content.Shared.DoAfter;
using Content.Server.Popups;
using Content.Shared.CCVar;
using Content.Shared.Eye;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Configuration;
using Robust.Server.GameObjects;
using System.Linq;

namespace Content.Server.Qlippoth.Systems;

public sealed partial class QGateSystem : EntitySystem
{
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ContainmentDimensionSystem _containmentDim = default!;
    [Dependency] private QlippothMarketSystem _market = default!;
    [Dependency] private QGateDungeonSystem _dungeons = default!;
    [Dependency] private ContainmentPortalSystem _portals = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private VisibilitySystem _visibility = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private PopupSystem _popup = default!;

    private readonly Dictionary<EntityUid, QGateDungeonSystem.RiftDungeon> _dungeonsByGate = new();
    private readonly Dictionary<EntityUid, EntityUid> _returnPortalsByGate = new();
    private readonly Dictionary<EntityUid, List<EntityUid>> _breachEffectsByGate = new();
    private TimeSpan _nextAutomaticSpawn;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<QGateComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<QGateComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<QGateComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<QlippothBreachSealDeviceComponent, AfterInteractEvent>(OnBreachSealDeviceUsed);
        SubscribeLocalEvent<QlippothBreachSealDeviceComponent, ActivateInWorldEvent>(OnBreachSealDeviceActivated);
        SubscribeLocalEvent<QlippothBreachSealDeviceComponent, SealQlippothGateDoAfterEvent>(OnBreachSealCompleted);
        _nextAutomaticSpawn = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.QlippothGateSpawnInterval));
    }

    private void OnMapInit(EntityUid uid, QGateComponent component, MapInitEvent args)
    {
        var xform = Transform(uid);
        if (_containmentDim.IsContainmentDimension(xform.MapID))
        {
            // CRITICAL RULE: Q-Gates can NEVER exist inside Containment Dimension.
            QueueDel(uid);
            return;
        }

        component.SpawnedAt = _timing.CurTime;
        HideGate(uid);
        Dirty(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime >= _nextAutomaticSpawn)
        {
            TrySpawnAutomaticGate();
            _nextAutomaticSpawn = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.QlippothGateSpawnInterval));
        }

        var query = EntityQueryEnumerator<QGateComponent, TransformComponent>();
        var curTime = _timing.CurTime;

        while (query.MoveNext(out var uid, out var qgate, out var xform))
        {
            // Verify map restriction
            if (_containmentDim.IsContainmentDimension(xform.MapID))
            {
                QueueDel(uid);
                continue;
            }

            if (qgate.IsBreached)
                continue;

            // A cleared rift remains alive during the evacuation window so the
            // return portal can be used before the dungeon is removed.
            if (qgate.IsCleared)
            {
                if (curTime >= qgate.PortalCloseAt)
                    CloseRift(uid, qgate);

                continue;
            }

            var elapsed = curTime - qgate.SpawnedAt;
            var warningAt = qgate.ArrivalEta - qgate.WarningEta;

            // Warn before the gate opens, then announce the confirmed opening.
            if (!qgate.FiveMinWarningSent && elapsed >= warningAt)
            {
                qgate.FiveMinWarningSent = true;
                var warningMsg = Loc.GetString("qgate-announcement-warning",
                    ("location", qgate.LocationName),
                    ("phase", GetPhaseName(qgate.Phase)));

                _chatSystem.DispatchGlobalAnnouncement(warningMsg, "CentCom", playSound: true, colorOverride: Color.FromHex("#DAA520"));
            }

            if (!qgate.ArrivalAnnouncementSent && elapsed >= qgate.ArrivalEta)
            {
                qgate.ArrivalAnnouncementSent = true;
                var spawnMsg = Loc.GetString("qgate-announcement-spawn",
                    ("location", qgate.LocationName),
                    ("phase", GetPhaseName(qgate.Phase)));

                _chatSystem.DispatchGlobalAnnouncement(spawnMsg, "CentCom", playSound: true, colorOverride: Color.FromHex("#FF4500"));
            }

            if (!qgate.RiftOpened && elapsed >= qgate.ArrivalEta)
                OpenRift(uid, qgate);

            if (qgate.RiftOpened && !qgate.ObjectiveCompleted && curTime >= qgate.RiftOpenedAt + qgate.Duration)
                TriggerBreach(uid, qgate);

            if (qgate.ObjectiveCompleted && qgate.PortalClosing && curTime >= qgate.PortalCloseAt)
                CloseRift(uid, qgate);
        }

        UpdateRadarConsoles(curTime);
    }

    private void TrySpawnAutomaticGate()
    {
        var activeGates = 0;
        var gateQuery = EntityQueryEnumerator<QGateComponent>();
        while (gateQuery.MoveNext(out _, out var activeQgate))
        {
            if (!activeQgate.IsBreached && !activeQgate.IsCleared)
                activeGates++;
        }

        if (activeGates >= Math.Max(1, _cfg.GetCVar(CCVars.QlippothMaxActiveGates)))
            return;

        var stationCandidates = new List<MapCoordinates>();
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapID == MapId.Nullspace || _containmentDim.IsContainmentDimension(xform.MapID))
                continue;

            stationCandidates.Add(_transform.GetMapCoordinates(xform.Owner));
        }

        var deepSpaceCandidates = new List<MapCoordinates>();
        foreach (var mapId in _mapManager.GetAllMapIds())
        {
            if (_containmentDim.IsContainmentDimension(mapId))
                continue;

            foreach (var grid in _mapManager.GetAllGrids(mapId))
                deepSpaceCandidates.Add(_transform.GetMapCoordinates(grid.Owner));
        }

        if (stationCandidates.Count == 0 && deepSpaceCandidates.Count == 0)
            return;

        var phase = RollAutomaticPhase();
        var preferStation = stationCandidates.Count > 0 && _random.Prob(0.75f);
        var candidates = preferStation || deepSpaceCandidates.Count == 0
            ? stationCandidates
            : deepSpaceCandidates;
        var gate = Spawn(GetGatePrototype(phase), _random.Pick(candidates));
        if (TryComp<QGateComponent>(gate, out var qgate))
        {
            qgate.LocationName = preferStation ? "Station Grid" : "Deep Space Sector";
            Dirty(gate, qgate);
        }
    }

    private QGatePhase RollAutomaticPhase()
    {
        var weights = new[]
        {
            Math.Max(0, _cfg.GetCVar(CCVars.QlippothPhase1Weight)),
            Math.Max(0, _cfg.GetCVar(CCVars.QlippothPhase2Weight)),
            Math.Max(0, _cfg.GetCVar(CCVars.QlippothPhase3Weight)),
            Math.Max(0, _cfg.GetCVar(CCVars.QlippothPhase4Weight)),
            Math.Max(0, _cfg.GetCVar(CCVars.QlippothPhase5Weight))
        };
        var total = weights.Sum();
        if (total <= 0)
            return QGatePhase.Phase1Rift;

        var roll = _random.Next(1, total + 1);
        for (var index = 0; index < weights.Length; index++)
        {
            roll -= weights[index];
            if (roll <= 0)
                return (QGatePhase) (index + 1);
        }

        return QGatePhase.Phase1Rift;
    }

    private void HideGate(EntityUid uid)
    {
        var visibility = EnsureComp<VisibilityComponent>(uid);
        _visibility.RemoveLayer((uid, visibility), (int) VisibilityFlags.Normal, false);
        _visibility.RefreshVisibility(uid, visibility);
    }

    private void ShowGate(EntityUid uid)
    {
        var visibility = EnsureComp<VisibilityComponent>(uid);
        _visibility.AddLayer((uid, visibility), (int) VisibilityFlags.Normal, false);
        _visibility.RefreshVisibility(uid, visibility);
    }

    private static string GetGatePrototype(QGatePhase phase)
    {
        return phase switch
        {
            QGatePhase.Phase2Verge => "QGatePhase2Verge",
            QGatePhase.Phase3Eclipse => "QGatePhase3Eclipse",
            QGatePhase.Phase4Abyss => "QGatePhase4Abyss",
            QGatePhase.Phase5Horizon => "QGatePhase5Horizon",
            _ => "QGatePhase1Rift"
        };
    }

    private void UpdateRadarConsoles(TimeSpan curTime)
    {
        var gates = new List<QGateComponent>();
        var gateQuery = EntityQueryEnumerator<QGateComponent>();
        while (gateQuery.MoveNext(out _, out var gate))
        {
            if (!gate.IsBreached && !gate.IsCleared)
                gates.Add(gate);
        }

        var radarQuery = EntityQueryEnumerator<QGateRadarComponent>();
        while (radarQuery.MoveNext(out var uid, out var radar))
        {
            if (gates.Count == 0)
            {
                if (radar.LastTrackedProbability == 0f && radar.EtaSeconds == 0)
                    continue;

                radar.LastTrackedProbability = 0f;
                radar.PredictedPhase = QGatePhase.Phase1Rift;
                radar.EtaSeconds = 0;
                Dirty(uid, radar);
                continue;
            }

            var gate = gates[0];
            var eta = gate.RiftOpened
                ? 0
                : Math.Max(0, (int)(gate.SpawnedAt + gate.ArrivalEta - curTime).TotalSeconds);

            if (radar.LastTrackedProbability == 0.75f &&
                radar.PredictedPhase == gate.Phase &&
                radar.EtaSeconds == eta)
                continue;

            radar.LastTrackedProbability = 0.75f;
            radar.PredictedPhase = gate.Phase;
            radar.EtaSeconds = eta;
            Dirty(uid, radar);
        }
    }

    public string GetTrackerDetail(TimeSpan curTime)
    {
        var lines = new List<string>();
        var query = EntityQueryEnumerator<QGateComponent, TransformComponent>();
        while (query.MoveNext(out _, out var gate, out var xform))
        {
            if (gate.IsBreached || gate.IsCleared)
                continue;

            var eta = gate.RiftOpened
                ? "OPEN"
                : $"{Math.Max(0, (int)(gate.SpawnedAt + gate.ArrivalEta - curTime).TotalSeconds)}s";
            lines.Add($"- {gate.LocationName} | {GetPhaseName(gate.Phase)} | ETA: {eta} | {xform.Coordinates.Position.X:0.0}, {xform.Coordinates.Position.Y:0.0}");
        }

        return lines.Count == 0 ? "No active Q-Gates detected." : string.Join("\n", lines);
    }

    private void OpenRift(EntityUid uid, QGateComponent qgate)
    {
        if (qgate.RiftOpened)
            return;

        qgate.RiftOpened = true;
        qgate.RiftOpenedAt = _timing.CurTime;
        ShowGate(uid);
        _dungeonsByGate[uid] = _dungeons.CreateRiftDungeon(qgate.Phase, uid);
        Dirty(uid, qgate);
    }

    public void ReportObjectiveCompleted(EntityUid gateUid)
    {
        if (!TryComp<QGateComponent>(gateUid, out var qgate) || qgate.IsBreached || qgate.ObjectiveCompleted)
            return;

        qgate.CompletedObjectives++;
        if (qgate.CompletedObjectives >= qgate.RequiredObjectives)
            TriggerCleared(gateUid, qgate);
        else
            Dirty(gateUid, qgate);
    }

    private void OnAfterInteract(EntityUid uid, QGateComponent qgate, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !qgate.RiftOpened || qgate.IsBreached || qgate.PortalClosing)
            return;

        if (!_dungeonsByGate.TryGetValue(uid, out var dungeon))
            return;

        _transform.SetMapCoordinates(args.User, dungeon.Entry);
        args.Handled = true;
    }

    private void OnActivateInWorld(EntityUid uid, QGateComponent qgate, ActivateInWorldEvent args)
    {
        if (args.Handled || !qgate.RiftOpened || qgate.IsBreached || qgate.PortalClosing)
            return;

        if (!_dungeonsByGate.TryGetValue(uid, out var dungeon))
            return;

        _transform.SetMapCoordinates(args.User, dungeon.Entry);
        args.Handled = true;
    }

    private void OnBreachSealDeviceUsed(EntityUid uid, QlippothBreachSealDeviceComponent component,
        AfterInteractEvent args)
    {
        if (args.Handled || args.Target == null || !args.CanReach)
            return;

        args.Handled = TryStartBreachSeal(uid, component, args.User, args.Target.Value);
    }

    private void OnBreachSealDeviceActivated(EntityUid uid, QlippothBreachSealDeviceComponent component,
        ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartBreachSeal(uid, component, args.User, args.Target);
    }

    private bool TryStartBreachSeal(EntityUid device, QlippothBreachSealDeviceComponent component,
        EntityUid user, EntityUid target)
    {
        if (!TryComp<QGateComponent>(target, out var qgate) || !qgate.IsBreached)
        {
            _popup.PopupEntity(Loc.GetString("qgate-seal-invalid-target"), user, user);
            return false;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, component.SealDuration,
            new SealQlippothGateDoAfterEvent(), device, target: target, used: device)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            Broadcast = true
        };

        return _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnBreachSealCompleted(EntityUid device, QlippothBreachSealDeviceComponent component,
        SealQlippothGateDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        SealBreachedGate(target);
        args.Handled = true;
    }

    private bool SealBreachedGate(EntityUid target)
    {
        if (!TryComp<QGateComponent>(target, out var qgate) || !qgate.IsBreached)
            return false;

        if (_dungeonsByGate.TryGetValue(target, out var dungeon))
        {
            EvacuateDungeon(dungeon.MapId, _transform.GetMapCoordinates(target));
            _dungeons.CloseRift(dungeon);
            _dungeonsByGate.Remove(target);
        }

        if (_returnPortalsByGate.Remove(target, out var returnPortal))
            QueueDel(returnPortal);

        if (_breachEffectsByGate.Remove(target, out var breachEffects))
        {
            foreach (var effect in breachEffects)
                QueueDel(effect);
        }

        QueueDel(target);
        _chatSystem.DispatchGlobalAnnouncement(
            Loc.GetString("qgate-announcement-sealed", ("location", qgate.LocationName)),
            "CentCom Science", playSound: true, colorOverride: Color.FromHex("#20B2AA"));
        return true;
    }

    public void TriggerBreach(EntityUid uid, QGateComponent? qgate = null)
    {
        if (!Resolve(uid, ref qgate) || qgate.IsBreached || qgate.IsCleared)
            return;

        qgate.IsBreached = true;
        if (_dungeonsByGate.TryGetValue(uid, out var dungeon))
        {
            EvacuateDungeon(dungeon.MapId, _transform.GetMapCoordinates(uid));
            _dungeons.CloseRift(dungeon);
            _dungeonsByGate.Remove(uid);
        }

        Spawn(GetQlippothPrototype(qgate.Phase), Transform(uid).Coordinates);
        var breachEffects = new List<EntityUid>
        {
            Spawn("EffectSparks", Transform(uid).Coordinates),
            Spawn("EffectVoidBlink", Transform(uid).Coordinates)
        };
        _breachEffectsByGate[uid] = breachEffects;
        Dirty(uid, qgate);
        var breachMsg = Loc.GetString("qgate-announcement-breach", ("location", qgate.LocationName));
        _chatSystem.DispatchGlobalAnnouncement(breachMsg, "CentCom Emergency Alert", playSound: true, colorOverride: Color.FromHex("#DC143C"));
    }

    public void TriggerCleared(EntityUid uid, QGateComponent? qgate = null)
    {
        if (!Resolve(uid, ref qgate) || qgate.IsCleared || qgate.IsBreached)
            return;

        qgate.IsCleared = true;
        qgate.ObjectiveCompleted = true;
        qgate.PortalClosing = true;
        qgate.PortalCloseAt = _timing.CurTime + TimeSpan.FromMinutes(3);

        if (_dungeonsByGate.TryGetValue(uid, out var dungeon))
        {
            var returnPortal = Spawn("ContainmentDimensionExitPortal", dungeon.Entry);
            _portals.RegisterReturnPortal(returnPortal, _transform.GetMapCoordinates(uid));
            _returnPortalsByGate[uid] = returnPortal;
        }

        Dirty(uid, qgate);
        var clearMsg = Loc.GetString("qgate-announcement-cleared", ("location", qgate.LocationName));
        _chatSystem.DispatchGlobalAnnouncement(clearMsg, "CentCom Notice", playSound: true, colorOverride: Color.FromHex("#32CD32"));
    }

    private void CloseRift(EntityUid uid, QGateComponent qgate)
    {
        if (_dungeonsByGate.TryGetValue(uid, out var dungeon))
        {
            var exit = _transform.GetMapCoordinates(uid);
            EvacuateDungeon(dungeon.MapId, exit);
            _dungeons.CloseRift(dungeon);
            _dungeonsByGate.Remove(uid);
        }

        if (_returnPortalsByGate.Remove(uid, out var returnPortal))
            QueueDel(returnPortal);

        qgate.PortalClosing = false;
        _market.AddSecuredQlippothToMarket(GetQlippothPrototype(qgate.Phase), qgate.Phase);
        QueueDel(uid);
    }

    private void EvacuateDungeon(MapId dungeonMap, MapCoordinates exit)
    {
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out var xform))
        {
            if (xform.MapID == dungeonMap)
                _transform.SetMapCoordinates(player, exit);
        }
    }

    public string GetPhaseName(QGatePhase phase)
    {
        return phase switch
        {
            QGatePhase.Phase1Rift => Loc.GetString("qgate-phase-rift"),
            QGatePhase.Phase2Verge => Loc.GetString("qgate-phase-verge"),
            QGatePhase.Phase3Eclipse => Loc.GetString("qgate-phase-eclipse"),
            QGatePhase.Phase4Abyss => Loc.GetString("qgate-phase-abyss"),
            QGatePhase.Phase5Horizon => Loc.GetString("qgate-phase-horizon"),
            _ => "Unknown Phase"
        };
    }

    private static EntProtoId GetQlippothPrototype(QGatePhase phase)
    {
        return phase switch
        {
            QGatePhase.Phase1Rift => "MobQlippothPhase1",
            QGatePhase.Phase2Verge => "MobQlippothPhase2",
            QGatePhase.Phase3Eclipse => "MobQlippothPhase3",
            QGatePhase.Phase4Abyss => "MobQlippothPhase4",
            QGatePhase.Phase5Horizon => "MobQlippothPhase5",
            _ => "MobQlippothPhase1"
        };
    }
}
