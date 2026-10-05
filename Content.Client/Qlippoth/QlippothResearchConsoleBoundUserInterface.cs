using System.Numerics;
using Content.Shared.Qlippoth;
using Content.Shared.Qlippoth.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Localization;

namespace Content.Client.Qlippoth;

/// <summary>
/// Displays the generated research graph for the Qlippoth currently inside the linked chamber.
/// This is a view only: all experiment, dependency, cooldown, and reward checks are repeated server-side.
/// </summary>
public sealed class QlippothResearchConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private DefaultWindow? _window;
    private Label? _status;
    private BoxContainer? _nodes;

    protected override void Open()
    {
        base.Open();
        _window = new DefaultWindow
        {
            Title = Loc.GetString("research-console-title"),
            MinSize = new Vector2(560, 420),
        };

        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _status = new Label { Text = Loc.GetString("research-console-waiting") };
        content.AddChild(_status);
        _nodes = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        content.AddChild(new ScrollContainer
        {
            VerticalExpand = true,
            Children = { _nodes },
        });

        _window.Contents.AddChild(content);
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (_status == null || _nodes == null || state is not QlippothResearchConsoleBuiState researchState)
            return;

        _status.Text = researchState.ChamberOccupied
            ? Loc.GetString("research-console-specimen-status",
                ("target", researchState.QlippothName ?? Loc.GetString("research-console-no-target")),
                ("cooldown", MathF.Ceiling(researchState.ActivityCooldown)))
            : Loc.GetString(researchState.HasLinkedChamber
                ? "research-console-empty"
                : "research-console-no-target");

        _nodes.RemoveAllChildren();
        foreach (var node in researchState.Nodes)
        {
            var nodeBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            var title = Loc.GetString(node.Name);
            var description = string.IsNullOrWhiteSpace(node.Description)
                ? string.Empty
                : Loc.GetString(node.Description);
            var progress = node.IsGear
                ? Loc.GetString(node.GearIssued ? "research-console-gear-issued" : "research-console-terminal")
                : node.Unlocked
                    ? Loc.GetString("research-console-discovered")
                    : Loc.GetString("research-console-progress",
                        ("progress", node.Progress),
                        ("required", node.RequiredProgress));

            nodeBox.AddChild(new Label
            {
                Text = $"{(node.IsCheckpoint ? Loc.GetString("research-console-checkpoint") + " " : string.Empty)}{title} - {progress}",
            });
            if (!string.IsNullOrWhiteSpace(node.Description))
                nodeBox.AddChild(new Label { Text = description });

            if (node.IsGear)
            {
                if (node.Available && !node.GearIssued)
                {
                    var gear = new Button { Text = Loc.GetString("research-console-claim-gear") };
                    gear.OnPressed += _ => SendMessage(new QlippothResearchGearMessage(node.Id));
                    nodeBox.AddChild(gear);
                }
            }
            else if (!node.Unlocked)
            {
                var methods = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
                foreach (var activity in node.Activities)
                {
                    var button = new Button
                    {
                        Text = Loc.GetString(GetActivityLocalizationKey(activity)),
                        Disabled = !node.Available || researchState.ActivityCooldown > 0f,
                    };
                    button.OnPressed += _ => SendMessage(new QlippothResearchActivityMessage(node.Id, activity));
                    methods.AddChild(button);
                }

                nodeBox.AddChild(methods);
            }

            _nodes.AddChild(nodeBox);
        }
    }

    private static string GetActivityLocalizationKey(QlippothResearchActivity activity)
    {
        return activity switch
        {
            QlippothResearchActivity.Observation => "research-console-activity-observation",
            QlippothResearchActivity.ResonanceScan => "research-console-activity-resonance",
            QlippothResearchActivity.ControlledTest => "research-console-activity-test",
            QlippothResearchActivity.ContainmentDiagnostics => "research-console-activity-diagnostics",
            _ => "research-console-activity-observation",
        };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Close();
    }
}
