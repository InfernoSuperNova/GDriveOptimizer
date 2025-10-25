using System.ComponentModel;
using Sandbox.Game.Entities;
using Torch;
using Torch.Views;

namespace GDriveOptimizer;

public class Config : ViewModel
{
    public static Config I;

    public Config()
    {
        I = this;
    }
    // [Display(Name = "Frames to cleanup untouched voxels", Description = "This controls how quickly old untouched regions will be cleaned up.")]
    // public long FramesToCleanupUntouchedVoxels { get; set; } = 21600; // 10 minutes
    //
    // [Display(Name = "Voxels to clean up per frame", Description = "How many voxels to try to run cleanup on.")]
    // public long MaxVoxelsToTryCleanupPerFrame { get; set; }

    [Display(Name = "Allow artificial gravity in natural gravity fields", Description = "If you like gravity drives on planets. Random fun option.")]
    public bool AllowArtificialGravityInNaturalGravity { get; set; } = false;
    
    
    [Display(Name = "Apply gravity force at center of mass", Description = "Makes gravity generators apply no torque, like thrusters.")]
    public bool ApplyForceAtCenterOfMass { get; set; } = false;
    
    
    [Display(Name = "Fix jump bug", Description = "Disables gravity for one frame after jumping to prevent a large torque to be applied which shreds subgrids.")]
    public bool FixJumpBug { get; set; } = true;
    



}