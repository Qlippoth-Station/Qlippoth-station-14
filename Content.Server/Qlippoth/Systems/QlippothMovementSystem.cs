using Robust.Shared.GameObjects;
using Content.Shared.Movement.Events;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Handles logic relating to how a Qlippoth moves.
    /// </summary>
    public sealed partial class QlippothMovementSystem : EntitySystem
    {
        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<QlippothMovementComponent, UpdateCanMoveEvent>(OnUpdateCanMove);             // to control when a qlippoth can move
            SubscribeLocalEvent<QlippothMovementComponent, CanWeightlessMoveEvent>(OnCanWeightlessMove); // to control if it can move without being affected by gravity
        }

        private void OnUpdateCanMove(EntityUid uid, QlippothMovementComponent comp, UpdateCanMoveEvent args)
        {
            if (comp.MovementType == MovementType.Inanimate || comp.MovementType == MovementType.Static) // static and inanimate qlippoths are not supposed to move, just change movement type if they need to move in another way.
                args.Cancel();
        }

        private void OnCanWeightlessMove(EntityUid uid, QlippothMovementComponent comp, ref CanWeightlessMoveEvent args) // mostly for non-constrained qlippoths, but this is a placeholder
        {
            if (comp.MovementType == MovementType.NonConstrained)
                args.CanMove = true;
        }
    }
}
