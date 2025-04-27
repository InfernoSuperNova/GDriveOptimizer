using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using HarmonyLib;
using NLog.Fluent;
using Sandbox.Game.Entities;
using SpaceEngineers.Game.Entities.Blocks;
using Torch;
using Torch.API;
using Torch.API.Plugins;
using Torch.Views;

namespace GDriveOptimizer;

public class Plugin : TorchPluginBase, IWpfPlugin
{
    private Persistent<Config> _config = null!;

    public override void Init(ITorchBase torch)
    {
        base.Init(torch);
        _config = Persistent<Config>.Load(Path.Combine(StoragePath, "GDriveOptimizer.cfg"));
        
        
    }

    public UserControl GetControl() => new PropertyGrid
    {
        Margin = new(3),
        DataContext = _config.Data
    };

    public override void Update()
    {
        DeltaWingGravitySystem.Update();
    }
}
