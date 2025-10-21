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

    /// <summary>
    /// Samples a world position on the AABB tree. 
    /// </summary>
    /// <param name="worldPos">The position to sample.</param>
    /// <param name="instigator">Optional grid to be passed to ignore this cubegrid when searching.</param>
    /// <returns></returns>
    public static Vector3D Sample(Vector3D worldPos, MyCubeGrid? instigator = null)
    {
        // Temp testing
        var collector = new GravityCollector();
        collector.Collect(_generatorTree, ref worldPos, instigator);
        var fields = collector.Fields;
        
        Vector3D force = default;
        foreach (var (field, grid) in fields)
        {
            var referenceWorldPos = grid.WorldMatrix.Translation;

            var worldDirection = worldPos - referenceWorldPos;
            var gridLocal = (Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(grid.WorldMatrix)));

            var box = field.LocalAABB;
            if (box.Contains(gridLocal) != ContainmentType.Contains) continue;
            
            var nonTransformed = field.Sample((Vector3I)gridLocal);
            force += Vector3D.TransformNormal(nonTransformed, grid.WorldMatrix);
        }
        
        return force;
    }

    public static Vector3D Sample(MyCubeGrid grid, Vector3I pos)
    {
        var field = ValidateGridField(grid);
        return field.Sample(pos);
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
    
    
    private class GravityCollector
    {
        public List<(SparseGravityField, MyCubeGrid)> Fields = new List<(SparseGravityField, MyCubeGrid)>();
        private readonly Func<int, bool> CollectAction;
        private Vector3D WorldPoint;
        private MyDynamicAABBTreeD Tree;

        public GravityCollector() => this.CollectAction = new Func<int, bool>(this.CollectCallback);



        private MyCubeGrid? _lastInstigator;
        public void Collect(MyDynamicAABBTreeD tree, ref Vector3D worldPoint, MyCubeGrid? instigator = null)
        {
            _lastInstigator = instigator;
            this.Tree = tree;
            this.WorldPoint = worldPoint;
            tree.QueryPoint(this.CollectAction, ref worldPoint);
        }

        private bool CollectCallback(int proxyId)
        {
            (SparseGravityField, MyCubeGrid) userData = this.Tree.GetUserData<(SparseGravityField, MyCubeGrid)>(proxyId);
            if (_lastInstigator != null && _lastInstigator == userData.Item2) return true;
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
    private static void OnFieldSizeChanged(MyGravityGenerator gen, Vector3 newSize)
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
        if (gen is MyGravityGenerator genFull)
        {
            field.ChangeGeneratorStrength(genFull);
        }
    }
    private static void OnGravityGeneratorFunctionalityChanged(MyGravityGeneratorBase gen, bool isFunctional)
    {
        var field = ValidateGridField(gen.CubeGrid);
        if (gen is MyGravityGenerator genFull)
        {
            field.ToggleGenerator(genFull);
        }
    }
    private static void OnGravityGeneratorCreated(MyGravityGeneratorBase gen, MyObjectBuilder_CubeBlock _, MyCubeGrid grid)
    {
        try
        {
            if (gen is MyGravityGenerator genFull)
            {
                AddGravityGeneratorToGrid(grid, genFull);
            }
                
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"Exception in OnGravityGeneratorCreated: {e}");
        }
        
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
        {
            RemoveGravityGeneratorFromGrid(gen.CubeGrid, genFull);
        }
        //DebugAll();
    }

    private static void DebugAll()
    {
        foreach (var field in _fields)
        {
            Plugin.Log.Info($"{field.Key.DisplayName}");
            field.Value.DebugGravityGenerators();
        }
    }
    
    
    


    private static void AddGravityGeneratorToGrid(MyCubeGrid grid, MyGravityGenerator gen)
    {
        var field = ValidateGridField(grid);
        field.AddGenerator(gen);
        field.InvalidateBoundingBox();
    }

    private static void RemoveGravityGeneratorFromGrid(MyCubeGrid grid, MyGravityGenerator gen)
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

            // 'Down' in grid space
            Vector3I downLocal = -Base6Directions.GetIntVector(gen.Orientation.Up);

            // Convert to Vector3 (grid-space direction)
            Vector3 downVector = new Vector3(downLocal);

            // Multiply by gravity strength
            total += downVector * gen.GravityAcceleration;
        }

        var was = CachedGravity;
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

    private BoundingBox m_localAABB;
    private bool m_localAABBValid;

    public BoundingBox LocalAABB
    {
        get
        {
            if (!m_localAABBValid)
            {
                RecalcBoundingBox();
                m_localAABBValid = true;
            }

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

        var generator1 = _generators.FirstElement();
        
        
        var box = GetGeneratorBox(generator1);
        foreach (var generator in _generators)
        {
            var otherBox = GetGeneratorBox(generator);
            box.Include(otherBox);
            box = BoundingBox.CreateMerged(box, otherBox);
        }
        m_localAABB = box;
    }

    private static BoundingBox GetGeneratorBox(MyGravityGenerator generator) // Cache this probably
    {
        var offset = new Vector3(2.5, 2.5, 2.5);
        return new BoundingBox(generator.Position * generator.CubeGrid.GridSize - generator.FieldSize / 2 - offset,
            generator.Position * generator.CubeGrid.GridSize + generator.FieldSize / 2 + offset);
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

    // Sample gravity at a voxel
    public Vector3D Sample(Vector3I voxel)
    {
        if (!voxelToRegion.TryGetValue(voxel, out var region))
        {
            ForceApplicatorSystem.InvalidateForGrid(_thisGrid);
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
                ForceApplicatorSystem.InvalidateForGrid(_thisGrid);
                region.Recalculate();
                break;

            case GravityRegionState.Invalid:              // Shit's fucked, check if a region exists for this signature, if not throw it out and get a new one
                ForceApplicatorSystem.InvalidateForGrid(_thisGrid);
                var generators = CollectGenerators(voxel);
                region = AssignVoxel(voxel, generators, _frame);
                break;
            
        }
        return region.CachedGravity;
    }


    private readonly ThreadLocal<HashSet<MyGravityGeneratorBase>> _threadLocalGeneratorBuffer
        = new(() => new HashSet<MyGravityGeneratorBase>());

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
        var voxelf = (Vector3)voxel;
        var aabb = GetGeneratorBox(generator);
        return aabb.Contains(voxelf) == ContainmentType.Contains;
    }

    // Assign a voxel to a FieldRegion
    public FieldRegion AssignVoxel(Vector3I voxel, IReadOnlyCollection<MyGravityGeneratorBase> generators, long frame)
    {
        ulong signature = SignatureHash64(generators);
        if (!signatureToRegion.TryGetValue(signature, out var region))
        {
            region = new FieldRegion(new HashSet<MyGravityGeneratorBase>(generators), frame, signature);
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

    public void InvalidateBoundingBox()
    {
        m_localAABBValid = false;
    }
}
