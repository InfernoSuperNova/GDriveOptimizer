




using System.Collections.Concurrent;
using System.Diagnostics;
using Havok;
using NLog;
using NLog.Fluent;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.GameSystems;
using SpaceEngineers.Game.ModAPI;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using VRageRender;

namespace GDriveOptimizer;


public class ForceData(Vector3D pos, Vector3D force)
{
    public Vector3D Position = pos;
    public Vector3D Force = force;
}

public static class DeltaWingGravitySystem
{
    public static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public static MyConcurrentHashSet<IMyEntity> GravityAffectedObjects = [];
    public static ConcurrentDictionary<IMyCubeGrid, ConcurrentBag<ForceData>> ShipForces = new();

    public static void AddGravityAffectedObjects(MyConcurrentHashSet<IMyEntity> newObjs)
    {
        foreach (var thing in newObjs)
        {
            GravityAffectedObjects.Add(thing);
        }
    }

public static async void Update()
{
    try
    {
        var tasks = new List<Task>();
        var results = new ConcurrentBag<(MyEntity Entity, Vector3 Force, Vector3D Position, float Mass)>();
        
        #if DEBUG_PERF
        var sw = Stopwatch.StartNew();
        #endif
        
        foreach (IMyEntity containedEntity in GravityAffectedObjects)
        {
            tasks.Add(Task.Run(() =>
            {
                MyEntity? myEntity = containedEntity as MyEntity;
                if (myEntity?.Physics == null) return;

                MatrixD worldMatrix = myEntity.WorldMatrix;

                float strengthMultiplier = MyGravityProviderSystem.CalculateArtificialGravityStrengthMultiplier(
                    MyGravityProviderSystem.CalculateHighestNaturalGravityMultiplierInPoint(worldMatrix.Translation));

                if (strengthMultiplier <= 0.0) return;

                Vector3 acceleration = MyGravityProviderSystem.CalculateArtificialGravityInPoint(worldMatrix.Translation, strengthMultiplier);

                if (myEntity is IMyVirtualMass myVirtualMass && myEntity.Physics.RigidBody.IsActive && myVirtualMass.IsWorking)
                {
                    //results.Add((myEntity, acceleration * myVirtualMass.VirtualMass, worldMatrix.Translation, myVirtualMass.VirtualMass));

                    var pos = worldMatrix.Translation;
                    var force = acceleration * myVirtualMass.VirtualMass;

                    if (pos == null || force == null || force == Vector3.Zero) return;
                    
                    var forceData = new ForceData(worldMatrix.Translation, acceleration * myVirtualMass.VirtualMass);

                    
                    
                    (ShipForces.GetOrAdd(myVirtualMass.CubeGrid, _ => new ConcurrentBag<ForceData>())).Add(forceData);

                    
                }
                else if (!myEntity.Physics.IsKinematic && !myEntity.Physics.IsStatic &&
                         myEntity.Physics.RigidBody2 == null && myEntity is not MyCharacter &&
                         myEntity.Physics.RigidBody != null)
                {
                    float mass = myEntity.Physics.RigidBody.Mass;
                    results.Add((myEntity, acceleration * mass, worldMatrix.Translation, mass));
                }
            }));
        }

        
        await Task.WhenAll(tasks);
        
        #if DEBUG_PERF
        sw.Stop();
        double microseconds = (sw.ElapsedTicks * 1_000_000.0) / Stopwatch.Frequency;
        Log.Info("Time (Calculate force): " + microseconds);
        
        sw.Restart();
        #endif
        // Apply forces on the main thread
        foreach (var (entity, force, position, _) in results)
        {
            if (entity is SpaceEngineers.Game.ModAPI.IMyVirtualMass vm)
            {
                if (!vm.CubeGrid.IsStatic && !vm.CubeGrid.Physics.IsStatic)
                {
                    vm.CubeGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, force, position, null, null, false);
                }
            }
            else if (entity is MyFloatingObject fo)
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


        foreach (var kvp in ShipForces)
        {
            var ship = kvp.Key;
            var physics = ship.Physics;
            var forces = kvp.Value;
            if (forces.Count == 0) continue;
            var force = GetWeightedAveragePositionAndForce(forces);
            
            
            physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, force.Force, force.Position, null, null, false);

            // foreach (var force in forces)
            // {
            //     physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, force.Force, force.Position, null, null, false);
            // }
        }
        #if DEBUG_PERF
        sw.Stop();
        microseconds = (sw.ElapsedTicks * 1_000_000.0) / Stopwatch.Frequency;
        Log.Info("Time (apply force): " + microseconds);
        MyGravityGeneratorBasePatch.LogProfilingTime();
        #endif
        GravityAffectedObjects = []; // Clear with a new one
        ShipForces.Clear();
        
        
    }
    catch (Exception e)
    {
        Log.Info(e);
    }
}
    
    
    
    
    public static ForceData GetWeightedAveragePositionAndForce(ConcurrentBag<ForceData> dataList)
    {
        
        Vector3 netForce = Vector3.Zero;
        Vector3 netTorque = Vector3.Zero; // Moment = r × F

        
        foreach (var data in dataList)
        {
            Vector3 force = data.Force;
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
}