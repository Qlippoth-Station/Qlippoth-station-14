using Content.Shared.Interaction;
using Content.Shared.Qlippoth.Components;
using Robust.Shared.Map;

namespace Content.Server.Qlippoth.Systems;

public sealed partial class ContainmentPortalSystem : EntitySystem
{
    [Dependency] private ContainmentDimensionSystem _containment = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    private readonly Dictionary<EntityUid, MapCoordinates> _returnCoordinates = new();
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
    }

    private void OnActivateInWorld(EntityUid uid, ContainmentPortalComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        if (component.IsExitPortal)
        {
            args.Handled = ExitPortal(uid, args.User);
            return;
        }

        if (_containment.IsContainmentDimension(Transform(args.User).MapID))
            return;

        args.Handled = EnterContainment(args.User);
    }

    private void OnAfterInteract(EntityUid uid, ContainmentPortalComponent component, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        if (component.IsExitPortal)
        {
            args.Handled = ExitPortal(uid, args.User);
            return;
        }

        if (_containment.IsContainmentDimension(Transform(args.User).MapID))
            return;

        args.Handled = EnterContainment(args.User);
    }

    private bool EnterContainment(EntityUid user)
    {
        var returnCoordinates = _transform.GetMapCoordinates(user);
        _containment.EnsureContainmentDimensionCreated();
        var mapId = _containment.ContainmentMapId;
        if (mapId == MapId.Nullspace)
            return false;

        _returnCoordinates[user] = returnCoordinates;
        _transform.SetMapCoordinates(user, new MapCoordinates(new System.Numerics.Vector2(0f, 0f), mapId));
        return true;
    }

    private bool ExitContainment(EntityUid user)
    {
        if (!_containment.IsContainmentDimension(Transform(user).MapID) || !_returnCoordinates.TryGetValue(user, out var returnCoordinates))
            return false;

        _returnCoordinates.Remove(user);
        _transform.SetMapCoordinates(user, returnCoordinates);
        return true;
    }

    private bool ExitPortal(EntityUid portal, EntityUid user)
    {
        if (_portalDestinations.Remove(portal, out var destination))
        {
            _transform.SetMapCoordinates(user, destination);
            return true;
        }

        return ExitContainment(user);
    }
}
