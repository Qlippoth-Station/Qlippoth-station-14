using System.Numerics;
using Content.Shared.Qlippoth.Components;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared.Destructible;
using Robust.Shared.Map;

namespace Content.Server.Qlippoth.Systems;

/// <summary>
/// Handles the physical transport of Qlippoth capsules from Cargo to Containment Dimension chambers.
/// When a capsule is docked into a matching chamber, the Qlippoth entity is spawned inside.
/// </summary>
public sealed partial class QlippothTransportSystem : EntitySystem
{
    [Dependency] private ContainmentDimensionSystem _containmentDim = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private QlippothActionInitiationSystem _initiation = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IMapManager _mapManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<QlippothCapsuleComponent, ComponentStartup>(OnCapsuleStartup);
        SubscribeLocalEvent<QlippothCapsuleComponent, DestructionEventArgs>(OnCapsuleDestroyed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var capsules = EntityQueryEnumerator<QlippothCapsuleComponent, TransformComponent>();
        while (capsules.MoveNext(out var capsuleUid, out var capsule, out var capsuleXform))
        {
            if (!_containmentDim.IsContainmentDimension(capsuleXform.MapID))
                continue;

            var docked = false;
            var chambers = EntityQueryEnumerator<ContainmentChamberComponent, TransformComponent>();
            while (chambers.MoveNext(out var chamberUid, out var chamber, out var chamberXform))
            {
                if (chamberXform.MapID != capsuleXform.MapID || chamber.IsOccupied ||
                    chamber.IsBreached || !chamber.IsBuilt)
                    continue;

                if (Vector2.Distance(capsuleXform.Coordinates.Position, chamberXform.Coordinates.Position) > 1.5f)
                    continue;

                if (capsule.TargetChamberId.Length > 0 && capsule.TargetChamberId != chamber.ChamberId)
                    continue;

                if (DockCapsuleToChamber(capsuleUid, chamberUid, capsule, chamber))
                {
                    _chatSystem.DispatchGlobalAnnouncement(
                        Loc.GetString("qgate-announcement-containment"),
                        "CentCom Containment", playSound: true, colorOverride: Color.FromHex("#32CD32"));
                    docked = true;
                }

                break;
            }

            if (docked)
                continue;

            if (capsule.FailureAnnounced || capsule.TargetChamberId.Length == 0)
                continue;

            if (!_containmentDim.IsChamberAvailable(capsule.TargetChamberId))
            {
                capsule.FailureAnnounced = true;
                Dirty(capsuleUid, capsule);
                _popup.PopupEntity(
                    Loc.GetString("containment-capsule-target-unavailable", ("chamber", capsule.TargetChamberId)),
                    capsuleUid);
                continue;
            }

            EntityUid? nearbyChamber = null;
            var nearestDistance = float.MaxValue;
            var invalidChambers = EntityQueryEnumerator<ContainmentChamberComponent, TransformComponent>();
            while (invalidChambers.MoveNext(out var chamberUid, out _, out var chamberXform))
            {
                if (chamberXform.MapID != capsuleXform.MapID)
                    continue;

                var distance = Vector2.Distance(capsuleXform.Coordinates.Position, chamberXform.Coordinates.Position);
                if (distance <= 1.5f && distance < nearestDistance)
                {
                    nearbyChamber = chamberUid;
                    nearestDistance = distance;
                }
            }

            if (nearbyChamber is not { } nearby)
                continue;

            capsule.FailureAnnounced = true;
            Dirty(capsuleUid, capsule);
            var nearbyId = Comp<ContainmentChamberComponent>(nearby).ChamberId;
            var warning = nearbyId == capsule.TargetChamberId
                ? Loc.GetString("containment-capsule-target-unavailable", ("chamber", capsule.TargetChamberId))
                : Loc.GetString("containment-capsule-wrong-chamber",
                    ("target", capsule.TargetChamberId), ("nearby", nearbyId));
            _popup.PopupEntity(warning, capsuleUid);
        }
    }

    private void OnCapsuleStartup(EntityUid uid, QlippothCapsuleComponent component, ComponentStartup args)
    {
        // Capsule spawned - awaiting Security to physically move it to containment
    }

