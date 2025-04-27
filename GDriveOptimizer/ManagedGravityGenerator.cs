using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Havok;
using Sandbox.Game.Entities;
using SpaceEngineers.Game.Entities.Blocks;

namespace GDriveOptimizer
{
    public static class GravityGeneratorRegistry
    {
        private static readonly Dictionary<MyGravityGeneratorBase, ManagedGravityGenerator> Map
            = new();

        public static void Register(MyGravityGeneratorBase gen, ManagedGravityGenerator wrapper)
            => Map[gen] = wrapper;

        public static void Unregister(MyGravityGeneratorBase gen)
            => Map.Remove(gen);

        public static bool TryGet(MyGravityGeneratorBase gen, out ManagedGravityGenerator wrapper)
            => Map.TryGetValue(gen, out wrapper);
    }
    
    public class ManagedGravityGenerator : IDisposable
    {
        private MyGravityGeneratorBase _generator;
        private MyCubeGrid  _grid;
        private GravityShip _ship;

        public ManagedGravityGenerator(MyGravityGeneratorBase generator, GravityShip ship)
        {
            _generator = generator;
            _grid      = generator.CubeGrid;
            _ship = ship;

            // hook it up so our patches can find this instance later
            GravityGeneratorRegistry.Register(_generator, this);
        }

        // this is the method we want to be called on both enter and leave
        public void ContainedEntitiesChanged(HkPhantomCallbackShape sender, HkRigidBody body)
        {
            // …your logic here…
        }
        public void Dispose()
        {
            GravityGeneratorRegistry.Unregister(_generator);
        }
    }
    
    
    
    
    // patch phantom_enter
    [HarmonyPatch(typeof(MyGravityGeneratorBase), "phantom_enter")]
    static class MyGravityGeneratorBase_PhantomEnter
    {
        static void Prefix(MyGravityGeneratorBase __instance,
            HkPhantomCallbackShape sender,
            HkRigidBody body)
        {
            if (GravityGeneratorRegistry.TryGet(__instance, out var wrapper))
                wrapper.ContainedEntitiesChanged(sender, body);
        }
    }

    // patch phantom_leave
    [HarmonyPatch(typeof(MyGravityGeneratorBase), "phantom_leave")]
    static class MyGravityGeneratorBase_PhantomLeave
    {
        static void Prefix(MyGravityGeneratorBase __instance,
            HkPhantomCallbackShape sender,
            HkRigidBody body)
        {
            if (GravityGeneratorRegistry.TryGet(__instance, out var wrapper))
                wrapper.ContainedEntitiesChanged(sender, body);
        }
    }
}