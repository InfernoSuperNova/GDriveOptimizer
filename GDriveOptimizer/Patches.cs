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
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.ModAPI;
using Torch.Managers.PatchManager;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Sync;
using VRageMath;
using VRageRender;

namespace GDriveOptimizer
{
    [PatchShim]
    public static class Patches
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        internal static readonly MethodInfo playerSimulate = typeof(MyCharacter).GetMethod(nameof(MyCharacter.Simulate), BindingFlags.Instance | BindingFlags.Public) ?? throw new Exception("Failed to find patch method (playerSimulate)");
        internal static readonly MethodInfo playerSimulatePatch = typeof(Patches).GetMethod(nameof(Patches.PlayerSimulate), BindingFlags.Static | BindingFlags.Public) ?? throw new Exception("Failed to find patch method (playerSimulatePatch)");
        
        private static double _lastActionMicroseconds = 0;
        private static int dumbTimer = 0;
        public static bool PlayerSimulate(MyCharacter __instance)
        {
            //if (dumbTimer++ % 120 != 00) return true;
            var result = GravityManager.Sample(__instance.WorldMatrix.Translation);
            //Plugin.Log.Info($"Gravity: {result}");
            return true;
        }
        
        public static void Patch(PatchContext ctx)
        {
            ctx.GetPattern(playerSimulate).Prefixes.Add(playerSimulatePatch);

            var harmony = new Harmony("test");
            harmony.PatchAll();

            GravityGeneratorPropertyPatches.DoPatch();
            Log.Info("Patching successful maybe");
        }
    }
    
    


    public static class GravityGeneratorPropertyPatches
    {
        public static event Action<MyGravityGenerator, Vector3> OnFieldSizeChanged;
        public static event Action<MyGravityGeneratorBase, float> OnGravityAccelerationChanged;

        private static readonly Dictionary<MyGravityGenerator, Action<SyncBase>> _fieldSizeGravityGens =
            new Dictionary<MyGravityGenerator, Action<SyncBase>>();

        private static readonly Dictionary<MyGravityGeneratorBase, Action<SyncBase>> _accelerationGravityGens =
            new Dictionary<MyGravityGeneratorBase, Action<SyncBase>>();

        private static FieldInfo _fieldSize =
            typeof(MyGravityGenerator).GetField("m_fieldSize", BindingFlags.Instance | BindingFlags.NonPublic);

        private static FieldInfo _acceleration = typeof(MyGravityGeneratorBase).GetField("m_gravityAcceleration",
            BindingFlags.Instance | BindingFlags.NonPublic);

        public static void DoPatch()
        {
            var harmony = new Harmony("GravityGeneratorPropertyPatches");
            var fieldSizeSetterMethod =
                AccessTools.PropertySetter("SpaceEngineers.Game.Entities.Blocks.MyGravityGenerator:FieldSize");
            // var accelerationSetterMethod = AccessTools.PropertySetter("SpaceEngineers.Game.Entities.Blocks.MyGravityGeneratorBase:GravityAcceleration");


            harmony.Patch(fieldSizeSetterMethod,
                postfix: new HarmonyMethod(typeof(GravityGeneratorPropertyPatches), nameof(FieldSizeSetterPrefix)));
            // harmony.Patch(accelerationSetterMethod, postfix: new HarmonyMethod(typeof(GravityGeneratorPropertyPatches), nameof(AccelerationSetterPrefix)));
        }

        public static void FieldSizeSetterPrefix(MyGravityGenerator __instance, ref Vector3 value)
        {
            var sync = (Sync<Vector3, SyncDirection.BothWays>)_fieldSize.GetValue(__instance);
            if (sync == null || _fieldSizeGravityGens.ContainsKey(__instance))
                return;


            Action<SyncBase> anon = (x) =>
            {
                var v = sync.Value;
                OnFieldSizeChanged?.Invoke(__instance, v);
            };

            sync.ValueChanged += anon;
            _fieldSizeGravityGens[__instance] = anon;
            __instance.OnClose += (gravityGenEntity) =>
            {
                if (!_fieldSizeGravityGens.ContainsKey((MyGravityGenerator)gravityGenEntity)) return;
                if ((Sync<Vector3, SyncDirection.BothWays>)_fieldSize.GetValue((MyGravityGenerator)gravityGenEntity) !=
                    null)
                    sync.ValueChanged -= _fieldSizeGravityGens[(MyGravityGenerator)gravityGenEntity];
            };
            var f = 0f;


            // Don't ask why. Keen doesn't use the acceleration frontend property at all.
            // Field size is literally the only guaranteed init point.
            // No touchy unless you're ready to suffer.
            AccelerationSetterPrefix(__instance, ref f);
        }

        public static void AccelerationSetterPrefix(MyGravityGeneratorBase __instance, ref float value)
        {
            var sync = (Sync<float, SyncDirection.BothWays>)_acceleration.GetValue(__instance);
            if (sync == null || _accelerationGravityGens.ContainsKey(__instance))
                return;

            Action<SyncBase> anon = (x) =>
            {
                var v = sync.Value;
                OnGravityAccelerationChanged?.Invoke(__instance, v);
            };

            sync.ValueChanged += anon;
            _accelerationGravityGens[__instance] = anon;
            __instance.OnClose += (gravityGenEntity) =>
            {
                if (!_accelerationGravityGens.ContainsKey((MyGravityGeneratorBase)gravityGenEntity)) return;
                if ((Sync<float, SyncDirection.BothWays>)_acceleration.GetValue(
                        (MyGravityGeneratorBase)gravityGenEntity) != null)
                    sync.ValueChanged -= _accelerationGravityGens[(MyGravityGeneratorBase)gravityGenEntity];
            };
        }
    }

    // [HarmonyPatch(typeof(MyGravityGeneratorBase), "CreateFieldShape")]
    // public static class CreateFieldShapePatch
    // {
    //     static bool Prefix(MyGravityGeneratorBase __instance, ref HkBvShape __result)
    //     {
    //         __result = new HkBvShape();
    //         return false;
    //     }
    // }

    [HarmonyPatch(typeof(MyGravityProviderSystem), "AddGravityGenerator")]
    public static class AddGravityGeneratorPatch
    {
        static bool Prefix(IMyGravityProvider gravityGenerator)
        {
            return false;
        }
    }
    [HarmonyPatch(typeof(MyGravityProviderSystem), "RemoveGravityGenerator")]
    public static class RemoveGravityGeneratorPatch
    {
        static bool Prefix(IMyGravityProvider gravityGenerator)
        {
            return false;
        }
    }
    [HarmonyPatch(typeof(MyGravityProviderSystem), "OnGravityGeneratorMoved")]
    public static class OnGravityGeneratorMovedPatch
    {
        static bool Prefix(IMyGravityProvider gravityGenerator, ref Vector3 velocity)
        {
            return false;
        }
    }


    [HarmonyPatch(typeof(MyVirtualMass), "Init")]
    public static class VirtualMassInit_Patch
    {

        public static Action<IMyVirtualMass> Trigger = delegate { };

        static void Prefix(MyVirtualMass __instance, MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
        {
            __instance.Physics = null;
            Trigger?.Invoke(__instance);
        }
    }
    [HarmonyPatch(typeof(MySpaceBall), "Init")]
    public static class SpaceBallInit_Patch
    {

        public static Action<IMyVirtualMass> Trigger = delegate { };

        static void Prefix(MySpaceBall __instance, MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
        {
            __instance.Physics = null;
            Trigger?.Invoke(__instance);
        }
    }
    [HarmonyPatch(typeof(MyGravityGeneratorBase), "UpdateBeforeSimulation")]
    public static class GravityGeneratorUpdate_Patch
    {
        

        static bool Prefix(MyGravityGeneratorBase __instance)
        {
            return false;
        }
    }
    
    
    
    [HarmonyPatch(typeof(MyGravityGeneratorBase), "Init")]
    public static class GravityInitPatch
    {
        public static event Action<MyGravityGeneratorBase, MyObjectBuilder_CubeBlock, MyCubeGrid>
            Trigger = delegate { };

        public static event Action<MyGravityGeneratorBase, MyCubeGrid, MyCubeGrid> GridChanged = delegate { };

        static void Prefix(MyGravityGeneratorBase __instance, MyObjectBuilder_CubeBlock objectBuilder,
            MyCubeGrid cubeGrid)
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

        static void Postfix(MyGravityGeneratorBase __instance)
        {
            __instance.Physics = null;
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