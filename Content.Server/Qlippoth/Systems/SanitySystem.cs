using Content.Shared.Qlippoth.Components;

namespace Content.Server.Qlippoth.Systems;

/// <summary>
/// The crew Sanity stat. Nothing here decides *when* sanity drops; that is the Qlippoth's own
/// action list (e.g. ProximityInitiation + DamageSanityResult). This system only applies the numbers.
/// </summary>
public sealed class SanitySystem : EntitySystem
{
    /// <summary>Lower sanity by a flat amount. With scaled = true the target's DrainMultiplier is applied (auras use this, direct costs don't).</summary>
    public void DamageSanity(EntityUid uid, float amount, bool scaled = false, SanityComponent? sanity = null)
    {
        if (!Resolve(uid, ref sanity, false))
            return;

        if (scaled)
            amount *= sanity.DrainMultiplier;

        sanity.CurrentSanity = MathF.Max(0f, sanity.CurrentSanity - amount);
        Dirty(uid, sanity);
    }

    public void RestoreSanity(EntityUid uid, float amount, SanityComponent? sanity = null)
    {
        if (!Resolve(uid, ref sanity))
            return;

        sanity.CurrentSanity = MathF.Min(sanity.MaxSanity, sanity.CurrentSanity + amount);
        Dirty(uid, sanity);
    }
}
