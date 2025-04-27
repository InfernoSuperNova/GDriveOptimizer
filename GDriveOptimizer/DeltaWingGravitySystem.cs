




using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Havok;
using NLog;
using NLog.Fluent;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.GameSystems;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.ModAPI;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using VRageRender;

namespace GDriveOptimizer;



public struct ForceData(Vector3D pos, Vector3D force)
{
    public readonly Vector3D Position = pos;
    public readonly Vector3D Force = force;
}
public static class DeltaWingGravitySystem
{
    private static readonly Dictionary<MyCubeGrid, GravityShip> GravityShips = [];
    
    
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly List<IMyEntity> GravityAffectedObjects = [];
    private static readonly List<IMyEntity> NonMassBlocks = [];
    private static readonly List<IMyVirtualMass> MassBlocks = [];


    private static readonly Dictionary<IMyCubeGrid, int> MassBlockCounts = new(); // TODO: Make this part of an object later TODO: remove key when grid closes (technically a very small memory leak rn)
    private static readonly Dictionary<IMyCubeGrid, int> GridIndices = new();
    public static void AddGravityAffectedObjects(MyConcurrentHashSet<IMyEntity> newObjs)
    {
        foreach (var thing in newObjs)
        {
            GravityAffectedObjects.Add(thing);
        }
    }



