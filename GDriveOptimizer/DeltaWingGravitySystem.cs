using System.Collections.Concurrent;
using System.Diagnostics;
using Havok;
using NLog;
using NLog.Fluent;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.GameSystems;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.Entity;
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
    public static Dictionary<MyPhysicsComponentBase, List<ForceData>> ShipForces = new();
    public static void AddGravityAffectedObjects(MyConcurrentHashSet<IMyEntity> newObjs)
    {
        foreach (var thing in newObjs)
        {
            GravityAffectedObjects.Add(thing);
        }
    }

public static async void Update()
{
    var tasks = new List<Task>();
    var results = new ConcurrentBag<(MyEntity Entity, Vector3 Force, Vector3D Position, float Mass)>();

    foreach (IMyEntity containedEntity in GravityAffectedObjects)
    {
        tasks.Add(Task.Run(() =>
        {
            MyEntity myEntity = containedEntity as MyEntity;
            if (myEntity == null || myEntity.Physics == null) return;

            MatrixD worldMatrix = myEntity.WorldMatrix;

            float strengthMultiplier = MyGravityProviderSystem.CalculateArtificialGravityStrengthMultiplier(
                MyGravityProviderSystem.CalculateHighestNaturalGravityMultiplierInPoint(worldMatrix.Translation));

            if (strengthMultiplier <= 0.0) return;

            Vector3 acceleration = MyGravityProviderSystem.CalculateArtificialGravityInPoint(worldMatrix.Translation, strengthMultiplier);

            var myVirtualMass = myEntity as SpaceEngineers.Game.ModAPI.IMyVirtualMass;
            if (myVirtualMass != null && myEntity.Physics.RigidBody.IsActive && myVirtualMass.IsWorking)
            {
                results.Add((myEntity, acceleration * myVirtualMass.VirtualMass, worldMatrix.Translation, myVirtualMass.VirtualMass));
            }
            else if (!myEntity.Physics.IsKinematic && !myEntity.Physics.IsStatic &&
                     myEntity.Physics.RigidBody2 == null && !(myEntity is MyCharacter) &&
                     myEntity.Physics.RigidBody != null)
            {
                float mass = myEntity.Physics.RigidBody.Mass;
                results.Add((myEntity, acceleration * mass, worldMatrix.Translation, mass));
            }
        }));
    }

    await Task.WhenAll(tasks);

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
    

    GravityAffectedObjects = []; // Clear with a new one
    ShipForces = new();
}
    
    
    
    
    public static ForceData GetWeightedAveragePositionAndForce(List<ForceData> dataList)
    {
        if (dataList == null || dataList.Count == 0)
            return new ForceData(Vector3D.Zero, Vector3D.Zero);

        Vector3D weightedPosSum = Vector3D.Zero;
        double totalWeight = 0;

        Vector3D totalForce = Vector3D.Zero;

        foreach (var data in dataList)
        {
            double weight = data.Force.Length();
            weightedPosSum += data.Position * weight;
            totalWeight += weight;

            totalForce += data.Force;
        }

        Vector3D avgPos = totalWeight > 0f ? weightedPosSum / totalWeight : Vector3D.Zero;
        Vector3D avgForce = totalForce / dataList.Count;

        return new ForceData(avgPos, avgForce);
    }
}