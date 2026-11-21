using System.Runtime.InteropServices;

namespace PipelineCheck;

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hwnd);
}
