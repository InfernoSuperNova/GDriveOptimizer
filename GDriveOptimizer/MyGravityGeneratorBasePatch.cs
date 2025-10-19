using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using HarmonyLib;
using Havok;
using NLog;
using NLog.Fluent;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Weapons;
using SpaceEngineers.Game.Entities.Blocks;
using Torch.Managers.PatchManager;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using VRageRender;

namespace GDriveOptimizer
{
    [PatchShim]
    public static class MyGravityGeneratorBasePatch
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        
        internal static readonly MethodInfo updateBeforeSimulation =
        typeof(MyGravityGeneratorBase)
        .GetMethod(nameof(MyGravityGeneratorBase.UpdateBeforeSimulation), BindingFlags.Instance | BindingFlags.Public) ?? 
        throw new Exception("Failed to find patch method");
        
        internal static readonly MethodInfo updateBeforeSimulationPatch = 
        typeof(MyGravityGeneratorBasePatch)
        .GetMethod(nameof(MyGravityGeneratorBasePatch.UpdateBeforeSimulation), BindingFlags.Static | BindingFlags.Public) ?? 
        throw new Exception("Failed to find patch method");
    
        
        internal static MethodInfo baseMethod = typeof(MyFunctionalBlock)  // replace with the actual base class type
        .GetMethod("UpdateBeforeSimulation", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? 
        throw new Exception("Failed to find patch method");
        
        
        
        internal static FieldInfo mContainedEntitiesInfo = typeof(MyGravityGeneratorBase)  // or __instance.GetType().BaseType if needed
        .GetField("m_containedEntities", BindingFlags.Instance | BindingFlags.NonPublic) ?? 
        throw new Exception("Failed to find patch method");
        
        internal static PropertyInfo hasDamageEffectInfo = typeof(MyCubeBlock)  // or __instance.GetType().BaseType if needed
        .GetProperty("HasDamageEffect", BindingFlags.Instance | BindingFlags.NonPublic) ?? 
        throw new Exception("Failed to find property HasDamageEffect in class MyCubeBlock");
        
        
        


        private static double _lastActionMicroseconds = 0;
        #if DEBUG_PERF
        public static void LogProfilingTime()
        {
            Log.Info("Time (Index blocks to list): " + _lastActionMicroseconds);
            _lastActionMicroseconds = 0;
        }
        #endif
        public static bool UpdateBeforeSimulation(MyGravityGeneratorBase __instance)
        {
            #if DEBUG_PERF
            Stopwatch sw = Stopwatch.StartNew();
            #endif
            GDBase.UpdateBeforeSimulation(__instance);
            //Base_UpdateBeforeSimulation(__instance);

            var containedEntities = (MyConcurrentHashSet<IMyEntity>)mContainedEntitiesInfo.GetValue(__instance);

            if (__instance.IsWorking)
            {
                //DeltaWingGravitySystem.AddGravityAffectedObjects(containedEntities);
            }

            if (containedEntities.Count != 0)
            {
                #if DEBUG_PERF
                sw.Stop();
                LogMicroseconds(sw.ElapsedTicks);
                #endif
                return false;
            }

            __instance.NeedsUpdate = (bool)hasDamageEffectInfo.GetValue(__instance)
                ? MyEntityUpdateEnum.EACH_FRAME
                : MyEntityUpdateEnum.EACH_100TH_FRAME;
            #if DEBUG_PERF
            sw.Stop();
            LogMicroseconds(sw.ElapsedTicks);
            #endif
            return false;
        }
        #if DEBUG_PERF
        private static void LogMicroseconds(long elapsedTicks)
        {
            double microseconds = (elapsedTicks * 1_000_000.0) / Stopwatch.Frequency;
            _lastActionMicroseconds += microseconds;
            //Debug.WriteLine($"UpdateBeforeSimulation took {microseconds:F3} µs");
        }
        #endif
        public static void Patch(PatchContext ctx)
        {
            ctx.GetPattern(updateBeforeSimulation).Prefixes.Add(updateBeforeSimulationPatch);
            
            var harmony = new Harmony("test");
            harmony.PatchAll();
            
            GravityGeneratorPropertyPatches.DoPatch();
            Log.Info("Patching successful maybe");
        }
    }

