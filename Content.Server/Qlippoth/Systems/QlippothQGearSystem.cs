using System.Linq;
using System.Numerics;
using Content.Server.Qlippoth;
using Content.Server.Qlippoth.Components;
using Content.Server.Popups;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Qlippoth.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.Qlippoth.Systems;

public sealed class QlippothQGearSystem : EntitySystem
{
    private const float GateScanRange = 20f;
    private static readonly TimeSpan AnchorDuration = TimeSpan.FromSeconds(30);

    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CorruptionSystem _corruption = default!;
    [Dependency] private ContainmentDimensionSystem _containment = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<QlippothQGearComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<QlippothQGearComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(EntityUid uid, QlippothQGearComponent gear, UseInHandEvent args)
    {
        if (args.Handled || gear.GearType != QlippothQGearType.GateCompass)
            return;

        args.Handled = true;
        if (_timing.CurTime < gear.NextUseAt)
        {
            _popup.PopupEntity(Loc.GetString("qlippoth-qgear-cooldown"), uid, args.User);
            return;
        }

        var origin = _transform.GetMapCoordinates(args.User);
        EntityUid? nearestGate = null;
        MapCoordinates? nearestCoordinates = null;
        var nearestDistance = GateScanRange;
        var gates = EntityQueryEnumerator<QGateComponent, TransformComponent>();
        while (gates.MoveNext(out var gateUid, out _, out _))
        {
            var coordinates = _transform.GetMapCoordinates(gateUid);
            if (coordinates.MapId != origin.MapId)
                continue;

            var distance = Vector2.Distance(origin.Position, coordinates.Position);
            if (distance > nearestDistance)
                continue;

            nearestGate = gateUid;
            nearestCoordinates = coordinates;
            nearestDistance = distance;
        }

        if (nearestGate is not { } target || nearestCoordinates is not { } targetCoordinates ||
            !TryComp<QGateComponent>(target, out var gate))
        {
            _popup.PopupEntity(Loc.GetString("qlippoth-qgear-compass-out-of-range"), uid, args.User);
            return;
        }

        ReportGate(target, args.User, gate, origin.Position, targetCoordinates.Position);
        gear.NextUseAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1f, gear.CooldownSeconds));
    }

    private void OnAfterInteract(EntityUid uid, QlippothQGearComponent gear, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (_timing.CurTime < gear.NextUseAt)
        {
            _popup.PopupEntity(Loc.GetString("qlippoth-qgear-cooldown"), uid, args.User);
            args.Handled = true;
            return;
        }

        var used = gear.GearType switch
        {
            QlippothQGearType.ResonanceProbe => UseResonanceProbe(uid, target, args.User),
            QlippothQGearType.InversionTongs => UseInversionTongs(uid, target, args.User),
            QlippothQGearType.BoundaryAnchor => UseBoundaryAnchor(uid, target, args.User),
            QlippothQGearType.GateCompass => UseGateCompass(uid, target, args.User),
            QlippothQGearType.HorizonLens => UseHorizonLens(uid, target, args.User),
            _ => false,
        };

        if (used)
            gear.NextUseAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1f, gear.CooldownSeconds));

        args.Handled = true;
    }

    private bool UseResonanceProbe(EntityUid tool, EntityUid target, EntityUid user)
    {
        if (!TryComp<QlippothComponent>(target, out var qlippoth))
        {
            _popup.PopupEntity(Loc.GetString("qlippoth-qgear-probe-no-specimen"), tool, user);
            return false;
        }

        var phases = qlippoth.GatePhases.Count == 0
            ? Loc.GetString("qlippoth-qgear-phase-unknown")
            : string.Join(", ", qlippoth.GatePhases.Select(phase => (int) phase));
        _popup.PopupEntity(Loc.GetString("qlippoth-qgear-probe-result",
            ("specimen", Name(target)),
            ("phase", phases)), target, user);
        return true;
    }

    private bool UseInversionTongs(EntityUid tool, EntityUid target, EntityUid user)
    {
        if (!_corruption.RemoveCorruption(target))
        {
            _popup.PopupEntity(Loc.GetString("qlippoth-qgear-tongs-no-corruption"), tool, user);
            return false;
        }

        _popup.PopupEntity(Loc.GetString("qlippoth-qgear-tongs-treated"), target, user);
        return true;
    }

    private bool UseBoundaryAnchor(EntityUid tool, EntityUid target, EntityUid user)
    {
        if (!TryComp<ContainmentChamberComponent>(target, out var chamber) ||
            !TryComp<TransformComponent>(target, out var transform) ||
            !_containment.IsContainmentDimension(transform.MapID) ||
            !chamber.IsBuilt || !chamber.IsBreached)
        {
            _popup.PopupEntity(Loc.GetString("qlippoth-qgear-anchor-invalid"), tool, user);
            return false;
        }

        if (_containment.IsBoundaryAnchorActive(target))
        {
            _popup.PopupEntity(Loc.GetString("qlippoth-qgear-anchor-already-active"), target, user);
            return false;
        }

        chamber.BoundaryAnchorExpiresAt = _timing.CurTime + AnchorDuration;
        Dirty(target, chamber);
        _popup.PopupEntity(Loc.GetString("qlippoth-qgear-anchor-active",
            ("duration", (int) AnchorDuration.TotalSeconds)), target, user);
        return true;
    }

    private bool UseGateCompass(EntityUid tool, EntityUid target, EntityUid user)
    {
        if (!TryComp<QGateComponent>(target, out var gate))
        {
            _popup.PopupEntity(Loc.GetString("qlippoth-qgear-compass-no-gate"), tool, user);
            return false;
        }

        var userCoordinates = _transform.GetMapCoordinates(user);
        var gateCoordinates = _transform.GetMapCoordinates(target);
        if (userCoordinates.MapId != gateCoordinates.MapId ||
            Vector2.Distance(userCoordinates.Position, gateCoordinates.Position) > GateScanRange)
        {
            _popup.PopupEntity(Loc.GetString("qlippoth-qgear-compass-out-of-range"), tool, user);
            return false;
        }

        ReportGate(target, user, gate, userCoordinates.Position, gateCoordinates.Position);
        return true;
    }

    private void ReportGate(
        EntityUid target,
        EntityUid user,
        QGateComponent gate,
        Vector2 userPosition,
        Vector2 gatePosition)
    {
        var status = gate.IsBreached
            ? Loc.GetString("qlippoth-qgear-gate-breached")
            : gate.IsCleared
                ? Loc.GetString("qlippoth-qgear-gate-cleared")
                : gate.RiftOpened
                    ? Loc.GetString("qlippoth-qgear-gate-open")
                    : Loc.GetString("qlippoth-qgear-gate-charging");
        var direction = gatePosition - userPosition;
        var angle = MathF.Atan2(direction.Y, direction.X) * (180f / MathF.PI);
        var directionIndex = ((int) MathF.Round(angle / 45f) + 8) % 8;
        var directionName = Loc.GetString(directionIndex switch
        {
            0 => "qlippoth-qgear-direction-east",
            1 => "qlippoth-qgear-direction-northeast",
            2 => "qlippoth-qgear-direction-north",
            3 => "qlippoth-qgear-direction-northwest",
            4 => "qlippoth-qgear-direction-west",
            5 => "qlippoth-qgear-direction-southwest",
            6 => "qlippoth-qgear-direction-south",
            _ => "qlippoth-qgear-direction-southeast",
        });
        _popup.PopupEntity(Loc.GetString("qlippoth-qgear-compass-result",
            ("phase", (int) gate.Phase),
            ("status", status),
            ("direction", directionName),
            ("distance", (int) Vector2.Distance(userPosition, gatePosition))),
            target, user);
    }

    private bool UseHorizonLens(EntityUid tool, EntityUid target, EntityUid user)
    {
        if (!TryComp<QlippothCorruptionComponent>(target, out var corruption) || corruption.IsRemoving)
        {
            _popup.PopupEntity(Loc.GetString("qlippoth-qgear-lens-no-corruption"), tool, user);
            return false;
        }

        var source = corruption.SourceQlippoth is { } sourceUid && Exists(sourceUid)
            ? Name(sourceUid)
            : Loc.GetString("qlippoth-qgear-source-unknown");
        _popup.PopupEntity(Loc.GetString("qlippoth-qgear-lens-result",
            ("severity", corruption.Severity),
            ("duration", (int) MathF.Ceiling(corruption.RemainingSeconds)),
            ("source", source)), target, user);
        return true;
    }
}
