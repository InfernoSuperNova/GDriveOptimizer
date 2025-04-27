using HarmonyLib;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace GDriveOptimizer;

[HarmonyPatch(typeof(MyCubeBlock), "OnCubeGridChanged")]
public static class Patch_OnCubeGridChanged
{
    public static readonly Logger Log = LogManager.GetCurrentClassLogger();
    
    static void Postfix(MyCubeBlock __instance, MyCubeGrid oldGrid)
    {
        if (__instance is MyGravityGeneratorBase generator)
        {
                
            var newGrid = generator.CubeGrid;
            DeltaWingGravitySystem.OnGravityGeneratorGridChanged(generator, oldGrid, newGrid);
        }
        if (__instance is MyVirtualMass mass)
        {
            try
            {
                var newGrid = mass.CubeGrid;
                DeltaWingGravitySystem.OnMassBlockGridChanged(mass, oldGrid, newGrid);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
                
        }
    }
}

[HarmonyPatch(typeof(MyFunctionalBlock), "Closing")]
public static class Patch_Closing
{
    static void Postfix(MyFunctionalBlock __instance)
    {
        if (__instance is MyVirtualMass mass)
        {
            DeltaWingGravitySystem.OnMassBlockClose(mass);
        }
       
    }
}