    [HarmonyPatch(typeof(MyFunctionalBlock), "UpdateBeforeSimulation")]
    class GDBase
    {
        [HarmonyReversePatch]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void UpdateBeforeSimulation(MyFunctionalBlock instance)
        {
            // An empty stub that lets us call the base method of UpdateBeforeSimulation on MyGravityGeneratorBase
        }
    }
    
    
    public static class GravityGeneratorPropertyPatches
    {
        public static event Action<MyGravityGenerator, Vector3> OnFieldSizeChanged = delegate { };
        public static event Action<MyGravityGeneratorBase, float> OnGravityAccelerationChanged = delegate { };
        public static void DoPatch()
        {
            var harmony = new Harmony("GravityGeneratorPropertyPatches");
            var fieldSizeSetterMethod = AccessTools.PropertySetter("SpaceEngineers.Game.Entities.Blocks.MyGravityGenerator:FieldSize");
            var accelerationSetterMethod = AccessTools.PropertySetter("SpaceEngineers.Game.Entities.Blocks.MyGravityGeneratorBase:GravityAcceleration");
            harmony.Patch(accelerationSetterMethod, new HarmonyMethod(typeof(GravityGeneratorPropertyPatches), nameof(AccelerationSetterPrefix)));
            harmony.Patch(fieldSizeSetterMethod, new HarmonyMethod(typeof(GravityGeneratorPropertyPatches), nameof(FieldSizeSetterPrefix)));
            
        }
        public static bool FieldSizeSetterPrefix(MyGravityGenerator __instance, ref Vector3 value)
        {
            Plugin.Log.Info("Sanitysize");
            OnFieldSizeChanged(__instance, value);
            return true;
        }
        public static bool AccelerationSetterPrefix(MyGravityGeneratorBase __instance, ref float value)
        {
            Plugin.Log.Info("Sanityacceleration");
            OnGravityAccelerationChanged(__instance, value);
            return true;
        }
    }
    
    [HarmonyPatch(typeof(MyGravityGeneratorBase), "Init")]
    public static class GravityInitPatch
    {
        public static event Action<MyGravityGeneratorBase, MyObjectBuilder_CubeBlock, MyCubeGrid> Trigger = delegate { };
        public static event Action<MyGravityGeneratorBase, MyCubeGrid, MyCubeGrid> GridChanged = delegate { };

        static void Postfix(MyGravityGeneratorBase __instance, MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
        {
            // Fire Init trigger
            Trigger?.Invoke(__instance, objectBuilder, cubeGrid);

            // Subscribe instance event with a closure capturing __instance
            __instance.CubeGridChanged += (oldGrid) =>
            {
                var newGrid = __instance.CubeGrid;
                GridChanged?.Invoke(__instance, (MyCubeGrid)oldGrid, newGrid);
            };
        }
    }
    [HarmonyPatch(typeof(MyGravityGeneratorBase), "Closing")]
    public static class GravityClosingPatch
    {
        
        public static event Action<MyGravityGeneratorBase> Trigger = delegate { };
        static void Postfix(MyGravityGeneratorBase __instance)
        {
            Trigger(__instance);
        }
    }

    [HarmonyPatch(typeof(MyGravityGeneratorBase), "OnIsWorkingChanged")]
    public static class GravityWorkingChangedPatch
    {
        public static event Action<MyGravityGeneratorBase, bool> Trigger = delegate { };
        static void Postfix(MyGravityGeneratorBase __instance)
        {
            bool newValue = __instance.IsWorking;
            Trigger(__instance, newValue);
        }
    }
}


