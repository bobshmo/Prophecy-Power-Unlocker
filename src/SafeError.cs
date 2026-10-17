using NvAPIWrapper.Native.Exceptions;

// Error reporting must not call back into a failing or unloaded NVIDIA driver.
internal static class SafeError
{
    public static string Describe(Exception error) => Describe(error, 0);

    private static string Describe(Exception error, int depth)
    {
        if (depth >= 8) return error.GetType().Name;
        if (error is NVIDIAApiException nvidia)
            return $"NVIDIA API status {(int)nvidia.Status} ({nvidia.Status})";
        // AggregateException.Message formats its children, including NVIDIA exceptions.
        if (error is AggregateException aggregate)
            return "Multiple errors: " + string.Join("; ", aggregate.InnerExceptions.Select(e => Describe(e, depth + 1)));
        if (error is System.Reflection.TargetInvocationException && error.InnerException != null)
            return "Invocation failed: " + Describe(error.InnerException, depth + 1);
        try { return error.Message; }
        catch { return error.GetType().Name + $" (HRESULT 0x{error.HResult:X8})"; }
    }

    public static string Details(Exception error) =>
        error.GetType().FullName + ": " + Describe(error) + Environment.NewLine + error.StackTrace;
}
