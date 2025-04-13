using System;
using System.Reflection;
using System.Windows.Data;
using HarmonyLib;
using Havok;
using NLog.Fluent;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using SpaceEngineers.Game.Entities.Blocks;
using Torch.Managers.PatchManager;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;
using VRageRender;

namespace GDriveOptimizer
{
    [PatchShim]
    public static class MyGravityGeneratorBasePatch
    {
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
        
        
        
        
        public static bool UpdateBeforeSimulation(MyGravityGeneratorBase __instance)
        { 
            
            //Base_UpdateBeforeSimulation(__instance); // We somehow need to call the base UpdateBeforeSimulation otherwise other logic will be broken
          var containedEntities = (MyConcurrentHashSet<IMyEntity>)mContainedEntitiesInfo.GetValue(__instance);
           if (__instance.IsWorking)
           {
               
               
               
             DeltaWingGravitySystem.AddGravityAffectedObjects(containedEntities);
           }
           if (containedEntities.Count != 0)
             return false;
           __instance.NeedsUpdate = (bool)hasDamageEffectInfo.GetValue(__instance) ? MyEntityUpdateEnum.EACH_FRAME : MyEntityUpdateEnum.EACH_100TH_FRAME;
                 
            return false;
        }

        public static void Patch(PatchContext ctx)
        {
            ctx.GetPattern(updateBeforeSimulation).Prefixes.Add(updateBeforeSimulationPatch);
            
            
            Log.Info("Patching successful maybe");
        }
    }
}


