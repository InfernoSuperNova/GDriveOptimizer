using SpaceEngineers.Game.Entities.Blocks;
using VRageMath;

namespace GDriveOptimizer.Gravity;


// -----------------------------
// Field Region (shared among voxels)
// -----------------------------
class FieldRegion
{
    public HashSet<MyGravityGeneratorBase> Generators; // The set of generators affecting this region
    private HashSet<MyGravityGeneratorSphere> _sphericalGenerators;
    public Vector3D CachedGravity;              // Precomputed gravity vector
    public GravityRegionState Validity;                 // Invalidated when generators change
    public long LastTouched = 0;
    public ulong Signature;
    public FieldRegion(HashSet<MyGravityGeneratorBase> gens, long frame, ulong signature)
    {
        Generators = gens;
        _sphericalGenerators = new HashSet<MyGravityGeneratorSphere>();
        foreach (var generator in gens)
        {
            if (generator is MyGravityGeneratorSphere s)
                _sphericalGenerators.Add(s);
        }
        LastTouched = frame;
        Recalculate();
        Signature = signature;
    }

    public Vector3D GetSphericalGravity(Vector3 pos)
    {
        var gravity = Vector3D.Zero;
        foreach (var spherical in _sphericalGenerators)
        {
            if (!spherical.IsWorking) continue;
            var spherePos = spherical.Position * spherical.CubeGrid.GridSize;
            var to = spherePos - pos;
            if (to.LengthSquared() > spherical.Radius * spherical.Radius) continue;
            gravity += to.Normalized() * spherical.GravityAcceleration;
        }
        return gravity;
    }
    
    public void Recalculate()
    {
        
        Vector3D total = Vector3D.Zero;
        foreach (var gen in Generators)
        {
            if (gen.GravityAcceleration == 0) continue;
            if (!gen.IsWorking) continue;
            if (gen is MyGravityGenerator)
            {
                // 'Down' in grid space
                Vector3I downLocal = -Base6Directions.GetIntVector(gen.Orientation.Up);

                // Convert to Vector3 (grid-space direction)
                Vector3 downVector = new Vector3(downLocal);

                // Multiply by gravity strength
                total += downVector * gen.GravityAcceleration;
            }
        }

        var was = CachedGravity;
        CachedGravity = total;
        Validity = GravityRegionState.Valid;
    }

    // public bool ShouldBeDeleted(long frame)
    // {
    //     return ((frame - LastTouched) > Config.I.FramesToCleanupUntouchedVoxels);
    // }
}