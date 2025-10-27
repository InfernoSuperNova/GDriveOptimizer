
using Sandbox.Game.Entities;
using SpaceEngineers.Game.Entities.Blocks;
using VRage.Collections;
using VRageMath;

namespace GDriveOptimizer.Gravity;


// -----------------------------
// Sparse Gravity Field. This is instantiated once per grid and contains and handles. everything to do with gravity generators on that grid.
// -----------------------------
public class SparseGravityField
{
    
    // Region mapping
    private readonly Dictionary<Vector3I, FieldRegion> _voxelToRegion = new();
    private readonly Dictionary<ulong, FieldRegion> _signatureToRegion = new();
    private readonly List<FieldRegion> _allRegions = [];

    // Generator lists and buffers
    private readonly MyConcurrentHashSet<MyGravityGeneratorBase> _generators = [];
    private readonly ThreadLocal<HashSet<MyGravityGeneratorBase>> _threadLocalGeneratorBuffer
        = new(() => []);
    
    // Grid and field AABB
    private readonly MyCubeGrid _thisGrid;
    private BoundingBox _localAABB;
    private bool _aabbValid;
    private MyOrientedBoundingBoxD _orientedAABB;
    private BoundingBoxD _worldAABB;
    private long _frame; // Todo, check age of regions and delete them from memory if they haven't been touched in a long time
    
    
    // Properties
    public BoundingBox LocalAABB
    {
        get
        {
            if (!_aabbValid) RecalcBoundingBox();
            return _localAABB;
        }
    }
    public MyOrientedBoundingBoxD OrientedAABB
    {
        get
        {
            if (!_aabbValid) RecalcBoundingBox();
            return _orientedAABB;
        }
    }
    public BoundingBoxD WorldAABB
    {
        get
        {
            if (!_aabbValid) RecalcBoundingBox();
            return _worldAABB;
        }
    }
    
    // Ctor
    public SparseGravityField(MyCubeGrid grid)
    {
        _thisGrid = grid;
    }
    
    // API
    public void Update(long frame)
    {
        _frame = frame;
        InvalidateBoundingBox();
    }
    public void AddGenerator(MyGravityGeneratorBase generator)
    {
        _generators.Add(generator);
        InvalidateAllRegions();
        ForceApplicatorSystem.InvalidateCachedIntraForce(_thisGrid);
    }
    public void RemoveGenerator(MyGravityGeneratorBase generator)
    {
        _generators.Remove(generator);
        foreach (var region in _allRegions)
            if (region.Generators.Contains(generator))
                region.Validity = GravityRegionState.Invalid;
        ForceApplicatorSystem.InvalidateCachedIntraForce(_thisGrid);
    }
    public void ChangeGeneratorStrength(MyGravityGeneratorBase generator)
    {
        if (!generator.IsFunctional) return; // Don't need to recalculate if the generator is turned off anyway
        foreach (var region in _allRegions) if (region.Generators.Contains(generator))
            region.Validity = GravityRegionState.Dirty;
        ForceApplicatorSystem.InvalidateCachedIntraForce(_thisGrid);
    }
    public void ToggleGenerator(MyGravityGeneratorBase generator)
    {
        foreach (var region in _allRegions) if (region.Generators.Contains(generator))
            region.Validity = GravityRegionState.Dirty;
        ForceApplicatorSystem.InvalidateCachedIntraForce(_thisGrid);
    }
    public void ChangeGeneratorSize(MyGravityGeneratorBase generator)
    {
        // Surely there's a better solution here
        // Could probably invalidate every region but the ones containing it if it's growing and invalidate all regions containing it if it's shrinking
        InvalidateAllRegions();
        ForceApplicatorSystem.InvalidateCachedIntraForce(_thisGrid);
    }
    public void InvalidateBoundingBox() => _aabbValid = false;
    
    // Sample API
    public Vector3D Sample(Vector3D gridLocal, bool debug)
    {
        var voxel = (Vector3I)gridLocal;
        
        
        if (debug) Log($"Sampling in grid {_thisGrid.DisplayName} voxel {gridLocal}");
        if (!_voxelToRegion.TryGetValue(voxel, out var region))
        {
            if (debug) Log($"No voxel found at this position, searching for existing one");
            // If it doesn't have a region, then we make one
            var generators = CollectGenerators(voxel, debug); // TODO: Perhaps assign blank regions to a special case
            if (generators.Count == 0) return Vector3D.Zero;
            region = AssignVoxel(voxel, generators, _frame, debug);
            return region.CachedGravity + region.GetSphericalGravity(gridLocal);
        }
        if (debug) Log($"Voxel found at this position, checking...");
        switch (region.Validity)
        {
            case GravityRegionState.Valid:                // Simply return the force of the valid region
                if (debug) Log($"... Valid");
                break;
            
            case GravityRegionState.Dirty:                // Recalculate the force, mark the region as valid again then return it
                region.Recalculate();
                if (debug) Log($"... Dirty, recalculating");
                break;

            case GravityRegionState.Invalid:              // Shit's fucked, check if a region exists for this signature, if not throw it out and get a new one
                var generators = CollectGenerators(voxel, debug);
                region = AssignVoxel(voxel, generators, _frame, debug);
                if (debug) Log($"... Invalid, reevaluating");
                break;
            
        }
        return region.CachedGravity + region.GetSphericalGravity(gridLocal);
    }

