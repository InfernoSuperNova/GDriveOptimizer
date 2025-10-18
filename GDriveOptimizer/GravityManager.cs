using Havok;
using Havok.Utils;
using Sandbox.Game.Entities;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRageMath;


namespace GDriveOptimizer;


// -----------------------------
// Gravity Generator
// -----------------------------


public static class GravityManager
{
    public static void Setup()
    {
        GravityGeneratorPropertyPatches.OnFieldSizeChanged += OnFieldSizeChanged;
        GravityGeneratorPropertyPatches.OnGravityAccelerationChanged += OnGravityAccelerationChanged;
        GravityInitPatch.Trigger += OnGravityGeneratorCreated;
        GravityWorkingChangedPatch.Trigger += OnGravityGeneratorFunctionalityChanged;
        GravityClosingPatch.Trigger += OnGravityGeneratorDeleted;
        GravityInitPatch.GridChanged += OnGravityGeneratorChangedGrid;
        Plugin.UpdateEvent += Update;
    }

    public static Vector3D Sample(Vector3D worldPos)
    {
        // TODO: Logic, bvh tree for grids
        return Vector3D.Zero;
    }

    public static Vector3D Sample(MyCubeGrid grid, Vector3I pos)
    {
        var field = ValidateGridField(grid);
        return field.Sample(pos);
    }
    
    private static void Update(long frame)
    {
        foreach (var field in _fields) field.Value.Update(frame);
    }
    private static void OnFieldSizeChanged(MyGravityGenerator gen, Vector3 newSize)
    {
        var field = ValidateGridField(gen.CubeGrid);
        field.ChangeGeneratorSize(gen);
    }
    private static void OnGravityAccelerationChanged(MyGravityGeneratorBase gen, float newStrength)
    {
        var field = ValidateGridField(gen.CubeGrid);
        if (gen is MyGravityGenerator genFull)
            field.ChangeGeneratorStrength(genFull);
    }
    private static void OnGravityGeneratorFunctionalityChanged(MyGravityGeneratorBase gen, bool isFunctional)
    {
        var field = ValidateGridField(gen.CubeGrid);
        if (gen is MyGravityGenerator genFull)
            field.ToggleGenerator(genFull);
    }
    private static void OnGravityGeneratorCreated(MyGravityGeneratorBase gen, MyObjectBuilder_CubeBlock _, MyCubeGrid grid)
    {
        if (gen is MyGravityGenerator genFull)
            AddGravityGeneratorToGrid(grid, genFull);
    }
    private static void OnGravityGeneratorChangedGrid(MyGravityGeneratorBase gen, MyCubeGrid oldGrid, MyCubeGrid newGrid)
    {
        if (gen is MyGravityGenerator genFull)
        {
            RemoveGravityGeneratorFromGrid(oldGrid, genFull);
            AddGravityGeneratorToGrid(newGrid, genFull);
        }
        
    }
    private static void OnGravityGeneratorDeleted(MyGravityGeneratorBase gen)
    {
        if (gen is MyGravityGenerator genFull)
            RemoveGravityGeneratorFromGrid(gen.CubeGrid, genFull);
    }
    
    
    
    
    private static Dictionary<MyCubeGrid, SparseGravityField> _fields = new();


    private static void AddGravityGeneratorToGrid(MyCubeGrid grid, MyGravityGenerator gen)
    {
        var field = ValidateGridField(grid);
        field.AddGenerator(gen);
        
    }

    private static void RemoveGravityGeneratorFromGrid(MyCubeGrid grid, MyGravityGenerator gen)
    {
        var field = ValidateGridField(grid);
        field.RemoveGenerator(gen);
    }

    private static SparseGravityField ValidateGridField(MyCubeGrid grid)
    {
        if (!_fields.ContainsKey(grid)) _fields.Add(grid, new SparseGravityField(grid));
        return _fields[grid];
    }
    
}

public enum GravityRegionState : byte
{
    Valid,          // Up to date
    Dirty,          // Needs recomputation (e.g., strength change)
    LazyInvalid,    // Must be cleaned up, but not urgently
    Invalid,        // Generator count changed or deleted, must be cleaned up, cannot be reused
}

// -----------------------------
// Field Region (shared among voxels)
// -----------------------------
class FieldRegion
{
    public HashSet<MyGravityGeneratorBase> Generators; // The set of generators affecting this region
    public Vector3D CachedGravity;              // Precomputed gravity vector
    public GravityRegionState Validity;                 // Invalidated when generators change
    public long LastTouched = 0;
    public ulong Signature;
    public FieldRegion(HashSet<MyGravityGeneratorBase> gens, long frame, ulong signature)
    {
        Generators = gens;
        LastTouched = frame;
        Recalculate();
        Signature = signature;
    }

    public void Recalculate()
    {
        Vector3D total = Vector3D.Zero;
        foreach (var gen in Generators)
        {
            if (gen.GravityAcceleration == 0) continue;
            if (!gen.IsFunctional) continue;
            total += gen.GravityAcceleration * gen.WorldMatrix.Down;

        }
        CachedGravity = total;
        Validity = GravityRegionState.Valid;
    }

    public bool ShouldBeDeleted(long frame)
    {
        return ((frame - LastTouched) > Config.FramesToDeleteUntouchedRegions);
    }
}

// -----------------------------
// Sparse Gravity Field
// -----------------------------
class SparseGravityField
{
    // Map each voxel to a shared FieldRegion
    private Dictionary<Vector3I, FieldRegion> voxelToRegion = new();
    
