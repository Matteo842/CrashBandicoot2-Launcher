namespace RecompOne.Runtime.Dispatch;

/// <summary>
/// Continuation token for MIPS <c>j</c> back into a mid-label of the poly rasterizer
/// (<c>func_800420F4</c>). Fragments incorrectly split out of that function use
/// <see cref="Jump"/> instead of <see cref="Dispatcher.Call"/>; the parent then
/// <c>goto</c>s the mid-label so the C# stack stays flat.
/// </summary>
public static class RasterContinue
{
    // Single-threaded guest CPU; plain static avoids ThreadStatic edge cases across pumps.
    static uint _addr;

    public static uint Addr => _addr;

    public static void Jump(uint addr) => _addr = addr;

    public static uint Take()
    {
        var a = _addr;
        _addr = 0;
        return a;
    }
}
