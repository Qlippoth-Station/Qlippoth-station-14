using Content.Server.Chat.Managers;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Qlippoth.Components;
using Content.Server.Popups;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Examine;
using Robust.Shared.Map;

namespace Content.Server.Qlippoth.Systems;

public sealed partial class ContainmentPortalSystem : EntitySystem
{
    [Dependency] private ContainmentDimensionSystem _containment = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IMapManager _mapManager = default!;
    private readonly Dictionary<EntityUid, PortalReturnRoute> _returnRoutes = new();
    private readonly Dictionary<EntityUid, MapCoordinates> _portalDestinations = new();

    public void RegisterReturnPortal(EntityUid portal, MapCoordinates destination)
    {
        _portalDestinations[portal] = destination;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ContainmentPortalComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ContainmentPortalComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId != "DimensionCommander" || EnterContainment(args.Mob))
            return;

        _chat.DispatchServerMessage(args.Player, Loc.GetString("containment-dimension-commander-transfer-failed"));
        SubscribeLocalEvent<ContainmentPortalComponent, ExaminedEvent>(OnPortalExamined);
        SubscribeLocalEvent<ContainmentPortalComponent, EntityTerminatingEvent>(OnPortalTerminating);
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);
    }

    private void OnPortalExamined(EntityUid uid, ContainmentPortalComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var ready = _portalDestinations.TryGetValue(uid, out var destination)
            ? _mapManager.MapExists(destination.MapId)
            : _containment.IsDepartmentPortalReady(component.Department);
        args.PushMarkup(Loc.GetString("containment-portal-status",
            ("department", component.Department),
            ("status", Loc.GetString(ready
                ? "containment-portal-ready"
                : "containment-portal-unavailable"))));
    }

    private void OnActivateInWorld(EntityUid uid, ContainmentPortalComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        if (!CanUsePortal(uid, args.User))
        {
            args.Handled = true;
            return;
        }

        if (component.IsExitPortal)
        {
            args.Handled = ExitPortal(uid, args.User);
            return;
        }

        if (_containment.IsContainmentDimension(Transform(args.User).MapID))
            return;

        args.Handled = EnterContainment(args.User, component.Department);
    }

    private void OnAfterInteract(EntityUid uid, ContainmentPortalComponent component, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        if (!CanUsePortal(uid, args.User))
        {
            args.Handled = true;
            return;
        }

        if (component.IsExitPortal)
        {
            args.Handled = ExitPortal(uid, args.User);
            return;
        }

        if (_containment.IsContainmentDimension(Transform(args.User).MapID))
            return;

        args.Handled = EnterContainment(args.User, component.Department);
    }

    private bool EnterContainment(EntityUid user, ContainmentDepartment department)
    {
        var returnCoordinates = _transform.GetMapCoordinates(user);
        _containment.EnsureContainmentDimensionCreated();
        if (!_containment.TryGetContainmentEntryCoordinates(out var entryCoordinates))
            return false;
        }

        _returnCoordinates[user] = returnCoordinates;
        _transform.SetMapCoordinates(user, entryCoordinates);
        return true;
    }

    private bool ExitContainment(EntityUid user)
    {
        if (!_containment.IsContainmentDimension(Transform(user).MapID) ||
            !_returnRoutes.TryGetValue(user, out var route))
            return false;

        if (!_mapManager.MapExists(route.ReturnCoordinates.MapId) ||
            !_mapManager.TryFindGridAt(route.ReturnCoordinates, out _, out _))
        {
            _returnRoutes.Remove(user);
            _popup.PopupEntity(Loc.GetString("containment-portal-destination-unavailable"), user, user);
            return false;
        }

        var currentCoordinates = _transform.GetMapCoordinates(user);
        var pulledCapsule = GetPulledCapsule(user, currentCoordinates);
        _returnRoutes.Remove(user);
        _transform.SetMapCoordinates(user, route.ReturnCoordinates);
        TransferPulledCapsule(pulledCapsule, currentCoordinates, route.ReturnCoordinates);
        return true;
    }

    private bool ExitPortal(EntityUid portal, EntityUid user)
    {
        if (_portalDestinations.TryGetValue(portal, out var destination))
        {
            if (!_mapManager.MapExists(destination.MapId))
            {
                _portalDestinations.Remove(portal);
                _popup.PopupEntity(Loc.GetString("containment-portal-destination-unavailable"), user, user);
                return false;
            }

            var currentCoordinates = _transform.GetMapCoordinates(user);
            var pulledCapsule = GetPulledCapsule(user, currentCoordinates);
            _returnRoutes.Remove(user);
            _transform.SetMapCoordinates(user, destination);
            TransferPulledCapsule(pulledCapsule, currentCoordinates, destination);
            return true;
        }

        if (!_returnRoutes.TryGetValue(user, out var route))
        {
            _popup.PopupEntity(Loc.GetString("containment-portal-destination-unavailable"), user, user);
            return false;
        }

        if (!TryComp<ContainmentPortalComponent>(portal, out var portalComponent) ||
            portalComponent.Department != route.Department)
        {
            _popup.PopupEntity(Loc.GetString("containment-portal-wrong-department"), user, user);
            return false;
        }

        return ExitContainment(user);
    }

    private bool CanUsePortal(EntityUid portal, EntityUid user)
    {
        if (!TryComp<AccessReaderComponent>(portal, out var accessReader) ||
            _accessReader.IsAllowed(user, portal, accessReader))
            return true;

        _popup.PopupEntity(Loc.GetString("containment-portal-access-denied"), portal, user);
        return false;
    }

    private void OnPortalTerminating(
        EntityUid uid,
        ContainmentPortalComponent component,
        ref EntityTerminatingEvent args)
    {
        _portalDestinations.Remove(uid);
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        _returnRoutes.Remove(args.Entity);
    }

    private (EntityUid Entity, MapCoordinates Coordinates, PullableComponent Pullable)? GetPulledCapsule(
        EntityUid user, MapCoordinates userCoordinates)
    {
        if (!TryComp<PullerComponent>(user, out var puller) ||
            puller.Pulling is not { } pulled ||
            !HasComp<QlippothCapsuleComponent>(pulled) ||
            !TryComp<PullableComponent>(pulled, out var pullable))
            return null;

        var capsuleCoordinates = _transform.GetMapCoordinates(pulled);
        if (capsuleCoordinates.MapId != userCoordinates.MapId)
            return null;

        return (pulled, capsuleCoordinates, pullable);
    }

    private void TransferPulledCapsule(
        (EntityUid Entity, MapCoordinates Coordinates, PullableComponent Pullable)? capsule,
        MapCoordinates userFrom, MapCoordinates userTo)
    {
        if (capsule is not { } pulled)
            return;

        _pulling.TryStopPull(pulled.Entity, pulled.Pullable);
        var offset = pulled.Coordinates.Position - userFrom.Position;
        _transform.SetMapCoordinates(pulled.Entity, new MapCoordinates(userTo.Position + offset, userTo.MapId));
    }

    private sealed record PortalReturnRoute(
        MapCoordinates ReturnCoordinates,
        ContainmentDepartment Department);
}
