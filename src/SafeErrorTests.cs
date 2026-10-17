using System.Diagnostics;
using System.Reflection;
using NvAPIWrapper.Native.Exceptions;
using NvAPIWrapper.Native.General;

internal static class SafeErrorTests
{
    private sealed class BrokenMessage : Exception
    {
        public override string Message => throw new InvalidOperationException("Message getter was called");
    }

    public static void Run()
    {
        var before = DriverModules();
        var native = (NVIDIAApiException)Activator.CreateInstance(typeof(NVIDIAApiException),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { (Status)(-1) }, null)!;
        var text = SafeError.Describe(native);
        if (!text.Contains("-1")) throw new Exception("Numeric NVIDIA status was lost.");
        if (!SafeError.Describe(new AggregateException(native)).Contains(text))
            throw new Exception("Aggregate NVIDIA status was lost.");
        if (!SafeError.Describe(new TargetInvocationException(native)).Contains(text))
            throw new Exception("Wrapped NVIDIA status was lost.");
        if (!SafeError.Details(native).Contains(text)) throw new Exception("Detailed logging lost the status.");
        if (SafeError.Describe(new Exception("ordinary error")) != "ordinary error")
            throw new Exception("Ordinary diagnostics changed.");
        if (!SafeError.Describe(new BrokenMessage()).Contains("BrokenMessage"))
            throw new Exception("Fallback diagnostics failed.");
        if (!before.SetEquals(DriverModules())) throw new Exception("Error formatting loaded NVIDIA native modules.");
    }

    private static HashSet<string> DriverModules() => Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
        .Where(m => m.ModuleName.StartsWith("nvapi", StringComparison.OrdinalIgnoreCase))
        .Select(m => m.ModuleName).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
