using Vivarium.Core;
using SNVector3 = System.Numerics.Vector3;

namespace Vivarium.Scripts;

/// <summary>
/// Whatever hosts <see cref="SpliceUi"/> — the real game's <c>VivariumMain</c> and devtools'
/// <c>HarnessSimHost</c> — implements this so the splice screen doesn't depend on either concretely.
/// </summary>
public interface ISpliceHost
{
    PlayerInputMode? PlayerInput { get; }
    Simulator Sim { get; }
    CreatureCatalog Creatures { get; }
    SNVector3 PlayerPosition { get; }
    bool Paused { get; set; }
}
