using Torch;

namespace GDriveOptimizer;

public class Config : ViewModel
{
    public static long FramesToDeleteUntouchedRegions { get; set; } = 21600; // 10 minutes
}