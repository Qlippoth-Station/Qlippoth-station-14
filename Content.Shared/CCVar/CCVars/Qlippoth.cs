using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<float> QlippothGateSpawnInterval =
        CVarDef.Create("qlippoth.gate_spawn_interval", 600f, CVar.SERVERONLY);

    public static readonly CVarDef<int> QlippothMaxActiveGates =
        CVarDef.Create("qlippoth.max_active_gates", 1, CVar.SERVERONLY);

    // How often each Q-Gate phase is rolled for automatic gates. Which Qlippoth comes through a phase,
    // its price and its effects are defined on the Qlippoth prototypes themselves (QlippothComponent + QlippothActions).
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
}
