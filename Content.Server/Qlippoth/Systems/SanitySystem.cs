using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Robust.Shared.Timing;

namespace Content.Server.Qlippoth.Systems;

public sealed class SanitySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SanityComponent, TransformComponent>();
        var qlippothQuery = EntityQuery<QlippothComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var sanity, out var xform))
        {
            if (sanity.CurrentSanity <= 0)
                continue;

            // Check proximity to Phase 3+ Qlippoths
            foreach (var (qlippoth, qXform) in qlippothQuery)
            {
                if (xform.MapID != qXform.MapID)
                    continue;

                if (qlippoth.Phase < QGatePhase.Phase3Eclipse)
                    continue;

                var dist = (xform.Coordinates.Position - qXform.Coordinates.Position).Length();
                if (dist < 8.0f)
                {
                    var drain = frameTime * (int)qlippoth.Phase * 2f * sanity.DrainMultiplier;
                    sanity.CurrentSanity = MathF.Max(0f, sanity.CurrentSanity - drain);
                    Dirty(uid, sanity);
                }
            }
        }
    }

    public void RestoreSanity(EntityUid uid, float amount, SanityComponent? sanity = null)
    {
        if (!Resolve(uid, ref sanity))
            return;

        sanity.CurrentSanity = MathF.Min(sanity.MaxSanity, sanity.CurrentSanity + amount);
        Dirty(uid, sanity);
    }
}
