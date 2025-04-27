using System;
using System.Collections.Generic;
using NLog;
using NLog.Fluent;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using SpaceEngineers.Game.Entities.Blocks;
using VRage.Game.ModAPI;
using VRageMath;

namespace GDriveOptimizer;

public class GravityShip
{
    
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly List<MyGravityGeneratorBase> _gravityGenerators = [];
    private Dictionary<MyGravityGeneratorBase, ManagedGravityGenerator> _gravityGeneratorLookup = new();
    private MyCubeGrid _grid;
    private ForceData MasterForce;


    public GravityShip(MyCubeGrid grid)
    {
        _grid = grid;
    }

    public bool HasGenerator(MyGravityGeneratorBase gen)
    {
        return _gravityGeneratorLookup.ContainsKey(gen);
    }


    public void AddGenerator(MyGravityGeneratorBase gen)
    {
        var managed = new ManagedGravityGenerator(gen, this);
        _gravityGeneratorLookup.Add(gen, managed);
        _gravityGenerators.Add(gen);
        AddGravityGenerator(gen);
    }

    public void RemoveGenerator(MyGravityGeneratorBase gen)
    {
        _gravityGeneratorLookup.Remove(gen); // Remove the entry from the dictionary
        _gravityGenerators.Remove(gen);
        RemoveGravityGenerator(gen);
    }


    private List<MyVirtualMass> MassBlocks;
    private Dictionary<MyVirtualMass, ManagedMassBlock> MassBlocksLookup;

    public bool HasMassBlock(MyVirtualMass mass)
    {
        return MassBlocksLookup.ContainsKey(mass);
    }

    public void AddMassBlock(MyVirtualMass mass)
    {
        
        // TODO: Call from mass block init event function
        var managed = new ManagedMassBlock(mass, this);
        MassBlocksLookup.Add(mass, managed);
        MassBlocks.Add(mass);
    }

    public void RemoveMassBlock(MyVirtualMass mass)
    {
        // TODO: Call from mass block closed event function
        MassBlocksLookup.Remove(mass);
        MassBlocks.Remove(mass);
        Recalculate();
    }

    /// <summary>
    /// Manually tells all gravity generators to recalculate their force normals.
    /// </summary>
    /// <exception cref="NotImplementedException"></exception>
    public void Recalculate()
    {
        Log.Info("Recalculate");
    }

    
    
    // Gravity AABB stuff
    
    private static Dictionary<IMyGravityProvider, int> m_proxyIdMap = new Dictionary<IMyGravityProvider, int>();
    private GravityCollector m_gravityCollector; // Every single ship gets it's own AABB tree of generators
    private static MyDynamicAABBTreeD m_artificialGravityGenerators = new MyDynamicAABBTreeD(Vector3D.One * 10.0, 10.0);
    
    
    
    
    public Vector3 CalculateArtificialGravityWorldPoint(
        IMyCubeGrid grid,
        Vector3D worldPoint,
        float gravityMultiplier = 1f)
    {
        MatrixD inv = grid.WorldMatrixInvScaled;
        Vector3D localPoint = Vector3D.Transform(worldPoint, inv);
        
        Vector3 localGravity = CalculateArtificialGravityLocalPoint(localPoint, gravityMultiplier);
        
        return Vector3.Transform(localGravity, grid.WorldMatrix);
    }
    
    public Vector3 CalculateArtificialGravityLocalPoint(
        Vector3D localPoint,
        float gravityMultiplier = 1f)
    {
        if ((double) gravityMultiplier == 0.0)
            return Vector3.Zero;
        if (m_gravityCollector == null)
            m_gravityCollector = new GravityCollector();
        m_gravityCollector.Gravity = Vector3.Zero;
        m_gravityCollector.Collect(m_artificialGravityGenerators, ref localPoint);
        return m_gravityCollector.Gravity * gravityMultiplier;
    }
    
    
    
    private class GravityCollector
    {
        public Vector3 Gravity;
        private readonly Func<int, bool> CollectAction;
        private Vector3D WorldPoint;
        private MyDynamicAABBTreeD Tree;

        public GravityCollector() => this.CollectAction = new Func<int, bool>(this.CollectCallback);

        public void Collect(MyDynamicAABBTreeD tree, ref Vector3D worldPoint)
        {
            this.Tree = tree;
            this.WorldPoint = worldPoint;
            tree.QueryPoint(this.CollectAction, ref worldPoint);
        }

        private bool CollectCallback(int proxyId)
        {
            IMyGravityProvider userData = this.Tree.GetUserData<IMyGravityProvider>(proxyId);
            if (userData.IsWorking && userData.IsPositionInRange(this.WorldPoint))
                this.Gravity += userData.GetWorldGravity(this.WorldPoint);
            return true;
        }
    }
    
    
        public static BoundingBoxD GetGridLocalAABB(IMyGravityProvider gravityGenerator)
        {
            // Step 1: Get the world-space AABB
            BoundingBoxD worldAABB;
            gravityGenerator.GetProxyAABB(out worldAABB);

            // Step 2: Get the grid's inverse world matrix
            var block = gravityGenerator as IMyCubeBlock;
            if (block == null)
                throw new InvalidOperationException("Gravity provider is not a cube block");

            var grid = block.CubeGrid;
            MatrixD gridWorldMatrixInv = grid.WorldMatrixInvScaled;

            // Step 3: Transform AABB corners into grid-local space
            Vector3D[] corners = worldAABB.GetCorners();
            BoundingBoxD localAABB = BoundingBoxD.CreateInvalid(); // empty box

            foreach (var corner in corners)
            {
                Vector3D localCorner = Vector3D.Transform(corner, gridWorldMatrixInv);
                localAABB.Include(localCorner);
            }

            return localAABB;
        }
    
    
        public static void AddGravityGenerator(IMyGravityProvider gravityGenerator)
        {
          if (m_proxyIdMap.ContainsKey(gravityGenerator))
            return;
          BoundingBoxD aabb = GetGridLocalAABB(gravityGenerator);
          int num = m_artificialGravityGenerators.AddProxy(ref aabb, (object) gravityGenerator, 0U);
          m_proxyIdMap.Add(gravityGenerator, num);
        }
    
        public static void RemoveGravityGenerator(IMyGravityProvider gravityGenerator)
        {
          int proxyId;
          if (!m_proxyIdMap.TryGetValue(gravityGenerator, out proxyId))
            return;
          m_artificialGravityGenerators.RemoveProxy(proxyId);
          m_proxyIdMap.Remove(gravityGenerator);
        }
    
        public static void OnGravityGeneratorMoved(
          IMyGravityProvider gravityGenerator,
          ref Vector3 velocity)
        {
          int proxyId;
          if (!m_proxyIdMap.TryGetValue(gravityGenerator, out proxyId))
            return;
          BoundingBoxD aabb;
          gravityGenerator.GetProxyAABB(out aabb);
          m_artificialGravityGenerators.MoveProxy(proxyId, ref aabb, (Vector3D) velocity);
        }
    
    
    // TODO: REcalc on center of mass modification
}