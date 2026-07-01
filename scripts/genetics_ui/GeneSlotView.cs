using System;
using System.Linq;
using Godot;
using Vivarium.Core;

namespace Vivarium.Scripts;

/// <summary>
/// One draggable/droppable gene tile — used both as a fixed ring/base slot on the splice board
/// and as a tray chip. <see cref="Role"/> decides whether it accepts drops (a slot) or is a pure
/// drag source (a tray chip). Dragging a gene onto an occupied slot swaps: the slot's previous
/// gene moves onto the dragged-from tile, so a tray chip that gave up its gene and received
/// nothing back removes itself (<see cref="Cleared"/>).
/// </summary>
public partial class GeneSlotView : PanelContainer
{
    public enum SlotRole { RingSlot, BaseSlot, TrayChip }

    public SlotRole Role { get; set; } = SlotRole.TrayChip;
    public Gene? Gene { get; private set; }
    public bool Locked { get; set; }

    /// <summary>Fired when this tile becomes empty as a result of a drag it did not initiate
    /// picking up a replacement — i.e. a tray chip that was dragged away with nothing to swap
    /// back in. Tray containers use this to free the now-empty chip node.</summary>
    public event Action<GeneSlotView>? Cleared;

    private Label _label = null!;
    private StyleBoxFlat _style = null!;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(112, 72);

        _style = new StyleBoxFlat
        {
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderColor = new Color(0f, 0f, 0f, 0.35f),
        };
        AddThemeStyleboxOverride("panel", _style);

        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _label.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_label);

        Refresh();
    }

    public void SetGene(Gene? gene)
    {
        Gene = gene;
        Refresh();
    }

    private void Refresh()
    {
        if (Locked)
        {
            Modulate = new Color(1f, 1f, 1f, 0.4f);
            _label.Text = "locked";
            _style.BgColor = new Color(0.2f, 0.2f, 0.2f);
            return;
        }

        Modulate = Colors.White;
        if (Gene is null)
        {
            _label.Text = Role == SlotRole.BaseSlot ? "Base Gene Slot" : "Gene Slot";
            _style.BgColor = new Color(0.88f, 0.88f, 0.9f);
            return;
        }

        _label.Text = $"{Gene.Id}\n[{Gene.Rarity}]";
        _style.BgColor = RarityColor(Gene.Rarity);
    }

    public static Color RarityColor(Rarity rarity) => rarity switch
    {
        Rarity.Common => new Color(0.78f, 0.78f, 0.8f),
        Rarity.Rare => new Color(0.4f, 0.58f, 0.95f),
        Rarity.Legendary => new Color(0.95f, 0.76f, 0.18f),
        _ => Colors.White,
    };

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (Gene is null || Locked) return default;

        var preview = new Label { Text = _label.Text, Modulate = new Color(1f, 1f, 1f, 0.85f) };
        SetDragPreview(preview);

        var payload = new Godot.Collections.Dictionary { ["source"] = this };
        return payload;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (Role == SlotRole.TrayChip || Locked) return false;
        if (!TryGetSource(data, out var source) || source == this || source.Gene is null) return false;

        var wanted = Role == SlotRole.BaseSlot ? GeneKind.Base : GeneKind.Specialty;
        return source.Gene.Kind == wanted;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!TryGetSource(data, out var source) || source.Gene is null) return;

        var incoming = source.Gene;
        var displaced = Gene;

        source.SetGene(displaced);
        if (displaced is null && source.Role == SlotRole.TrayChip)
            source.Cleared?.Invoke(source);

        SetGene(incoming);
    }

    private static bool TryGetSource(Variant data, out GeneSlotView source)
    {
        source = null!;
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var dict = data.AsGodotDictionary();
        if (!dict.TryGetValue("source", out var value)) return false;
        if (value.AsGodotObject() is not GeneSlotView view) return false;
        source = view;
        return true;
    }
}
