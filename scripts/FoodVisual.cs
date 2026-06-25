using Godot;
using Vivarium.Core;

namespace Vivarium.Scripts;

/// <summary>
/// Thin visual for a single <see cref="FoodItem"/> — mirrors <see cref="BlobVisual"/>.
/// Reads the model each frame: positions itself on the item, colors from the food type's
/// hex, and scales/dims with the item's remaining <see cref="FoodItem.Amount"/> so a grazed
/// item visibly shrinks, vanishing while depleted and reappearing when it regrows.
/// </summary>
public partial class FoodVisual : Node3D
{
    [Export] private MeshInstance3D? _mesh;

    private FoodItem _model = null!;
    private StandardMaterial3D _material = null!;

    public void Init(FoodItem model)
    {
        _model = model;

        if (_mesh == null)
        {
            foreach (var child in GetChildren())
            {
                if (child is MeshInstance3D mi)
                {
                    _mesh = mi;
                    break;
                }
            }
        }

        if (_mesh == null)
        {
            GD.PrintErr("FoodVisual: no MeshInstance3D child found");
            return;
        }

        _material = new StandardMaterial3D
        {
            AlbedoColor = Color.FromHtml(_model.Def.ColorHex),
        };
        _mesh.MaterialOverride = _material;

        SyncFromModel();
    }

    public void SyncFromModel()
    {
        if (_model == null || _mesh == null) return;

        Position = new Vector3(_model.Position.X, _model.Position.Y, _model.Position.Z);

        // Hide depleted food; otherwise scale with remaining amount (with a visible floor).
        if (!_model.Available)
        {
            Visible = false;
            return;
        }
        Visible = true;
        float s = 0.4f + 0.6f * _model.Amount;
        Scale = new Vector3(s, s, s);
    }
}
