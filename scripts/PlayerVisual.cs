using Godot;
using Vivarium.Core;

namespace Vivarium.Scripts;

/// <summary>
/// Humanoid figure for the player avatar, assembled from Godot primitives in code.
/// Body (capsule) + head (sphere) + two arms (boxes), all gold. Mirrors BlobVisual's
/// Init/SyncFromModel contract but builds its mesh children itself — no .tscn required.
/// </summary>
public partial class PlayerVisual : Node3D
{
    private Blob _model = null!;

    public void Init(Blob model)
    {
        _model = model;

        var mat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.84f, 0.2f) };

        // Body — capsule standing upright
        var body = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.18f, Height = 0.55f },
            MaterialOverride = mat,
            Position = new Vector3(0f, 0f, 0f),
        };
        AddChild(body);

        // Head — sphere sitting above body
        var head = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.22f, Height = 0.44f },
            MaterialOverride = mat,
            Position = new Vector3(0f, 0.47f, 0f),
        };
        AddChild(head);

        // Arms — left and right boxes
        var armMeshL = new BoxMesh { Size = new Vector3(0.28f, 0.12f, 0.12f) };
        var armMeshR = new BoxMesh { Size = new Vector3(0.28f, 0.12f, 0.12f) };

        AddChild(new MeshInstance3D { Mesh = armMeshL, MaterialOverride = mat, Position = new Vector3(-0.32f, 0.15f, 0f) });
        AddChild(new MeshInstance3D { Mesh = armMeshR, MaterialOverride = mat, Position = new Vector3( 0.32f, 0.15f, 0f) });

        SyncFromModel();
    }

    public void SyncFromModel()
    {
        if (_model == null) return;
        Position = new Vector3(_model.Position.X, _model.Position.Y, _model.Position.Z);
    }
}
