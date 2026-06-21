# Fix: Light shining up instead of down + dark terrain

## Root cause
`Rotation = new Vector3(Mathf.Pi / 2, 0, 0)` rotates local -Z to world **+Y (UP)**.
The DirectionalLight3D shines along local -Z, so the light points away from terrain.
Zero light hits the ground → everything is dark.

## Fix
Change `Mathf.Pi / 2` to `-Mathf.Pi / 2` in `VivariumMain.cs`.
Rotation around X by -90° maps local -Z to world -Y (DOWN).

## Verification
- Build — must pass
- Launch game — terrain should be lit from above with soft shadows from SDFGI + ambient
