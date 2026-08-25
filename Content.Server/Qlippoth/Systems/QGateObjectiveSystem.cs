using Content.Shared.Interaction;
using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;

namespace Content.Server.Qlippoth.Systems;

public sealed partial class QGateObjectiveSystem : EntitySystem
{
    [Dependency] private QGateSystem _qGate = default!;
    [Dependency] private SharedAudioSystem _audio = default!;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<QGateDungeonObjectiveComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<QGateDungeonObjectiveComponent, ActivateInWorldEvent>(OnActivateInWorld);
    }

    private void OnInteractHand(EntityUid uid, QGateDungeonObjectiveComponent objective, ref InteractHandEvent args)
    {
        if (args.Handled || objective.Completed || !Exists(objective.Gate))
            return;

        ExecuteActions(uid, objective, args.User);
        args.Handled = true;
    }

    private void OnActivateInWorld(EntityUid uid, QGateDungeonObjectiveComponent objective, ActivateInWorldEvent args)
    {
        if (args.Handled || objective.Completed || !Exists(objective.Gate))
            return;

        ExecuteActions(uid, objective, args.User);
        args.Handled = true;
    }

    private void ExecuteActions(EntityUid uid, QGateDungeonObjectiveComponent objective, EntityUid user)
    {
        foreach (var action in objective.Actions)
        {
            if (action.Initiation is not QGateObjectiveInteractInitiation)
                continue;

            foreach (var result in action.Results)
                ExecuteResult(uid, objective, user, result);
        }
    }

    private void ExecuteResult(EntityUid uid, QGateDungeonObjectiveComponent objective, EntityUid user,
        QGateObjectiveResult result)
    {
        switch (result)
        {
            case CompleteQGateObjectiveResult:
                CompleteObjective(uid, user);
                break;
            case PlayQGateObjectiveSoundResult sound:
                _audio.PlayPvs(new SoundPathSpecifier(sound.SoundPath), uid,
                    AudioParams.Default.WithVolume(sound.Volume));
                break;
            case SpawnQGateObjectiveEntityResult spawn:
                var coordinates = Transform(uid).Coordinates;
                for (var i = 0; i < spawn.Count; i++)
                    Spawn(spawn.Prototype, coordinates);
                break;
        }
    }

    public void CompleteObjective(EntityUid uid, EntityUid user)
    {
        if (!TryComp<QGateDungeonObjectiveComponent>(uid, out var objective) || objective.Completed)
            return;

        objective.Completed = true;
        Dirty(uid, objective);
        _qGate.ReportObjectiveCompleted(objective.Gate);
    }
}
