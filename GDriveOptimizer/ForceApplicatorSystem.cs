using System.Collections.Concurrent;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using SpaceEngineers.Game.ModAPI;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace GDriveOptimizer;

public static class ForceApplicatorSystem
{
    
    private class GridForceHandler
    {
        
        private static readonly FieldInfo JumpTimeLeftField =
            typeof(MyGridJumpDriveSystem).GetField("m_jumpTimeLeft", BindingFlags.NonPublic | BindingFlags.Instance);
        
        public List<IMyVirtualMass> Masses;
        public MyCubeGrid Grid;
        public bool Valid;
        public bool JustJumped;
        public bool WillJump;
        private (Vector3D, Vector3D) _cachedForce;
        private Dictionary<IMyVirtualMass, bool> _lastEnabled;
        internal float StrengthMultiplier = 1;

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
            JustJumped = WillJump;
            WillJump = IsAboutToJump();
            StrengthMultiplier = Config.I.AllowArtificialGravityInNaturalGravity ? 1 : MyGravityProviderSystem.CalculateArtificialGravityStrengthMultiplier(MyGravityProviderSystem.CalculateHighestNaturalGravityMultiplierInPoint(Grid.WorldMatrix.Translation));
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
                if (!mass.IsWorking) continue;
                var pos = mass.Position * mass.CubeGrid.GridSize;
                var force = GravityManager.Sample(Grid, pos) * mass.VirtualMass;
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
        
        private bool IsAboutToJump()
        {
            var jumpSystem = Grid.GridSystems?.JumpSystem;
            if (jumpSystem == null) return false;

            var timeLeft = (float)JumpTimeLeftField?.GetValue(jumpSystem);
            
            return timeLeft is > 0 and < 0.016666666f;
        }
    }
    
    private static Dictionary<MyCubeGrid, GridForceHandler> _gridForceHandlers = [];
    private static Dictionary<MyCubeGrid, GridForceHandler> _gridForceHandlersSwap = [];
    private static HashSet<IMyVirtualMass> _masses = [];
    private static HashSet<IMyVirtualMass> _massesSwap = [];

    public static void Setup()
    {
        VirtualMassPatches.Init += AddMassBlock;
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

    public static void InvalidateCachedIntraForce(MyCubeGrid grid)
    {
        if (!_gridForceHandlers.ContainsKey(grid))
        {
            //Plugin.Log.Warn($"ForceApplicatorSystem: Tried to invalidate grid {grid.DisplayName}, does not exist!?!?");
            return;
        }
        _gridForceHandlers[grid].Invalidate();
    }
    
    
    
    public static void Update()
    {
        
        var temp = _gridForceHandlersSwap;
        temp.Clear();
        foreach (var field in _gridForceHandlers) if (!field.Key.Closed) temp.Add(field.Key, field.Value);
        _gridForceHandlersSwap = _gridForceHandlers;
        _gridForceHandlers = temp;
        foreach (var forceManager in _gridForceHandlers.Values.ToList()) // Yes yes, I went to all the effort to reuse lists... then just did this dumb hack. Only because forceManager.Update can mutate the collection
        {
            forceManager.Update();
        }

        foreach (var forceManager in _gridForceHandlers)
        {
            if (Config.I.FixJumpBug && forceManager.Value.JustJumped) continue; // Prevents force from being performed two galaxies away causing a huge jerk in the ship shredding subgrids
            var force = forceManager.Value.GetIntraForce();
            var grid = forceManager.Key;
            
            
            var transformedForce = Vector3D.TransformNormal(force.Item1 * forceManager.Value.StrengthMultiplier, grid.WorldMatrix);
            var transformedPos = Vector3D.Transform(force.Item2, grid.WorldMatrix);
            
            if (transformedForce.IsZero()) continue;
            if (Config.I.ApplyForceAtCenterOfMass) transformedPos = grid.Physics.CenterOfMassWorld;
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
        //if (!mass.Physics.RigidBody.IsActive) return;
        if (!mass.IsWorking || mass.CubeGrid.IsStatic ||
            mass.CubeGrid.Physics.IsStatic) return;
        
        var worldMatrix = mass.WorldMatrix;
        Vector3D pos = worldMatrix.Translation;
        var interGravity = GravityManager.Sample(pos, (MyCubeGrid)mass.CubeGrid);
        if (interGravity == Vector3D.Zero) return;
        
        Vector3 force = interGravity * mass.VirtualMass * _gridForceHandlers[(MyCubeGrid)mass.CubeGrid].StrengthMultiplier;
        
        
        Vector3? torque = new Vector3?();
        float? maxSpeed = new float?();
        if (Config.I.ApplyForceAtCenterOfMass) pos = mass.CubeGrid.Physics.CenterOfMassWorld;
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