    private void OnCapsuleDestroyed(EntityUid uid, QlippothCapsuleComponent component, DestructionEventArgs args)
    {
        ReleaseCapsuleContents(uid, component, announce: true);
    }

    /// <summary>
    /// Called when Security docks a capsule into a containment chamber.
    /// Spawns the contained Qlippoth entity inside the chamber.
    /// </summary>
    public bool DockCapsuleToChamber(EntityUid capsuleUid, EntityUid chamberUid,
        QlippothCapsuleComponent? capsule = null,
        ContainmentChamberComponent? chamber = null)
    {
        if (!Resolve(capsuleUid, ref capsule) || !Resolve(chamberUid, ref chamber))
            return false;

        if (capsule.ContainedQlippothProto == null ||
            (capsule.TargetChamberId.Length > 0 && capsule.TargetChamberId != chamber.ChamberId))
            return false;

        if (chamber.IsOccupied)
            return false;

        if (!chamber.IsBuilt)
            return false;

        _containmentDim.EnsureContainmentDimensionCreated();
        // Verify the chamber is inside Containment Dimension
        var chamberXform = Transform(chamberUid);
        if (!_containmentDim.IsContainmentDimension(chamberXform.MapID))
            return false;

        // Spawn first, then claim the chamber so a failed claim leaves the capsule intact.
        var qlippothUid = Spawn(capsule.ContainedQlippothProto, chamberXform.Coordinates);
        if (!_containmentDim.TryAssignQlippothToChamber(chamberUid, qlippothUid))
        {
            QueueDel(qlippothUid);
            return false;
        }

        _initiation.Dispatch<OnArrivedInitiation>(qlippothUid, new QlippothArrivalEventArgs(chamberUid, QlippothArrivalKind.ContainmentDock));
        _initiation.Dispatch<OnContainedInitiation>(qlippothUid, new QlippothArrivalEventArgs(chamberUid, QlippothArrivalKind.ContainmentDock));

        capsule.ContainedQlippothProto = null;
        Dirty(capsuleUid, capsule);
        QueueDel(capsuleUid);

        return true;
    }

    /// <summary>
    /// Handles a capsule breach event (capsule broke during transport).
    /// Spawns the Qlippoth at the capsule's current location on the station.
    /// </summary>
    public void BreachCapsule(EntityUid capsuleUid, QlippothCapsuleComponent? capsule = null)
    {
        if (!Resolve(capsuleUid, ref capsule))
            return;

        ReleaseCapsuleContents(capsuleUid, capsule, announce: true);
        QueueDel(capsuleUid);
    }

    private void ReleaseCapsuleContents(EntityUid capsuleUid, QlippothCapsuleComponent capsule, bool announce)
    {
        if (capsule.ContainedQlippothProto is not { } proto || !Exists(capsuleUid))
            return;

        var capsuleTransform = Transform(capsuleUid);
        var inContainmentDimension = _containmentDim.IsContainmentDimension(capsuleTransform.MapID);
        var spawnCoordinates = capsuleTransform.Coordinates;
        var location = inContainmentDimension
            ? "Containment Dimension"
            : "Station Grid";
        if (inContainmentDimension && !_mapManager.MapExists(capsuleTransform.MapID))
        {
            if (capsule.FallbackLocation is not { } fallback ||
                !TryComp<TransformComponent>(fallback, out var fallbackTransform))
            {
                _chatSystem.DispatchGlobalAnnouncement(
                    Loc.GetString("containment-capsule-recovery-failed"),
                    "CentCom Emergency Alert", playSound: true, colorOverride: Color.FromHex("#DC143C"));
                return;
            }

            spawnCoordinates = fallbackTransform.Coordinates.Offset(new Vector2(0f, -1f));
            location = "Cargo fallback point";
        }

        capsule.ContainedQlippothProto = null;
        Dirty(capsuleUid, capsule);
        Spawn(proto, spawnCoordinates);
        if (!announce)
            return;

        _chatSystem.DispatchGlobalAnnouncement(
            Loc.GetString("qgate-announcement-capsule-breach", ("location", location)),
            "CentCom Emergency Alert", playSound: true, colorOverride: Color.FromHex("#DC143C"));
    }
}
