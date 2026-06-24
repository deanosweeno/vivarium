using Godot;
using Vivarium.Core;

namespace Vivarium.Scripts;

public partial class BlobVisual : Node3D
{
    [Export] private MeshInstance3D? _cube;

    private Blob _model = null!;
    private StandardMaterial3D _material = null!;

    public void Init(Blob model)
    {
        _model = model;

        // Find the cube mesh: use export if wired, otherwise search children
        if (_cube == null)
        {
            foreach (var child in GetChildren())
            {
                if (child is MeshInstance3D mi)
                {
                    _cube = mi;
                    break;
                }
            }
        }

        if (_cube == null)
        {
            GD.PrintErr("BlobVisual: no MeshInstance3D child found");
            return;
        }

        // Create a unique unshaded material so each blob outputs exact color
        // without lighting washing out the pastel tones.
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _cube.MaterialOverride = _material;

        SyncFromModel();
    }

    public void SyncFromModel()
    {
        if (_model == null || _cube == null) return;

        // Set world position (model Y is vertical, add 0.5 so cube sits above model origin)
        Position = new Vector3(_model.Position.X, _model.Position.Y, _model.Position.Z);

        // Apply pastel color
        _material.AlbedoColor = new Color(_model.R, _model.G, _model.B);
    }
}
