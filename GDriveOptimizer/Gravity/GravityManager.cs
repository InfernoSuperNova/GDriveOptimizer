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

namespace GDriveOptimizer.Gravity;


// -----------------------------
// Gravity Generator
// -----------------------------


public static class GravityManager
{
    #region PrivateStatics
    private static Dictionary<MyCubeGrid, SparseGravityField> _fields = new();
    private static Dictionary<MyCubeGrid, SparseGravityField> _fieldsSwap = new();
    private static MyDynamicAABBTreeD _generatorTree = new MyDynamicAABBTreeD();
    private static GravityCollector _collector = new GravityCollector();
    #endregion
    
    #region Setup
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
    #endregion
    
    #region PublicAPI
    /// <summary>
    /// Samples a world position on the AABB tree. 
    /// </summary>
    /// <param name="worldPos">The position to sample.</param>
    /// <param name="instigator">Optional grid to be passed to ignore this cubegrid when searching.</param>
    /// <param name="debug">Whether to log this sample action</param>
    /// <returns></returns>
    public static Vector3D Sample(Vector3D worldPos, MyCubeGrid? instigator = null, bool debug = false)
    {
        return Sample(worldPos, _generatorTree, instigator, debug);
    }


    public static Vector3D Sample(Vector3D worldPos, MyDynamicAABBTreeD tree, MyCubeGrid? instigator = null,
        bool debug = false)
    {
        if (debug) Log($"Sample pos: {worldPos}");
        _collector.Collect(tree, ref worldPos, instigator, debug);
        var fields = _collector.Fields;
        
        Vector3D force = default;
        foreach (var (field, grid) in fields)
        {
            if (debug) Log($"Sampling field {grid.DisplayName}");
            var referenceWorldPos = grid.WorldMatrix.Translation;
            var worldDirection = worldPos - referenceWorldPos;
            var gridLocal = (Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(grid.WorldMatrix)));
            var box = field.LocalAABB;
            if (box.Contains(gridLocal) != ContainmentType.Contains) continue;
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

    public static List<(SparseGravityField, MyCubeGrid)> GetFieldsIntersectingWithGrid(MyCubeGrid grid)
    {
        _fields.TryGetValue(grid, out var field);
        _collector.Intersect(_generatorTree, grid, field);
        return _collector.Fields;
    }
    
    #endregion
    
    #region EventHandlers
    
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
    
    #endregion
    
    #region Helpers
    
    private static SparseGravityField ValidateGridField(MyCubeGrid grid)
    {
        if (!_fields.ContainsKey(grid)) _fields.Add(grid, new SparseGravityField(grid));
        return _fields[grid];
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
    private static MyDynamicAABBTreeD GenerateAABBTree()
    {
        MyDynamicAABBTreeD gravityFieldTree = new MyDynamicAABBTreeD();
        foreach (var field in _fields)
        {
            var aabb = field.Value.WorldAABB;
            gravityFieldTree.AddProxy(ref aabb, (object)(field.Value, field.Key), 0U);
        }
        return gravityFieldTree;
    }
    private static void Log(object log)
    {
        Plugin.Log.Info(log);
    }
    #endregion
    
    #region Classes
    private class GravityCollector
    {
        public List<(SparseGravityField, MyCubeGrid)> Fields = new List<(SparseGravityField, MyCubeGrid)>();
        private readonly Func<int, bool> CollectAction;
        private readonly Func<int, bool> IntersectAction;
        private MyDynamicAABBTreeD Tree;

        public GravityCollector()
        {
            CollectAction = CollectCallback;
            IntersectAction = IntersectCallback;
        }


        private MyCubeGrid? _lastInstigator;
        private BoundingBoxD _lastWorldBox;
        private bool _lastDebug;
        private SparseGravityField? _lastField;

        public void Collect(MyDynamicAABBTreeD tree, ref Vector3D worldPoint, MyCubeGrid? instigator = null, bool debug = false)
        {
            Fields.Clear();
            _lastInstigator = instigator;
            _lastDebug = debug;
            Tree = tree;
            tree.QueryPoint(CollectAction, ref worldPoint);
        }

        private bool CollectCallback(int proxyId)
        {
            (SparseGravityField, MyCubeGrid) userData = Tree.GetUserData<(SparseGravityField, MyCubeGrid)>(proxyId);
            if (_lastInstigator != null && _lastInstigator == userData.Item2) return true;
            if (_lastDebug) Log($"Collected gravity field {userData}");
            Fields.Add(userData);
            return true;
        }

        public void Intersect(MyDynamicAABBTreeD tree, MyCubeGrid instigator, SparseGravityField? field, bool debug = false)
        {
            Fields.Clear();
            _lastInstigator = instigator;
            _lastDebug = debug;
            _lastField = field;
            Tree = tree;

            var box = instigator.PositionComp.WorldAABB;
            tree.Query(IntersectAction, ref box);
        }
        
        private bool IntersectCallback(int proxyId)
        {
            (SparseGravityField, MyCubeGrid) userData = Tree.GetUserData<(SparseGravityField, MyCubeGrid)>(proxyId);
            if (_lastField != null && userData.Item1.WorldAABB == _lastField.WorldAABB) return true;
            // If we make it here then there's another grid that we should test on
            Fields.Add(userData);

            return true;
        }
    }
    #endregion
}