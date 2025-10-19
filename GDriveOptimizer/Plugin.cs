using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Windows.Controls;
using NLog.Fluent;
using Sandbox.Game.Entities;
using SpaceEngineers.Game.Entities.Blocks;
using Torch;
using Torch.API;
using Torch.API.Plugins;
using Torch.Views;
using NLog;

namespace GDriveOptimizer;

public class Plugin : TorchPluginBase, IWpfPlugin
{
    
    public static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private Persistent<Config> _config = null!;
    private long _frame;
    public static event Action<long> UpdateEvent = delegate { };
    
    public override void Init(ITorchBase torch)
    {
        
        base.Init(torch);
        _config = Persistent<Config>.Load(Path.Combine(StoragePath, "GDriveOptimizer.cfg"));
        GravityManager.Setup();
        
    }

    public UserControl GetControl() => new PropertyGrid
    {
        Margin = new(3),
        DataContext = _config.Data
    };

    public override void Update()
    {
        UpdateEvent(_frame++);
    }
}
