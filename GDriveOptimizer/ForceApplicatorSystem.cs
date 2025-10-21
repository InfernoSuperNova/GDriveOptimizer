using System.Collections.Concurrent;
using Sandbox.Game.Entities;
using SpaceEngineers.Game.ModAPI;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRageMath;

namespace GDriveOptimizer;

public static class ForceApplicatorSystem
{


    private class GridForceHandler
    {
        public List<IMyVirtualMass> Masses;
        public MyCubeGrid Grid;
        public bool Valid;
        private (Vector3D, Vector3D) _cachedForce;
        private Dictionary<IMyVirtualMass, bool> _lastEnabled;

        public void AddMassBlock(IMyVirtualMass mass)
        {
            Masses.Add(mass);
            var cubeBlock = (MyCubeBlock)mass;
            _lastEnabled.Add(mass, !cubeBlock.IsWorking);
        } 

        public GridForceHandler(MyCubeGrid grid)
        {
            Masses = [];
            Grid = grid;
            Valid = false;
            _lastEnabled = [];
        }

        public void Update()
        {
            for (var index = Masses.Count - 1; index >= 0; index--)
            {
                var mass = Masses[index];
                var cubeBlock = (MyCubeBlock)mass;
                if (mass.CubeGrid != Grid)
                {
                    Masses.Remove(mass);
                    ForceApplicatorSystem.AddMassBlock(mass);
                }
                else if (mass.Closed)
                {
                    Masses.Remove(mass);
                }
                if (cubeBlock.IsWorking != _lastEnabled[mass]) Invalidate();
                _lastEnabled[mass] = cubeBlock.IsWorking;
            }
        }

        public (Vector3D, Vector3D) GetIntraForce()
        {
            if (Valid) return _cachedForce;

            var forces = new List<(Vector3D, Vector3D)>();

            foreach (var mass in Masses)
            {
                
                var pos = mass.Position * mass.CubeGrid.GridSize;
                var force = GravityManager.Sample(Grid, (Vector3I)pos) * mass.VirtualMass;
                forces.Add((force, pos));
            }

            _cachedForce = GetWeightedAveragePositionAndForce(forces);
            Valid = true;
            return _cachedForce;
        }

        public void Invalidate()
        {
            Valid = false;
        }
    }
    
    
    // TODO:
    // Fix jump bug
    // Add auto invalidation to old regions
    // Update handling for floating objects probably and astronauts
    // Implement sphericals
    // Remove physics components from shit that don't need them eg the player
    // Cache total force applied to a grid for intra grid actions and invalidate when mass block state changes or when region force is recalculated
    // Make sure it doesn't work in natural gravity
    private static Dictionary<MyCubeGrid, GridForceHandler> _gridForceHandlers = [];
    private static Dictionary<MyCubeGrid, GridForceHandler> _gridForceHandlersSwap = [];
    private static HashSet<IMyVirtualMass> _masses = [];
    private static HashSet<IMyVirtualMass> _massesSwap = [];

    public static void Setup()
    {
        VirtualMassInit_Patch.Trigger += AddMassBlock;
        SpaceBallInit_Patch.Trigger += AddMassBlock;
    }

    private static void AddMassBlock(IMyVirtualMass mass)
    {
        var grid = (MyCubeGrid)mass.CubeGrid;
        var handler = ValidateGridForceHandler(grid);
        handler.AddMassBlock(mass);
        _masses.Add(mass);
    }

    private static GridForceHandler ValidateGridForceHandler(MyCubeGrid grid)
    {
        if (!_gridForceHandlers.ContainsKey(grid)) _gridForceHandlers.Add(grid, new GridForceHandler(grid));
        return _gridForceHandlers[grid];
    }

    public static void InvalidateForGrid(MyCubeGrid grid)
    {
        _gridForceHandlers[grid].Invalidate();
    }
    
    
    
    public static void Update()
    {
        
        var temp = _gridForceHandlersSwap;
        temp.Clear();
        foreach (var field in _gridForceHandlers) if (!field.Key.Closed) temp.Add(field.Key, field.Value);
        _gridForceHandlersSwap = _gridForceHandlers;
        _gridForceHandlers = temp;
        foreach (var forceManager in _gridForceHandlers)
        {
            forceManager.Value.Update();
        }

        foreach (var forceManager in _gridForceHandlers)
        {
            var force = forceManager.Value.GetIntraForce();
            var grid = forceManager.Key;
            
            
            var transformedForce = Vector3D.TransformNormal(force.Item1, grid.WorldMatrix);
            var transformedPos = Vector3D.Transform(force.Item2, grid.WorldMatrix);
            
            if (transformedForce.IsZero()) continue;
            grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, transformedForce, transformedPos, null, null, false);
            
        }

        var temp2 = _massesSwap;
        temp2.Clear();
        foreach (var mass in _masses) if (!mass.Closed) temp2.Add(mass);
        _massesSwap = _masses;
        _masses = temp2;
        foreach (var block in _masses)
        {
            ApplyArtificialGravityToMassBlock(block);
        }
    }
    

    private static void ApplyArtificialGravityToMassBlock(IMyVirtualMass mass)
    {
        if (!mass.Physics.RigidBody.IsActive) return;
        if (!mass.IsWorking || mass.CubeGrid.IsStatic ||
            mass.CubeGrid.Physics.IsStatic) return;
        
        //float strengthMultiplier = MyGravityProviderSystem.CalculateArtificialGravityStrengthMultiplier(MyGravityProviderSystem.CalculateHighestNaturalGravityMultiplierInPoint(worldMatrix.Translation));
        var worldMatrix = mass.WorldMatrix;
        Vector3D pos = worldMatrix.Translation;
        var interGravity = GravityManager.Sample(pos, (MyCubeGrid)mass.CubeGrid);
        if (interGravity == Vector3D.Zero) return;
        
        Vector3 force = interGravity * mass.VirtualMass;
        
        
        Vector3? torque = new Vector3?();
        float? maxSpeed = new float?();
        
        mass.CubeGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, force, pos, torque, maxSpeed, false);
    }
    
    

    
    private static (Vector3D, Vector3D) GetWeightedAveragePositionAndForce(List<(Vector3D,Vector3D)> dataList)
    {
        
        Vector3D netForce = Vector3D.Zero;
        Vector3D netTorque = Vector3D.Zero; // Moment = r × F

        
        foreach (var (force, position) in dataList)
        {
            
            netForce += force;
            netTorque += Vector3D.Cross(position, force); // Torque = r × F
        }
        if (netForce.LengthSquared() == 0f)
        {
            return (Vector3D.Zero, Vector3D.Zero);
        }
        var rPerp = Vector3D.Cross(netForce, netTorque) 
                    / netForce.LengthSquared();

        return (netForce, rPerp);
    }
    
    
}