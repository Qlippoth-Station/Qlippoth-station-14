using Robust.Shared.GameObjects;
using Content.Shared.Movement.Pulling.Events;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Listens to SS14 events and forwards matching Qlippoth actions to QlippothActionResultSystem.
    /// Each initiation type has its own subscriber below.
    /// </summary>
    public sealed partial class QlippothActionInitiationSystem : EntitySystem
    {
        [Dependency] private QlippothActionResultSystem _resultSystem = default!;

        public override void Initialize()
        {
            base.Initialize();
            // TODO: Subscribers for all initiations that require existing events.
            SubscribeLocalEvent<QlippothActionsComponent, PullStartedMessage>(OnPullStarted);
            // SubscribeLocalEvent<QlippothActionsComponent, PushAttemptEvent>(OnEvent);
        }

        /// <summary>
        /// Detects the right action to initiate and relays actions to Result System.
        /// It could be a bit slow, as all qlippoth event subscriptions fire DispatchActions. As long as total number of Qlippoths active at once is low, it will be fine
        /// but if performance problems arise because of this, restructuring will be needed. Potentially some of the handling could be moved to another function
        /// in an example scenario if the biggest contributer calls dispatch around 30% of all calls, it can have its own handling maybe to get rid of foreach?
        /// </summary>
        private void DispatchActions(EntityUid uid, QlippothActionsComponent actionsComponent, System.Type initiationType, object? eventArgs = null)
        {
            List<QlippothAction> triggeredActions = new();
            foreach (var action in actionsComponent.Actions)
            {
                if (action.Initiation.GetType() == initiationType) // input the lowest level class in handlers so it doesnt get confused
                    triggeredActions.Add(action);
            }
            _resultSystem.ExecuteResults(uid, triggeredActions, eventArgs);
        }
        #region Event Handlers
        private void OnPullStarted(EntityUid uid, QlippothActionsComponent component, PullStartedMessage eventArgs)
        {
            DispatchActions(uid, component, typeof(OnPullInitiation), eventArgs);
        }
        #endregion

        #region Qlippoth Specific Event Definitions
        #endregion
    }
}
