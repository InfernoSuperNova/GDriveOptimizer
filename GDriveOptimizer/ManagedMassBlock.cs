using System;
using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.ModAPI;

namespace GDriveOptimizer
{
    public static class VirtualMassRegistery
    {
        private static readonly Dictionary<MyVirtualMass, ManagedMassBlock> Map
            = new();

        public static void Register(MyVirtualMass mass, ManagedMassBlock wrapper)
            => Map[mass] = wrapper;

        public static void Unregister(MyVirtualMass mass)
            => Map.Remove(mass);

        public static bool TryGet(MyVirtualMass mass, out ManagedMassBlock wrapper)
            => Map.TryGetValue(mass, out wrapper);
    }
    
    public class ManagedMassBlock : IDisposable
    {
    
        private MyVirtualMass _mass;
        private MyCubeGrid  _grid;
        private GravityShip _ship;
    
        public ManagedMassBlock(MyVirtualMass mass, GravityShip ship)
        {
            _mass = mass;
            _grid = mass.CubeGrid;
            _ship = ship;

            // hook it up so our patches can find this instance later
            VirtualMassRegistery.Register(_mass, this);
        }
    
    
    
        public void Dispose()
        {
            VirtualMassRegistery.Unregister(_mass);
        }
        // terminal toggle
        public void OnEnabledChanged()
        {
            if (_mass.IsFunctional && _mass.ResourceSink.IsPoweredByType(MyResourceDistributorComponent.ElectricityId))
            {
                // Enabled toggled while has sufficient integrity and is powered
                _ship.Recalculate();
            }
        }
        // low power
        public void OnPowerStateChanged()
        {
            if (_mass.IsFunctional && _mass.Enabled)
            {
                // Power supply failed / kicked in while enabled and sufficient integrity
                _ship.Recalculate();
            }
        }
        // damage
        public void OnFunctionalChanged()
        {
            if (_mass.Enabled && _mass.ResourceSink.IsPoweredByType(MyResourceDistributorComponent.ElectricityId))
            {
                // Damaged beyond functionality / repaired while enabled and sufficient power supplied
                _ship.Recalculate();
            }
        }
    }
    
    
    [HarmonyPatch(typeof(MyVirtualMass), "OnEnabledChanged")]
    public static class OnEnabledChanged_Patch
    {
        static void Postfix(MyVirtualMass __instance)
        {
            if (VirtualMassRegistery.TryGet(__instance, out var wrapper))
                wrapper.OnEnabledChanged();
        }
    }
    [HarmonyPatch(typeof(MyVirtualMass), "Receiver_IsPoweredChanged")]
    public static class Receiver_IsPoweredChanged_Patch
    {
        static void Postfix(MyVirtualMass __instance)
        {
            if (VirtualMassRegistery.TryGet(__instance, out var wrapper))
                wrapper.OnPowerStateChanged();
        }
    }
    [HarmonyPatch(typeof(MyVirtualMass), "ComponentStack_IsFunctionalChanged")]
    public static class ComponentStack_IsFunctionalChanged_Patch
    {
        static void Postfix(MyVirtualMass __instance)
        {
            if (VirtualMassRegistery.TryGet(__instance, out var wrapper))
                wrapper.OnFunctionalChanged();
        }
    }
};

