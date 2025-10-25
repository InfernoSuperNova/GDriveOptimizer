using Havok;
using Havok.Utils;
using Sandbox.Game.Entities;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRageMath;
using NLog;
using Sandbox.Game.Entities.Cube;
using Torch.Server.Annotations;
using VRage.Collections;

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
        GravityGeneratorBasePatches.Init += OnGravityGeneratorCreated;
        GravityGeneratorBasePatches.WorkingChanged += OnGravityGeneratorFunctionalityChanged;
        GravityGeneratorBasePatches.Closed += OnGravityGeneratorDeleted;
        GravityGeneratorBasePatches.GridChanged += OnGravityGeneratorChangedGrid;
        Plugin.UpdateEvent += Update;
    }

    /// <summary>
    /// Samples a world position on the AABB tree. 
    /// </summary>
    /// <param name="worldPos">The position to sample.</param>
    /// <param name="instigator">Optional grid to be passed to ignore this cubegrid when searching.</param>
    /// <param name="debug">Whether to log this sample action</param>
    /// <returns></returns>
    public static Vector3D Sample(Vector3D worldPos, MyCubeGrid? instigator = null, bool debug = false)
    {
        if (debug) Log($"Sample pos: {worldPos}");
        var collector = new GravityCollector(); // TODO: Make this reused
        collector.Collect(_generatorTree, ref worldPos, instigator, debug);
        var fields = collector.Fields;
        
        Vector3D force = default;
        foreach (var (field, grid) in fields)
        {
            if (debug) Log($"Sampling field {grid.DisplayName}");
            var referenceWorldPos = grid.WorldMatrix.Translation;
            if (debug) Log($"Got the reference world pos");
            
            var worldDirection = worldPos - referenceWorldPos;
            var gridLocal = (Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(grid.WorldMatrix)));
            if (debug) Log($"Got grid local");
            var box = field.LocalAABB;
            if (box.Contains(gridLocal) != ContainmentType.Contains) continue;
            if (debug) Log($"Passed box local check");
            var nonTransformed = field.Sample(gridLocal, debug);
            force += Vector3D.TransformNormal(nonTransformed, grid.WorldMatrix);
        }
        if (debug) Log($"Sample good");
        return force;
    }



    public static Vector3D Sample(MyCubeGrid grid, Vector3D pos)
    {
        var field = ValidateGridField(grid);
        return field.Sample(pos, false);
    }
    
    
    private static Dictionary<MyCubeGrid, SparseGravityField> _fields = new();
    private static Dictionary<MyCubeGrid, SparseGravityField> _fieldsSwap = new();
    private static MyDynamicAABBTreeD _generatorTree = new MyDynamicAABBTreeD();
    
    private static MyDynamicAABBTreeD GenerateAABBTree()
    {
        MyDynamicAABBTreeD gravityFieldTree = new MyDynamicAABBTreeD();
        foreach (var field in _fields)
        {
            var aabb = field.Value.GetProxyAABB();
            gravityFieldTree.AddProxy(ref aabb, (object)(field.Value, field.Key), 0U);
        }
        return gravityFieldTree;
    }
    
    private static void Log(object log)
    {
        Plugin.Log.Info(log);
    }
    
    private class GravityCollector
    {
        public List<(SparseGravityField, MyCubeGrid)> Fields = new List<(SparseGravityField, MyCubeGrid)>();
        private readonly Func<int, bool> CollectAction;
        private Vector3D WorldPoint;
        private MyDynamicAABBTreeD Tree;

        public GravityCollector() => this.CollectAction = new Func<int, bool>(this.CollectCallback);



        private MyCubeGrid? _lastInstigator;
        private bool _lastDebug;
        public void Collect(MyDynamicAABBTreeD tree, ref Vector3D worldPoint, MyCubeGrid? instigator = null, bool debug = false)
        {
            _lastInstigator = instigator;
            _lastDebug = debug;
            this.Tree = tree;
            this.WorldPoint = worldPoint;
            tree.QueryPoint(this.CollectAction, ref worldPoint);
        }

        private bool CollectCallback(int proxyId)
        {
            (SparseGravityField, MyCubeGrid) userData = this.Tree.GetUserData<(SparseGravityField, MyCubeGrid)>(proxyId);
            if (_lastInstigator != null && _lastInstigator == userData.Item2) return true;
            if (_lastDebug) Log($"Collected gravity field {userData}");
            Fields.Add(userData);
            return true;
        }
    }

    
    private static void Update(long frame)
    {
        _generatorTree = GenerateAABBTree();

        var temp = _fieldsSwap;
        temp.Clear();
        foreach (var field in _fields) if (!field.Key.Closed) temp.Add(field.Key, field.Value);
        _fieldsSwap = _fields;
        _fields = temp;
        foreach (var field in _fields) field.Value.Update(frame);
        
        
        ForceApplicatorSystem.Update();
    }
    private static void OnFieldSizeChanged(MyGravityGeneratorBase gen, Vector3 newSize)
    {
        var field = ValidateGridField(gen.CubeGrid);
        {
            field.ChangeGeneratorSize(gen);
            field.InvalidateBoundingBox();
        }
    }
    private static void OnGravityAccelerationChanged(MyGravityGeneratorBase gen, float newStrength)
    {
        var field = ValidateGridField(gen.CubeGrid);
        field.ChangeGeneratorStrength(gen);
        
    }
    private static void OnGravityGeneratorFunctionalityChanged(MyGravityGeneratorBase gen, bool isFunctional)
    {
        var field = ValidateGridField(gen.CubeGrid);
        field.ToggleGenerator(gen);

    }
    private static void OnGravityGeneratorCreated(MyGravityGeneratorBase gen, MyObjectBuilder_CubeBlock _, MyCubeGrid grid)
    {
        try
        {
            AddGravityGeneratorToGrid(grid, gen);
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"Exception in OnGravityGeneratorCreated: {e}");
        }
        
    }
    private static void OnGravityGeneratorChangedGrid(MyGravityGeneratorBase gen, MyCubeGrid oldGrid, MyCubeGrid newGrid)
    {
            RemoveGravityGeneratorFromGrid(oldGrid, gen);
            AddGravityGeneratorToGrid(newGrid, gen);
        
    }
    private static void OnGravityGeneratorDeleted(MyGravityGeneratorBase gen)
    {
        RemoveGravityGeneratorFromGrid(gen.CubeGrid, gen);
    }

    private static void DebugAll()
    {
        foreach (var field in _fields)
        {
            Plugin.Log.Info($"{field.Key.DisplayName}");
            field.Value.DebugGravityGenerators();
        }
    }
    private static void AddGravityGeneratorToGrid(MyCubeGrid grid, MyGravityGeneratorBase gen)
    {
        var field = ValidateGridField(grid);
        field.AddGenerator(gen);
        field.InvalidateBoundingBox();
    }

    private static void RemoveGravityGeneratorFromGrid(MyCubeGrid grid, MyGravityGeneratorBase gen)
    {
        var field = ValidateGridField(grid);
        field.RemoveGenerator(gen);
        field.InvalidateBoundingBox();
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
            var to = (spherical.Position - pos * spherical.CubeGrid.GridSize);
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

    private MyConcurrentHashSet<MyGravityGeneratorBase> _generators = new();

    private MyCubeGrid _thisGrid;

    private BoundingBox m_localAABB;
    private bool m_localAABBValid;

    public BoundingBox LocalAABB
    {
        get
        {
            if (m_localAABBValid) return m_localAABB;
            RecalcBoundingBox();
            m_localAABBValid = true;

            return m_localAABB;
        }
        
    }
    public void DebugGravityGenerators()
    {
        Plugin.Log.Info($"Gen count: {_generators.Count}");
    }
    public SparseGravityField(MyCubeGrid grid)
    {
        _thisGrid = grid;
    }
    
    private long _frame = 0;// Todo, check age of regions and delete them from memory if they haven't been touched in a long time
    public void Update(long frame)
    {
        _frame = frame;
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
        foreach (var region in allRegions)
            if (region.Generators.Contains(generator))
                region.Validity = GravityRegionState.Invalid;
        ForceApplicatorSystem.InvalidateCachedIntraForce(_thisGrid);
    }
    
    
    public void ChangeGeneratorStrength(MyGravityGeneratorBase generator)
    {
        if (!generator.IsFunctional) return; // Don't need to recalculate if the generator is turned off anyway
        foreach (var region in allRegions) if (region.Generators.Contains(generator))
            region.Validity = GravityRegionState.Dirty;
        ForceApplicatorSystem.InvalidateCachedIntraForce(_thisGrid);
    }

    public void ToggleGenerator(MyGravityGeneratorBase generator)
    {
        foreach (var region in allRegions) if (region.Generators.Contains(generator))
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
    
    public BoundingBoxD GetProxyAABB()
    {
        var box = LocalAABB;
        
        MatrixD matrix = _thisGrid.WorldMatrix;
        Vector3D translation = Vector3D.Transform(box.Center, matrix);
        Quaternion fromRotationMatrix = Quaternion.CreateFromRotationMatrix(in matrix);
        MyOrientedBoundingBoxD local = new MyOrientedBoundingBoxD(translation, box.HalfExtents, fromRotationMatrix);
        return local.GetAABB();
    }

    public void RecalcBoundingBox()
    {
        if (_generators.Count == 0)
        {
            m_localAABB = new BoundingBox();
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
        m_localAABB = box;
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
        voxelToRegion.Clear();
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

    private void Log(object log)
    {
        Plugin.Log.Info(log);
    }
    
    // Sample gravity at a voxel
    public Vector3D Sample(Vector3D gridLocal, bool debug)
    {
        var voxel = (Vector3I)gridLocal;
        
        
        if (debug) Log($"Sampling in grid {_thisGrid.DisplayName} voxel {gridLocal}");
        if (!voxelToRegion.TryGetValue(voxel, out var region))
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


    private readonly ThreadLocal<HashSet<MyGravityGeneratorBase>> _threadLocalGeneratorBuffer
        = new(() => new HashSet<MyGravityGeneratorBase>());

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

    // Assign a voxel to a FieldRegion
    public FieldRegion AssignVoxel(Vector3I voxel, IReadOnlyCollection<MyGravityGeneratorBase> generators, long frame, bool debug)
    {
        ulong signature = SignatureHash64(generators);
        if (debug) Log($"Assigning voxel, signature is {signature}");
        if (!signatureToRegion.TryGetValue(signature, out var region))
        {
            if (debug) Log($"Region does not exist, generating a new one matching signature");
            region = new FieldRegion(new HashSet<MyGravityGeneratorBase>(generators), frame, signature);
            allRegions.Add(region);
            signatureToRegion[signature] = region;
        }
        if (debug) Log($"Assigned region for voxel");
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

    public void InvalidateBoundingBox()
    {
        m_localAABBValid = false;
    }
}
