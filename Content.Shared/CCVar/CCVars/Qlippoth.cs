using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<float> QlippothGateSpawnInterval =
        CVarDef.Create("qlippoth.gate_spawn_interval", 600f, CVar.SERVERONLY);

    public static readonly CVarDef<int> QlippothMaxActiveGates =
        CVarDef.Create("qlippoth.max_active_gates", 1, CVar.SERVERONLY);

    public static readonly CVarDef<int> QlippothPhase1Weight =
        CVarDef.Create("qlippoth.phase1_weight", 55, CVar.SERVERONLY);

    public static readonly CVarDef<int> QlippothPhase2Weight =
        CVarDef.Create("qlippoth.phase2_weight", 25, CVar.SERVERONLY);

    public static readonly CVarDef<int> QlippothPhase3Weight =
        CVarDef.Create("qlippoth.phase3_weight", 12, CVar.SERVERONLY);

    public static readonly CVarDef<int> QlippothPhase4Weight =
        CVarDef.Create("qlippoth.phase4_weight", 6, CVar.SERVERONLY);

    public static readonly CVarDef<int> QlippothPhase5Weight =
        CVarDef.Create("qlippoth.phase5_weight", 2, CVar.SERVERONLY);

    public static readonly CVarDef<int> QlippothPhase1Price =
        CVarDef.Create("qlippoth.phase1_price", 500, CVar.SERVERONLY);

    public static readonly CVarDef<int> QlippothPhase2Price =
        CVarDef.Create("qlippoth.phase2_price", 1000, CVar.SERVERONLY);

    public static readonly CVarDef<int> QlippothPhase3Price =
        CVarDef.Create("qlippoth.phase3_price", 2000, CVar.SERVERONLY);

    public static readonly CVarDef<int> QlippothPhase4Price =
        CVarDef.Create("qlippoth.phase4_price", 4000, CVar.SERVERONLY);

    public static readonly CVarDef<int> QlippothPhase5Price =
        CVarDef.Create("qlippoth.phase5_price", 8000, CVar.SERVERONLY);

    public static readonly CVarDef<float> QlippothCorruptionRadius =
        CVarDef.Create("qlippoth.corruption_radius", 5f, CVar.SERVERONLY);

    public static readonly CVarDef<float> QlippothCorruptionChance =
        CVarDef.Create("qlippoth.corruption_chance", 0.02f, CVar.SERVERONLY);

    public static readonly CVarDef<float> QlippothCorruptionDuration =
        CVarDef.Create("qlippoth.corruption_duration", 60f, CVar.SERVERONLY);

    public static readonly CVarDef<float> QlippothCorruptionSanityDrain =
        CVarDef.Create("qlippoth.corruption_sanity_drain", 2f, CVar.SERVERONLY);

    public static readonly CVarDef<float> QlippothCorruptionPulseInterval =
        CVarDef.Create("qlippoth.corruption_pulse_interval", 10f, CVar.SERVERONLY);

    public static readonly CVarDef<float> QlippothCorruptionSpreadChance =
        CVarDef.Create("qlippoth.corruption_spread_chance", 0.15f, CVar.SERVERONLY);
}
