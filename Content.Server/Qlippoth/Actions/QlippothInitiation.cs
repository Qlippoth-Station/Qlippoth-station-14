using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Qlippoth
{
    /// <summary>
    /// Abstract base for all Qlippoth initiations.
    /// Defines when a Qlippoth action is triggered.
    ///
    ///
    /// !!!!! right now, parent partial classes don't really contribute anything, later on common ground could be implemented so it will stay  !!!!
    /// it also provides a little bit of clarity and grouping, could be structured to have main initiation types as regions though.
    /// </summary>




    [ImplicitDataDefinitionForInheritors]
    public abstract partial class QlippothInitiation
    {
        public abstract void Register(EntityUid uid, QlippothActionInitiationSystem initiationSystem);
    }


    #region Initiation Types
    [DataDefinition]
    public abstract partial class TriggerInitiation : QlippothInitiation { }
    // -------------------------------------------------------------------------
    // Trigger - Activation depends on a condition specific to the Qlippoth. Usually does not interact with in-game events.
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class ExternalInitiation : QlippothInitiation
    // -------------------------------------------------------------------------
    // External - Activated by others, ex: in-hand activation by another player.
    // -------------------------------------------------------------------------
    {
        // [DataField]
        // maybe a variable for the activator here? could be used by most externalinitiations as "activator" mob is the common ground
        // need to find what players or mobs go as
    }
    [DataDefinition]
    public abstract partial class TimedInitiation : QlippothInitiation
    // -------------------------------------------------------------------------
    // Timed - Activated after a certain amount of time or in intervals, after initialization, arrival to the station, containment etc.
    // May be used in combination with other initiations with chained actions.
    // -------------------------------------------------------------------------
    {
        [DataField]
        public float Interval { get; set; } = 10f;

        public override void Register(EntityUid uid, QlippothActionInitiationSystem initiationSystem)
        {
            // register a timer event that triggers every Interval seconds, and call initiation system dispatch with this initiation type.
            // need to also determine which part of the code or object should register
        }
    }

    [DataDefinition]
    public abstract partial class EventBasedInitiation : QlippothInitiation
    // -------------------------------------------------------------------------
    // EventBased - Activates at the start or end of other in-game events. This does not necessarily mean coded event system, more focused on gameplay events.
    // -------------------------------------------------------------------------
    {
        [DataField]
        public string EventId { get; set; } = string.Empty;
    }

    [DataDefinition]
    public abstract partial class InternalInitiation : QlippothInitiation { }
    // -------------------------------------------------------------------------
    // Internal - Activates when the controller of the Qlippoth decides to. Requires UI for Players.
    // -------------------------------------------------------------------------

    [DataDefinition]
    public abstract partial class InterfaceInitiation : QlippothInitiation { }
    // -------------------------------------------------------------------------
    // Interface - Opens up a UI Interface, acts more like a result that opens up more actions. Interface could be shown to the controller or others that interact with it.
    // -------------------------------------------------------------------------
    #endregion

    #region Fundamental Initiation Implementations
    [DataDefinition]
    public sealed partial class InRangeClickInitiation : ExternalInitiation
    {
        public override void Register(EntityUid uid, QlippothActionInitiationSystem initiationSystem)
        {
            ISawmill sawmill = Logger.GetSawmill("qlippoth");
            sawmill.Warning("InRangeClickInitiation Register() is not implemented yet, actions with this initiation will not trigger.");
        }
    }

    [DataDefinition]
    public sealed partial class OnPullInitiation : TriggerInitiation
    {
        public override void Register(EntityUid uid, QlippothActionInitiationSystem initiationSystem)
        {
            ISawmill sawmill = Logger.GetSawmill("qlippoth");
            sawmill.Warning("OnPullInitiation Register() is not implemented yet, actions with this initiation will not trigger.");
        }
    }
    #endregion

    #region Complex Initiation Implementations
    #endregion

    #region Other Initiation Implementations
    #endregion
}
