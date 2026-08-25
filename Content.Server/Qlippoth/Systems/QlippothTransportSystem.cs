using System.Numerics;
using Content.Shared.Qlippoth.Components;
using Content.Server.Chat.Systems;
using Content.Shared.Destructible;
using Robust.Shared.Map;

namespace Content.Server.Qlippoth.Systems;

/// <summary>
/// Handles the physical transport of Qlippoth capsules from Cargo to Containment Dimension chambers.
/// When a capsule is docked into a matching chamber, the Qlippoth entity is spawned inside.
/// </summary>
public sealed class QlippothTransportSystem : EntitySystem
{
    [Dependency] private readonly ContainmentDimensionSystem _containmentDim = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;

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

            var chambers = EntityQueryEnumerator<ContainmentChamberComponent, TransformComponent>();
            while (chambers.MoveNext(out var chamberUid, out var chamber, out var chamberXform))
            {
                if (chamberXform.MapID != capsuleXform.MapID || chamber.IsOccupied || !chamber.IsBuilt)
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
                }

                break;
            }

            if (capsule.FailureAnnounced || capsule.TargetChamberId.Length == 0)
                continue;

            var nearbyChamber = false;
            var invalidChambers = EntityQueryEnumerator<ContainmentChamberComponent, TransformComponent>();
            while (invalidChambers.MoveNext(out _, out var chamber, out var chamberXform))
            {
                if (chamberXform.MapID == capsuleXform.MapID &&
                    Vector2.Distance(capsuleXform.Coordinates.Position, chamberXform.Coordinates.Position) <= 1.5f)
                {
                    nearbyChamber = true;
                    break;
                }
            }

            if (nearbyChamber)
            {
                capsule.FailureAnnounced = true;
                Dirty(capsuleUid, capsule);
                _chatSystem.DispatchGlobalAnnouncement(
                    Loc.GetString("containment-capsule-transfer-failed", ("chamber", capsule.TargetChamberId)),
                    "CentCom Containment", playSound: true, colorOverride: Color.FromHex("#DAA520"));
            }
        }
    }

    private void OnCapsuleStartup(EntityUid uid, QlippothCapsuleComponent component, ComponentStartup args)
    {
        // Capsule spawned - awaiting Security to physically move it to containment
    }

    private void OnCapsuleDestroyed(EntityUid uid, QlippothCapsuleComponent component, DestructionEventArgs args)
    {
        if (component.ContainedQlippothProto == null)
            return;

        var location = Transform(uid).MapID == _containmentDim.ContainmentMapId
            ? "Containment Dimension"
            : "Station Grid";
        Spawn(component.ContainedQlippothProto, Transform(uid).Coordinates);
        _chatSystem.DispatchGlobalAnnouncement(
            Loc.GetString("qgate-announcement-capsule-breach", ("location", location)),
            "CentCom Emergency Alert", playSound: true, colorOverride: Color.FromHex("#DC143C"));
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

        if (capsule.ContainedQlippothProto == null)
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

        // Spawn the Qlippoth entity at the chamber's location
        var qlippothUid = Spawn(capsule.ContainedQlippothProto, chamberXform.Coordinates);

        // Update chamber state
        chamber.IsOccupied = true;
        chamber.ContainedQlippoth = qlippothUid;
        Dirty(chamberUid, chamber);

        // Update Qlippoth with its chamber reference
        if (TryComp<QlippothComponent>(qlippothUid, out var qlippoth))
        {
            qlippoth.ContainmentChamberId = chamber.ChamberId;
            Dirty(qlippothUid, qlippoth);
        }

        // Destroy the capsule
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

        if (capsule.ContainedQlippothProto == null)
            return;

        QueueDel(capsuleUid);
    }
}
