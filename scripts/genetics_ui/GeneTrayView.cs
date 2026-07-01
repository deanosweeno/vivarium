using Godot;
using Vivarium.Core;

namespace Vivarium.Scripts;

/// <summary>
/// A scrollable tray of draggable <see cref="GeneSlotView"/> chips (one per collected gene of
/// <see cref="AcceptedKind"/>), and a drop target for returning a gene dragged off a board slot
/// (creates a fresh chip, clears the source slot). The board slots themselves handle
/// slot-to-slot / tray-to-slot drops; this only handles the "drag back into the tray" case.
/// </summary>
public partial class GeneTrayView : Control
{
    [Export] public GeneKind AcceptedKind { get; set; } = GeneKind.Specialty;
    [Export] public NodePath ChipContainerPath { get; set; } = new();
    [Export] public PackedScene? ChipScene { get; set; }

    private Control _chipContainer = null!;

    public override void _Ready()
    {
        _chipContainer = GetNode<Control>(ChipContainerPath);
    }

    public GeneSlotView AddChip(Gene gene)
    {
        var chip = ChipScene is not null
            ? ChipScene.Instantiate<GeneSlotView>()
            : new GeneSlotView();
        _chipContainer.AddChild(chip);
        chip.Role = GeneSlotView.SlotRole.TrayChip;
        chip.SetGene(gene);
        chip.Cleared += c => c.QueueFree();
        return chip;
    }

    public void Clear()
    {
        foreach (var child in _chipContainer.GetChildren())
            child.QueueFree();
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var dict = data.AsGodotDictionary();
        if (!dict.TryGetValue("source", out var value)) return false;
        if (value.AsGodotObject() is not GeneSlotView source) return false;
        return source.Role != GeneSlotView.SlotRole.TrayChip
            && source.Gene is not null
            && source.Gene.Kind == AcceptedKind;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var dict = data.AsGodotDictionary();
        var source = (GeneSlotView)dict["source"].AsGodotObject();
        if (source.Gene is not { } gene) return;

        source.SetGene(null);
        AddChip(gene);
    }
}