    // Helpers
    private void RecalcBoundingBox()
    {
        if (_generators.Count == 0)
        {
            _localAABB = new BoundingBox();
            return;
        }

        var generator1 = _generators.First();
        
        
        var box = GetGeneratorBox(generator1);
        foreach (var generator in _generators)
        {
            var otherBox = GetGeneratorBox(generator);
            box.Include(otherBox);
            box = BoundingBox.CreateMerged(box, otherBox);
        }
        _localAABB = box;
        
        
        MatrixD matrix = _thisGrid.WorldMatrix;
        Vector3D translation = Vector3D.Transform(box.Center, matrix);
        Quaternion fromRotationMatrix = Quaternion.CreateFromRotationMatrix(in matrix);
        _orientedAABB = new MyOrientedBoundingBoxD(translation, box.HalfExtents, fromRotationMatrix);
        _worldAABB = _orientedAABB.GetAABB();
    }
    
    private static BoundingBox GetGeneratorBox(MyGravityGeneratorBase generator) // Cache this probably
    {
        var offset = new Vector3(2.5, 2.5, 2.5);
        if (generator is MyGravityGeneratorSphere s)
        {
            return new BoundingBox(generator.Position * generator.CubeGrid.GridSize - s.Radius / 2 - offset,
                generator.Position * generator.CubeGrid.GridSize + s.Radius / 2 + offset);
        }
        
        var l = generator as MyGravityGenerator;
        return new BoundingBox(generator.Position * generator.CubeGrid.GridSize - l.FieldSize / 2 - offset,
            generator.Position * generator.CubeGrid.GridSize + l.FieldSize / 2 + offset);
    }
    
    
    /// <summary>
    /// This technically doesn't invalidate any region but it has the same effect
    /// </summary>
    private void InvalidateAllRegions() 
    {
        _voxelToRegion.Clear();
        _allRegions.Clear();
        _signatureToRegion.Clear();
    }

    /// <summary>
    /// Marks a region as invalid and stops it from being used in the future.
    /// </summary>
    private void InvalidateRegion(FieldRegion region)
    {
        region.Validity = GravityRegionState.Invalid;
        _allRegions.Remove(region);
    }
    
    // Assign a voxel to a FieldRegion
    private FieldRegion AssignVoxel(Vector3I voxel, IReadOnlyCollection<MyGravityGeneratorBase> generators, long frame, bool debug)
    {
        ulong signature = SignatureHash64(generators);
        if (debug) Log($"Assigning voxel, signature is {signature}");
        if (!_signatureToRegion.TryGetValue(signature, out var region))
        {
            if (debug) Log($"Region does not exist, generating a new one matching signature");
            region = new FieldRegion(new HashSet<MyGravityGeneratorBase>(generators), frame, signature);
            _allRegions.Add(region);
            _signatureToRegion[signature] = region;
        }
        if (debug) Log($"Assigned region for voxel");
        _voxelToRegion[voxel] = region;
        return region;
    }
    
    /// <summary>
    /// Collects all generators covering a given voxel.
    /// WARNING: Returns a reused HashSet that is cleared on each call. 
    /// Copy it immediately if you need to keep it, e.g. 'new HashSet(CollectGenerators(voxel))'.
    /// Thread safe.
    /// </summary>
    private IReadOnlyCollection<MyGravityGeneratorBase> CollectGenerators(Vector3I voxel, bool debug)
    {
        var generatorBuffer = //_threadLocalGeneratorBuffer.Value;
            new HashSet<MyGravityGeneratorBase>(); // TODO: Debug why using the thread local hashset crashes when a character polls while spawning a grid
        generatorBuffer.Clear();

        foreach (var generator in _generators)
        {
            if (GeneratorCoversVoxel(generator, voxel)) generatorBuffer.Add(generator);
        }

        if (debug) Log($"Found generators covering voxel: {generatorBuffer.Count}");
        return generatorBuffer;
    }

    private bool GeneratorCoversVoxel(MyGravityGeneratorBase generator, Vector3I voxel)
    {
        var voxelf = (Vector3)voxel;
        var aabb = GetGeneratorBox(generator);
        return aabb.Contains(voxelf) == ContainmentType.Contains;
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
    
    // Debug
    private void Log(object log)
    {
        Plugin.Log.Info(log);
    }


    








}