    private readonly struct GravityStrengthResult(MyEntity entity, Vector3 force, Vector3D position, float mass)
    {
        public readonly MyEntity Entity = entity;
        public readonly Vector3 Force = force;
        public readonly Vector3D Position = position;
        public readonly float Mass = mass;

        public void Deconstruct(out MyEntity entity, out Vector3 force, out Vector3D position, out float mass)
        {
            entity = Entity;
            force = Force;
            position = Position;
            mass = Mass;
        }
    }
public static async void Update()
{
    return;
    #region Deprecated
    
    try
    {

        #region SeparateMassBlocks
        for (int i = 0; i < GravityAffectedObjects.Count; i++)
        {
            var entity = GravityAffectedObjects[i];
            if (entity is IMyVirtualMass myVirtualMass)
            {
            }
            else
            {
                NonMassBlocks.Add(entity);
            }
        }
        #endregion
        
        
        
        
        var tasks = new List<Task>();
        #if DEBUG_PERF
        var sw = Stopwatch.StartNew();
        #endif

        int length = GravityAffectedObjects.Count;
        
        


        GravityStrengthResult[] results = new GravityStrengthResult[length];

        #region NonMassBlockForceGeneration
        int index = 0;
        foreach (IMyEntity containedEntity in NonMassBlocks)
        {
            var thisIndex = index;
            tasks.Add(Task.Run(() =>
            {
                MyEntity? myEntity = containedEntity as MyEntity;
                if (myEntity?.Physics == null) return;
        
                MatrixD worldMatrix = myEntity.WorldMatrix;
        
                float strengthMultiplier = MyGravityProviderSystem.CalculateArtificialGravityStrengthMultiplier(
                    MyGravityProviderSystem.CalculateHighestNaturalGravityMultiplierInPoint(worldMatrix.Translation));
        
                if (strengthMultiplier <= 0.0) return;
        
                Vector3 acceleration = MyGravityProviderSystem.CalculateArtificialGravityInPoint(worldMatrix.Translation, strengthMultiplier);
        
                if (myEntity.Physics.IsKinematic || myEntity.Physics.IsStatic ||
                    myEntity.Physics.RigidBody2 != null || myEntity is MyCharacter ||
                    myEntity.Physics.RigidBody == null) return;
                
                float mass = myEntity.Physics.RigidBody.Mass;
                results[thisIndex] = new GravityStrengthResult(myEntity, acceleration * mass, worldMatrix.Translation, mass);
            }));
            index++;
        }
        #endregion

        #region MassBlockForceGeneration
        
        
        Dictionary<IMyCubeGrid, ForceData[]> shipResults = new();
        
        foreach (IMyVirtualMass myVirtualMass in MassBlocks)
        {
            var cubeGrid = myVirtualMass.CubeGrid;
    
            if (!GridIndices.ContainsKey(cubeGrid))
            {
                GridIndices.Add(cubeGrid, 0);
                shipResults.Add(cubeGrid, new ForceData[MassBlockCounts[cubeGrid]]);
            }

            var gridSpecificIndex = GridIndices[cubeGrid];
            GridIndices[cubeGrid] += 1;

            // Capture local variables for use inside the Task
            var localCubeGrid = cubeGrid;
            var localIndex = gridSpecificIndex;

            tasks.Add(Task.Run(() =>
            {
                if (myVirtualMass.Physics != null &&
                    myVirtualMass.Physics.RigidBody.IsActive &&
                    myVirtualMass.IsWorking)
                {
                    MatrixD worldMatrix = myVirtualMass.WorldMatrix;

                    float strengthMultiplier = MyGravityProviderSystem.CalculateArtificialGravityStrengthMultiplier(
                        MyGravityProviderSystem.CalculateHighestNaturalGravityMultiplierInPoint(worldMatrix.Translation));

                    if (strengthMultiplier <= 0.0f) return;

                    Vector3 acceleration = MyGravityProviderSystem.CalculateArtificialGravityInPoint(worldMatrix.Translation, strengthMultiplier);

                    Vector3D pos = worldMatrix.Translation;
                    Vector3 force = acceleration * myVirtualMass.VirtualMass;

                    if (force == Vector3.Zero) return;

                    var forceData = new ForceData(pos, force);

                    // Access is safe because array was already initialized on the main thread
                    shipResults[localCubeGrid][localIndex] = forceData;
                }
            }));
        }
        #endregion
        
        await Task.WhenAll(tasks);
        
        #if DEBUG_PERF
        sw.Stop();
        double microseconds = (sw.ElapsedTicks * 1_000_000.0) / Stopwatch.Frequency;
        Log.Info("Time (Calculate force): " + microseconds);
        
        sw.Restart();
        #endif
        // Apply forces on the main thread
        
        tasks.Clear();
        
        #region NonMassBlockForceApplication
        foreach (var (entity, force, position, _) in results)
        {
            if (entity == null) continue; // Indice is missing
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    if (entity is MyFloatingObject fo)
                    {
                        fo.GeneratedGravity += force;
                    }
                    else if (entity is MyInventoryBagEntity bag1)
                    {
                        bag1.GeneratedGravity += force;
                    }
                    else if (entity is MyCargoContainerInventoryBagEntity bag2)
                    {
                        bag2.GeneratedGravity += force;
                    }
                    else
                    {
                        entity.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, force, null, null);
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception
                    Log.Error($"Error processing entity {entity}: {ex.Message}");
                    Log.Error(ex.StackTrace);
                    // Optionally, you could rethrow or handle the exception depending on your needs
                }
            }));
        }
        #endregion

        #region MassBlockForceApplication
        foreach (var kvp in shipResults)
            tasks.Add(Task.Run(() =>
            {
            var ship = kvp.Key;
            var physics = ship.Physics;
            var forces = kvp.Value;
            if (forces.Length == 0) return;
            var force = GetWeightedAveragePositionAndForce(forces);
            
            
            physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, force.Force, force.Position, null, null, false);
            
            }));
        #endregion
        await Task.WhenAll(tasks);
        #if DEBUG_PERF
        sw.Stop();
        microseconds = (sw.ElapsedTicks * 1_000_000.0) / Stopwatch.Frequency;
        Log.Info("Time (apply force): " + microseconds);
        MyGravityGeneratorBasePatch.LogProfilingTime();
        #endif
        GravityAffectedObjects.Clear(); // Clear with a new one
        NonMassBlocks.Clear();
        GridIndices.Clear();

    }
    catch (Exception e)
    {
        Log.Info(e);
        
        // Should clear them out in case shit breaks, this can and will freeze your system otherwise
        GravityAffectedObjects.Clear(); // Clear with a new one
        NonMassBlocks.Clear();
        GridIndices.Clear();
    }

    #endregion

}
    
    
    
    
    private static ForceData GetWeightedAveragePositionAndForce(ForceData[] dataList)
    {
        
        Vector3 netForce = Vector3.Zero;
        Vector3 netTorque = Vector3.Zero; // Moment = r × F


        for (var index = 0; index < dataList.Length; index++)
        {
            var data = dataList[index];
            Vector3 force = data.Force;
            if (force == null) continue;
            Vector3 position = data.Position;

            netForce += force;
            netTorque += Vector3.Cross(position, force); // Torque = r × F
        }

        if (netForce.LengthSquared() == 0f)
        {
            return new ForceData(Vector3.Zero, Vector3.Zero);
        }
        var rPerp = Vector3.Cross(netForce, netTorque) 
                    / netForce.LengthSquared();

        return new ForceData(rPerp, netForce);
    }


    private static GravityShip GetGravityShip(MyCubeGrid grid)
    {
        if (!GravityShips.ContainsKey(grid))
        {
            GravityShips.Add(grid, new GravityShip(grid));
        }

        return GravityShips[grid];
    }
    

    public static void OnGravityChanged(MyGravityGeneratorBase generator, float value)
    {
        //Log.Info(generator + ": Gravity changed to " + value);
    }
    
    public static void OnGravityGeneratorInit(MyGravityGeneratorBase generator, MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
    {
        Log.Info(generator + ": Init");
        if (!MassBlockCounts.ContainsKey(cubeGrid)) MassBlockCounts.Add(cubeGrid, 0);
        
        var gravityShip = GetGravityShip(cubeGrid);
        gravityShip.AddGenerator(generator);
    }
    
    public static void OnGravityGeneratorClose(MyGravityGeneratorBase generator)
    {
        Log.Info(generator + ": Closed");
        
        var gravityShip = GetGravityShip(generator.CubeGrid);
        gravityShip.RemoveGenerator(generator);
    }
    
    
    public static void OnGravityGeneratorWorkingChanged(MyGravityGeneratorBase generator, bool newValue)
    {
        //Log.Info(generator + ": working: " + newValue);
    }
    
    public static void OnGravityGeneratorGridChanged(MyGravityGeneratorBase generator, MyCubeGrid oldGrid, MyCubeGrid newGrid)
    {
        Log.Info(generator + ": grid changed to " + newGrid);
        
    }
    
    public static void OnFieldShapeUpdated(MyGravityGeneratorBase generator)
    {
        //Log.Info(generator + ": Field shape modified");
    }
    
    
    
    public static void OnMassBlockInit(MyVirtualMass mass, MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
    {
        Log.Info(mass + ": Init");
        if (!MassBlockCounts.ContainsKey(cubeGrid)) MassBlockCounts.Add(cubeGrid, 0);
        MassBlockCounts[cubeGrid] += 1;
        Log.Info("Mass block count for grid " + cubeGrid + " is now " + MassBlockCounts[cubeGrid]);
        MassBlocks.Add(mass);

        var gravityShip = GetGravityShip(cubeGrid);
        gravityShip.AddMassBlock(mass);
    }
    
    public static void OnMassBlockClose(MyVirtualMass mass)
    {
        Log.Info(mass + ": Closed");
        MassBlockCounts[mass.CubeGrid] -= 1;
        Log.Info("Mass block count for grid " + mass.CubeGrid + " is now " + MassBlockCounts[mass.CubeGrid]);
        MassBlocks.Remove(mass);

        var gravityShip = GetGravityShip(mass.CubeGrid);
        gravityShip.RemoveMassBlock(mass);
    }
    
    public static void OnMassBlockGridChanged(MyVirtualMass mass, MyCubeGrid oldGrid, MyCubeGrid newGrid)
    {
        Log.Info(mass + ": grid changed to " + newGrid);
        MassBlockCounts[oldGrid] -= 1;
        Log.Info("Mass block count for grid " + oldGrid + " is now " + MassBlockCounts[oldGrid]);
        if (!MassBlockCounts.ContainsKey(newGrid)) MassBlockCounts.Add(newGrid, 0);
        MassBlockCounts[newGrid] += 1;
        Log.Info("Mass block count for grid " + newGrid + " is now " + MassBlockCounts[newGrid]);
        
        
    }
}