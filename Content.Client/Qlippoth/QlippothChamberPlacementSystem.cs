using Content.Client.Hands.Systems;
using Content.Shared.Qlippoth.Components;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client.Qlippoth;

/// <summary>
/// Starts the standard placement overlay while a chamber construction kit is held.
/// </summary>
public sealed class QlippothChamberPlacementSystem : EntitySystem
{
    private const string PlacementMode = nameof(QlippothChamberPlacementMode);

    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPlacementManager _placement = default!;
    [Dependency] private readonly HandsSystem _hands = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var player = _playerManager.LocalSession?.AttachedEntity;
        if (player is not { } playerUid)
            return;

        var held = _hands.GetActiveItem(playerUid.AsNullable());
        if (held is not { } heldUid || !TryComp<QlippothChamberConstructionKitComponent>(heldUid, out _))
        {
            if (_placement.CurrentPermission?.EntityType == "ContainmentChamberMarker" &&
                _placement.CurrentPermission.PlacementOption == PlacementMode)
            {
                _placement.Clear();
            }

            return;
        }

        if (_placement.CurrentPermission?.MobUid == heldUid &&
            _placement.CurrentPermission.PlacementOption == PlacementMode)
        {
            return;
        }

        _placement.BeginPlacing(new PlacementInformation
        {
            MobUid = heldUid,
            EntityType = "ContainmentChamberMarker",
            PlacementOption = PlacementMode,
            Range = 8,
            UseEditorContext = false
        });
    }
}
