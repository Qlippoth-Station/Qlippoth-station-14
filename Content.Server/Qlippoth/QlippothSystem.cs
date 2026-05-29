using Content.Shared.Interaction.Events;
using Robust.Shared.Audio.Systems;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// This handles logic and interactions relating to all Qlippoths.
    /// For different types of Qlippoths: <see cref="QlippothHandSizeComponent"/>
    /// </summary>
    public sealed partial class QlippothSystem : EntitySystem
    {
        [Dependency] private SharedAudioSystem _audio = default!; // audio functionality for testing and prototyping.
        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<QlippothHandSizeComponent, UseInHandEvent>(OnUseInHand);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);
        }

        private void OnUseInHand(EntityUid uid, QlippothHandSizeComponent comp, UseInHandEvent args)
        {
            _audio.PlayPvs(comp.QlippothSound, uid);
        }
    }
}
