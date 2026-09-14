using Content.Shared.Qlippoth;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Identity component of a Qlippoth. Every Qlippoth carries this (it lives on QlippothBase).
    /// Says which Q-Gate phases can bring this Qlippoth, what its rift looks like and what it is worth.
    /// Nothing here is tied to a single "phase": a Qlippoth may fit several gate levels, and a gate level
    /// may have several candidate Qlippoths (QlippothSystem picks one by weight).
    ///
    /// Passive effects (sanity auras, corruption...) are NOT here either: give the Qlippoth a ProximityInitiation action.
    /// Presence and Movement describe *how* it exists and behaves (their own components); this one is *where it comes from and what it is worth*.
    /// </summary>
    [RegisterComponent]
    public sealed partial class QlippothComponent : Component
    {
        /// <summary>Q-Gate phases whose rift can contain this Qlippoth. Empty = never spawns from a gate.</summary>
        [DataField]
        public List<QGatePhase> GatePhases { get; set; } = new();

        /// <summary>Relative chance among all Qlippoths that share a gate phase. 0 = listed but never picked.</summary>
        [DataField]
        public float SpawnWeight { get; set; } = 1f;

        /// <summary>How the rift dimension behind the gate is built for this Qlippoth.</summary>
        [DataField]
        public QlippothDungeon Dungeon { get; set; } = new ArenaDungeon();

        /// <summary>Cargo price when a secured copy is sold on the containment market.</summary>
        [DataField]
        public int MarketPrice { get; set; } = 500;

        /// <summary>Research points one research console scan yields while this Qlippoth is contained.</summary>
        [DataField]
        public int ResearchPoints { get; set; } = 50;
    }
}
