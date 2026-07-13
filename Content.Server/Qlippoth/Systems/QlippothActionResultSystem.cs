using Content.Server.Atmos.EntitySystems;   // AtmosphereSystem
using Robust.Shared.GameObjects;            // SharedTransformSystem
using Robust.Shared.Audio.Systems;          // SharedAudioSystem

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Executes the results of Qlippoth actions.
    /// Called by QlippothActionInitiationSystem when an initiation condition is met.
    /// </summary>
    public sealed partial class QlippothActionResultSystem : EntitySystem
    {
        #region dependencies
        [Dependency] public SharedAudioSystem Audio = default!;
        [Dependency] public AtmosphereSystem Atmosphere = default!;
        [Dependency] public SharedTransformSystem QlippothTransform = default!;
        [Dependency] public IEntityManager QlippothEntityManager = default!;
        #endregion
        public override void Initialize()
        {
            base.Initialize();
        }

        /// <summary>
        /// Called from Initiation system, applies results of initiated actions.
        /// </summary>
        public void ExecuteResults(EntityUid uid, List<QlippothAction> actions, object? eventArgs = null)
        {
            foreach (var action in actions)
            {
                foreach (var result in action.Results)
                {
                    result.Execute(uid, this, eventArgs);
                }
            }
        }
    }
}
