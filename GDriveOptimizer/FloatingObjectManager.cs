using System.Reflection;
using System.Windows.Controls;
using GDriveOptimizer.Gravity;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.GameSystems;
using VRage.Game.Components;
using VRageMath;

namespace GDriveOptimizer;

public static class FloatingObjectManager
{
    public static void Setup()
    {
        Plugin.UpdateEvent += Update;
    }
    private static void Update(long frame)
    {
        var floating = MyEntities
            .GetEntities()
            .Where(e => e is MyFloatingObject or MyInventoryBagEntity or MyCargoContainerInventoryBagEntity);
        foreach (var floatingObject in floating)
        {
            var gravity = GravityManager.Sample(floatingObject.PositionComp.GetPosition());
            switch (floatingObject)
            {
                    
                case MyFloatingObject myFloatingObject:
                    Vector3 vector3_2 = (myFloatingObject.HasConstraints() ? 2f : 1f) * gravity;
                    myFloatingObject.GeneratedGravity += vector3_2;
                    continue;
                case MyInventoryBagEntity inventoryBagEntity1:
                    inventoryBagEntity1.GeneratedGravity += gravity;
                    continue;
                case MyCargoContainerInventoryBagEntity inventoryBagEntity2:
                    inventoryBagEntity2.GeneratedGravity += gravity;
                    continue;
                default:
                    floatingObject.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, new Vector3?(gravity * floatingObject.Physics.RigidBody.Mass), new Vector3D?(), new Vector3?());
                    continue;
            }
        }
    }
}