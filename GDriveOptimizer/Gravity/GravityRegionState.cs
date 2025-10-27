namespace GDriveOptimizer.Gravity;

public enum GravityRegionState : byte
{
    Valid,          // Up to date
    Dirty,          // Needs recomputation (e.g., strength change)
    LazyInvalid,    // Must be cleaned up, but not urgently
    Invalid,        // Generator count changed or deleted, must be cleaned up, cannot be reused
}