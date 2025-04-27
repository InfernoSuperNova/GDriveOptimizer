using HarmonyLib;
using Sandbox.Game.Entities;
using SpaceEngineers.Game.Entities.Blocks;
using VRage.Game;

namespace GDriveOptimizer;

[HarmonyPatch(typeof(MyVirtualMass), "Init")]
public static class MassInitPatch
{
    static void Postfix(MyVirtualMass __instance, MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
    {
        DeltaWingGravitySystem.OnMassBlockInit(__instance, objectBuilder, cubeGrid);
    }
}
