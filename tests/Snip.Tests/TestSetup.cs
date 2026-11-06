using System.Runtime.CompilerServices;

namespace Snip.Tests;

static class TestSetup
{
    /// <summary>Tests log to a file of their own, never the real %LOCALAPPDATA%\Snip\snip.log.</summary>
    [ModuleInitializer]
    internal static void RedirectLog() => Log.FilePath = Path.Combine(Path.GetTempPath(), "snip-tests.log");
}