    // Optional: canonicalization dictionary to share identical regions
    private Dictionary<ulong, FieldRegion> signatureToRegion = new();
    
    // Track all regions for incremental validation
    private List<FieldRegion> allRegions = new();

    private HashSet<MyGravityGenerator> _generators = new();

    private MyCubeGrid _thisGrid;



    public SparseGravityField(MyCubeGrid grid)
    {
        _thisGrid = grid;
    }
    
    private long _frame = 0;// Todo, check age of regions and delete them from memory if they haven't been touched in a long time
    public void Update(long frame)
    {
        _frame = frame;
    }
    
    public void AddGenerator(MyGravityGenerator generator)
    {
        _generators.Add(generator);
        InvalidateAllRegions();
    }

    public void RemoveGenerator(MyGravityGenerator generator)
    {
        _generators.Remove(generator);
        foreach (var region in allRegions)
            if (region.Generators.Contains(generator))
                region.Validity = GravityRegionState.Invalid;
    }

    public void ChangeGeneratorStrength(MyGravityGenerator generator)
    {
        if (!generator.IsFunctional) return; // Don't need to recalculate if the generator is turned off anyway
        foreach (var region in allRegions) if (region.Generators.Contains(generator))
            region.Validity = GravityRegionState.Dirty;
    }

    public void ToggleGenerator(MyGravityGenerator generator)
    {
        foreach (var region in allRegions) if (region.Generators.Contains(generator))
            region.Validity = GravityRegionState.Dirty;
    }

    public void ChangeGeneratorSize(MyGravityGenerator generator)
    {
        // Surely there's a better solution here
        // Could probably invalidate every region but the ones containing it if it's growing and invalidate all regions containing it if it's shrinking
        InvalidateAllRegions();
    }

    
    /// <summary>
    /// This technically doesn't invalidate any region but it has the same effect
    /// </summary>
    private void InvalidateAllRegions() 
    {
        allRegions.Clear();
        signatureToRegion.Clear();
    }

    /// <summary>
    /// Marks a region as invalid and stops it from being used in the future.
    /// </summary>
    private void InvalidateRegion(FieldRegion region)
    {
        region.Validity = GravityRegionState.Invalid;
        allRegions.Remove(region);
    }

    // Sample gravity at a voxel
    public Vector3D Sample(Vector3I voxel)
    {
        if (!voxelToRegion.TryGetValue(voxel, out var region))
        {
            // If it doesn't have a region, then we make one
            var generators = CollectGenerators(voxel);
            if (generators.Count == 0) return Vector3D.Zero;
            region = AssignVoxel(voxel, generators, _frame);
            return region.CachedGravity;
        }
        
        switch (region.Validity)
        {
            case GravityRegionState.Valid:                // Simply return the force of the valid region
                break;
            
            case GravityRegionState.Dirty:                // Recalculate the force, mark the region as valid again then return it
                region.Recalculate();
                break;

            case GravityRegionState.Invalid:              // Shit's fucked, check if a region exists for this signature, if not throw it out and get a new one
                var generators = CollectGenerators(voxel);
                region = AssignVoxel(voxel, generators, _frame);
                break;
            
        }
        return region.CachedGravity;
    }


    private readonly ThreadLocal<HashSet<MyGravityGeneratorBase>> _threadLocalGeneratorBuffer
        = new(() => []);

    /// <summary>
    /// Collects all generators covering a given voxel.
    /// WARNING: Returns a reused HashSet that is cleared on each call. 
    /// Copy it immediately if you need to keep it, e.g. 'new HashSet(CollectGenerators(voxel))'.
    /// Thread safe.
    /// </summary>
    private IReadOnlyCollection<MyGravityGeneratorBase> CollectGenerators(Vector3I voxel)
    {
        var generatorBuffer = _threadLocalGeneratorBuffer.Value;
        generatorBuffer.Clear();

        foreach (var generator in _generators)
        {
            if (GeneratorCoversVoxel(generator, voxel)) generatorBuffer.Add(generator);
        }

        return generatorBuffer;
    }

    private bool GeneratorCoversVoxel(MyGravityGenerator generator, Vector3I voxel)
    {
        var voxelf = (Vector3)voxel * _thisGrid.GridSize;
        var aabbmaybe = generator.GetBoundingBox();
        if (aabbmaybe != null)
        {
            var aabb = aabbmaybe.Value; // TODO: Replace grabbing the box directly with a lazily updated AABB tree instead
            return aabb.Contains(voxelf) == ContainmentType.Contains;
        }
        return false;
        
    }

    // Assign a voxel to a FieldRegion
    public FieldRegion AssignVoxel(Vector3I voxel, IReadOnlyCollection<MyGravityGeneratorBase> generators, long frame)
    {
        ulong signature = SignatureHash64(generators);
        if (!signatureToRegion.TryGetValue(signature, out var region))
        {
            region = new FieldRegion([..generators], frame, signature);
            allRegions.Add(region);
            signatureToRegion[signature] = region;
        }
        voxelToRegion[voxel] = region;
        return region;
    }

    // Simple signature: sorted generator IDs concatenated
    private ulong SignatureHash64(IReadOnlyCollection<MyGravityGeneratorBase> generators)
    {
        ulong hash = 14695981039346656037UL; // FNV offset
        foreach (var g in generators)
        {
            hash ^= (ulong)g.GetHashCode();
            hash *= 1099511628211UL; // FNV prime
        }
        return hash;
    }
    
}
