using System.Runtime.InteropServices;
namespace VictusPowerUnlockGui;
internal static class CpuLoadReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime {public uint Low,High;public ulong Value=>((ulong)High<<32)|Low;}
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle,out FileTime kernel,out FileTime user);
    internal static double Calculate(ulong idle0,ulong kernel0,ulong user0,ulong idle1,ulong kernel1,ulong user1)
    {
        if(idle1<idle0||kernel1<kernel0||user1<user0)throw new InvalidOperationException("CPU time counters moved backwards.");
        ulong total=(kernel1-kernel0)+(user1-user0),idle=idle1-idle0;
        if(total==0||idle>total)throw new InvalidOperationException("CPU time sample is unavailable.");
        return 100.0*(total-idle)/total;
    }
    internal static double Read()
    {
        if(!GetSystemTimes(out var idle0,out var kernel0,out var user0))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"CPU time counters could not be read.");
        Thread.Sleep(200);
        if(!GetSystemTimes(out var idle1,out var kernel1,out var user1))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"CPU time counters could not be read.");
        return Calculate(idle0.Value,kernel0.Value,user0.Value,idle1.Value,kernel1.Value,user1.Value);
    }
}
