using System;
using System.Collections.Generic;
using System.Reflection;
using NLog;
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

namespace GDriveOptimizer
{
    [PatchShim]
    public static class Patches
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        internal static readonly MethodInfo playerSimulate =
            typeof(MyCharacter).GetMethod(nameof(MyCharacter.Simulate), BindingFlags.Instance | BindingFlags.Public)
            ?? throw new Exception("Failed to find patch method (playerSimulate)");

        internal static readonly MethodInfo playerSimulatePatch =
            typeof(Patches).GetMethod(nameof(PlayerSimulate), BindingFlags.Static | BindingFlags.Public)
            ?? throw new Exception("Failed to find patch method (playerSimulatePatch)");

        private static double _lastActionMicroseconds = 0;
        private static int dumbTimer = 0;

        public static bool PlayerSimulate(MyCharacter __instance)
        {
            //var result = GravityManager.Sample(__instance.WorldMatrix.Translation, null, false);
            return true;
        }

        public static void Patch(PatchContext ctx)
        {
            ctx.GetPattern(playerSimulate).Prefixes.Add(playerSimulatePatch);

            GravityGeneratorPropertyPatches.DoPatch(ctx);

            Log.Info("Torch patching successful maybe");
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

        private static FieldInfo _acceleration =
            typeof(MyGravityGeneratorBase).GetField("m_gravityAcceleration", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void DoPatch(PatchContext ctx)
        {
            var fieldSizeSetterMethod = typeof(MyGravityGenerator).GetProperty("FieldSize")?.GetSetMethod();
            if (fieldSizeSetterMethod != null)
                ctx.GetPattern(fieldSizeSetterMethod).Suffixes.Add(
                    typeof(GravityGeneratorPropertyPatches).GetMethod(nameof(FieldSizeSetterPrefix)));

            // If you want acceleration patches in future, same pattern can be applied
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
                if ((Sync<Vector3, SyncDirection.BothWays>)_fieldSize.GetValue((MyGravityGenerator)gravityGenEntity) != null)
                    sync.ValueChanged -= _fieldSizeGravityGens[(MyGravityGenerator)gravityGenEntity];
            };

            var f = 0f;
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
                if ((Sync<float, SyncDirection.BothWays>)_acceleration.GetValue((MyGravityGeneratorBase)gravityGenEntity) != null)
                    sync.ValueChanged -= _accelerationGravityGens[(MyGravityGeneratorBase)gravityGenEntity];
            };
        }
    }

    // Torch-style patches for GravityProviderSystem

    [PatchShim]
    public static class GravityProviderSystemPatches
    {
        internal static readonly MethodInfo AddMethod = typeof(MyGravityProviderSystem)
            .GetMethod(nameof(MyGravityProviderSystem.AddGravityGenerator));

        internal static readonly MethodInfo RemoveMethod = typeof(MyGravityProviderSystem)
            .GetMethod(nameof(MyGravityProviderSystem.RemoveGravityGenerator));

        internal static readonly MethodInfo MoveMethod = typeof(MyGravityProviderSystem)
            .GetMethod(nameof(MyGravityProviderSystem.OnGravityGeneratorMoved));

        internal static readonly MethodInfo TotalGravityInPointMethod = typeof(MyGravityProviderSystem)
            .GetMethod(nameof(MyGravityProviderSystem.CalculateTotalGravityInPoint));

        public static void Patch(PatchContext ctx)
        {
            ctx.GetPattern(AddMethod).Prefixes.Add(typeof(GravityProviderSystemPatches).GetMethod(nameof(AddPrefix)));
            ctx.GetPattern(RemoveMethod).Prefixes.Add(typeof(GravityProviderSystemPatches).GetMethod(nameof(RemovePrefix)));
            ctx.GetPattern(MoveMethod).Prefixes.Add(typeof(GravityProviderSystemPatches).GetMethod(nameof(MovePrefix)));
            ctx.GetPattern(TotalGravityInPointMethod).Prefixes.Add(typeof(GravityProviderSystemPatches).GetMethod(nameof(CalculateTotalGravityInPointPrefix)));
        }

        public static bool AddPrefix(IMyGravityProvider gravityGenerator) => false;
        public static bool RemovePrefix(IMyGravityProvider gravityGenerator) => false;
        public static bool MovePrefix(IMyGravityProvider gravityGenerator, ref Vector3 velocity) => false;
        
        public static bool CalculateTotalGravityInPointPrefix(ref Vector3 __result, Vector3D worldPoint)
        {
            float naturalGravityMultiplier;
            var naturalGravity = MyGravityProviderSystem.CalculateNaturalGravityInPoint(worldPoint, out naturalGravityMultiplier);
            var artificialGravity = GravityManager.Sample(worldPoint);
            __result = naturalGravity + artificialGravity * (Config.I.AllowArtificialGravityInNaturalGravity ? 1 : MyGravityProviderSystem.CalculateArtificialGravityStrengthMultiplier(naturalGravityMultiplier) / 2); // I really really don't know why this needs to be divided by 2 but eh
            return false;
        }
    }
    
    
    // VirtualMass and SpaceBall Init patches
    [PatchShim]
    public static class VirtualMassPatches
    {
        // Explicitly specify parameter types to avoid AmbiguousMatchException
        internal static readonly MethodInfo VirtualMassInit = typeof(MyVirtualMass)
            .GetMethod(nameof(MyVirtualMass.Init),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { typeof(MyObjectBuilder_CubeBlock), typeof(MyCubeGrid) },
                null);

        internal static readonly MethodInfo SpaceBallInit = typeof(MySpaceBall)
            .GetMethod(nameof(MySpaceBall.Init),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { typeof(MyObjectBuilder_CubeBlock), typeof(MyCubeGrid) },
                null);

        // Event to notify when either VirtualMass or SpaceBall initializes
        public static event Action<IMyVirtualMass> Init = delegate { };

        public static void Patch(PatchContext ctx)
        {
            ctx.GetPattern(VirtualMassInit).Prefixes.Add(
                typeof(VirtualMassPatches).GetMethod(nameof(VirtualMassInitPrefix)));

            ctx.GetPattern(SpaceBallInit).Prefixes.Add(
                typeof(VirtualMassPatches).GetMethod(nameof(SpaceBallInitPrefix)));
        }

        public static void VirtualMassInitPrefix(MyVirtualMass __instance, MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
        {
            __instance.Physics = null; // disable physics for optimization
            Init?.Invoke(__instance);
        }

        public static void SpaceBallInitPrefix(MySpaceBall __instance, MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
        {
            __instance.Physics = null; // disable physics for optimization
            Init?.Invoke(__instance);
        }
    }

    // GravityGeneratorBase patches
    [PatchShim]
    public static class GravityGeneratorBasePatches
    {
        // Public methods
        internal static readonly MethodInfo UpdateMethod = typeof(MyGravityGeneratorBase)
            .GetMethod(nameof(MyGravityGeneratorBase.UpdateBeforeSimulation),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes, // no parameters
                null);

        internal static readonly MethodInfo InitMethod = typeof(MyGravityGeneratorBase)
            .GetMethod(nameof(MyGravityGeneratorBase.Init),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { typeof(MyObjectBuilder_CubeBlock), typeof(MyCubeGrid) },
                null);

        // Private methods
        internal static readonly MethodInfo ClosingMethod = typeof(MyGravityGeneratorBase)
            .GetMethod("Closing", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static readonly MethodInfo IsWorkingMethod = typeof(MyGravityGeneratorBase)
            .GetMethod("OnIsWorkingChanged", BindingFlags.Instance | BindingFlags.NonPublic);

        public static event Action<MyGravityGeneratorBase, MyObjectBuilder_CubeBlock, MyCubeGrid> Init = delegate { };
        public static event Action<MyGravityGeneratorBase, MyCubeGrid, MyCubeGrid> GridChanged = delegate { };
        public static event Action<MyGravityGeneratorBase> Closed = delegate { };
        public static event Action<MyGravityGeneratorBase, bool> WorkingChanged = delegate { };

        public static void Patch(PatchContext ctx)
        {
            ctx.GetPattern(UpdateMethod).Prefixes.Add(typeof(GravityGeneratorBasePatches)
                .GetMethod(nameof(UpdatePrefix)));

            ctx.GetPattern(InitMethod).Prefixes.Add(typeof(GravityGeneratorBasePatches)
                .GetMethod(nameof(InitPrefix)));

            ctx.GetPattern(InitMethod).Suffixes.Add(typeof(GravityGeneratorBasePatches)
                .GetMethod(nameof(InitPostfix)));

            ctx.GetPattern(ClosingMethod).Suffixes.Add(typeof(GravityGeneratorBasePatches)
                .GetMethod(nameof(ClosingPostfix)));

            ctx.GetPattern(IsWorkingMethod).Suffixes.Add(typeof(GravityGeneratorBasePatches)
                .GetMethod(nameof(IsWorkingPostfix)));
        }

    public static bool UpdatePrefix(MyGravityGeneratorBase __instance) => false;

    public static void InitPrefix(MyGravityGeneratorBase __instance, MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
    {
        Init?.Invoke(__instance, objectBuilder, cubeGrid);

        __instance.CubeGridChanged += (oldGrid) =>
        {
            var newGrid = __instance.CubeGrid;
            GridChanged?.Invoke(__instance, (MyCubeGrid)oldGrid, newGrid);
        };
    }

    public static void InitPostfix(MyGravityGeneratorBase __instance)
    {
        __instance.Physics = null;
    }

    public static void ClosingPostfix(MyGravityGeneratorBase __instance)
    {
        Closed?.Invoke(__instance);
    }

    public static void IsWorkingPostfix(MyGravityGeneratorBase __instance)
    {
        WorkingChanged?.Invoke(__instance, __instance.IsWorking);
    }
}

}
