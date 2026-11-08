using System.Runtime.InteropServices;
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;

namespace VictusPowerUnlockGui;

internal sealed record PcfControllerCandidate(uint Index, uint LayoutVersion, PcfPowerValues? Values, string? Error = null);
internal sealed record PcfDiscoveryMetadata(uint ReportedMask, uint? MasterMask, PcfControllerCandidate[] Candidates,
    uint? SelectedIndex, string Reason, string? MasterDiagnostic = null);
internal sealed record PcfInformationBuffers(byte[] ControllerInfo, byte[]? MasterInfo, string? MasterDiagnostic = null);

/// <summary>
/// Selects an explicitly reported PCF controller by reading its known power fields.
/// Never substitutes controller zero after discovery fails. These queries do not discover TPP write bounds.
/// </summary>
internal static class PcfControllerDiscovery
{
    // Audited in NVIDIA 616.92's nvapi64_impl ControllerGetInfo implementation:
    // RVA 0x688C0/RM 0x01000401 copies the output bitmap to byte 0, tests each
    // bit, and writes valid=1 at 0x1C + index*0xC4. MasterGetInfo (0x6AC80,
    // RM 0x01000801) has its own bitmap and 0x190-stride MASTER entries.
    // The version/size checks below restrict decoding to these audited layouts.
    internal const uint ControllerInfoId = 0xB4C5D8BA;
    internal const uint ControllerInfoVersion = 0x0001189C;
    internal const int ControllerInfoSize = 0x189C;
    internal const int ControllerEntryOffset = 0x1C;
    internal const int ControllerEntryStride = 0xC4;
    internal const uint MasterInfoId = 0x1071E0D3;
    internal const uint MasterInfoVersion = 0x00033208;
    internal const int MasterInfoSize = 0x3208;
    private const uint MaximumPlausibleMilliwatts = 1_000_000;
    private static readonly object DiscoveryLock = new();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr QueryInterface(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadInformation(IntPtr buffer);

    internal static PcfPowerController Open()
    {
        lock (DiscoveryLock)
        {
            var metadata = ReadMetadata();
            if (metadata.SelectedIndex is not uint index)
                throw new InvalidOperationException(metadata.Reason);

            // Every probe has disposed its own session. Reopen the selected controller so
            // its session and startup snapshot remain alive for the calling operation.
            var controller = new PcfPowerController(index);
            try
            {
                if (!Plausible(controller.GetPowerValues()))
                    throw new InvalidOperationException("PCF power state changed during controller discovery. Read the platform again.");
                return controller;
            }
            catch
            {
                controller.Dispose();
                throw;
            }
        }
    }

    internal static PcfDiscoveryMetadata ReadMetadata()
    {
        lock (DiscoveryLock)
            return Discover(ReadInformationBuffers, ProbeController);
    }

    // Injectable reads keep selection, error handling and bitmap validation testable
    // without loading NVIDIA libraries or initializing a hardware session.
    internal static PcfDiscoveryMetadata Discover(Func<PcfInformationBuffers> readInformation,
        Func<uint, PcfControllerCandidate> probe)
    {
        uint controllerMask;
        uint? masterMask = null;
        string? masterDiagnostic;
        try
        {
            var information = readInformation();
            controllerMask = ReadControllerMask(information.ControllerInfo);
            masterDiagnostic = information.MasterDiagnostic;
            if (information.MasterInfo != null)
            {
                try { masterMask = ReadMasterMask(information.MasterInfo); }
                catch (InvalidOperationException ex) { masterDiagnostic = SafeError.Describe(ex); }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(0, null, [], null, "PCF controller information is unavailable: " + SafeError.Describe(ex));
        }

        var candidates = new List<PcfControllerCandidate>();
        for (uint index = 0; index < 32; index++)
        {
            if ((controllerMask & (1u << (int)index)) == 0) continue;
            try
            {
                var candidate = probe(index);
                if (candidate.Index != index)
                    candidate = new(index, 0, null, "Controller read returned a different index.");
                candidates.Add(candidate);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                candidates.Add(new(index, 0, null, SafeError.Describe(ex)));
            }
        }

        var usable = candidates.Where(c => c.Error == null && KnownLayout(c.LayoutVersion) && Plausible(c.Values)).ToArray();
        return usable.Length switch
        {
            1 => new(controllerMask, masterMask, candidates.ToArray(), usable[0].Index,
                $"PCF controller {usable[0].Index} uniquely identified from the driver's controller information and live power fields.", masterDiagnostic),
            0 => new(controllerMask, masterMask, candidates.ToArray(), null,
                "No reported PCF controller exposes a coherent supported power state. PCF writes are unavailable.", masterDiagnostic),
            _ => new(controllerMask, masterMask, candidates.ToArray(), null,
                "Multiple reported PCF controllers expose power fields. The active controller is ambiguous; PCF writes are unavailable.", masterDiagnostic)
        };
    }

    internal static uint ReadControllerMask(byte[] buffer)
    {
        if (buffer.Length != ControllerInfoSize || BitConverter.ToUInt32(buffer, 4) != ControllerInfoVersion)
            throw new InvalidOperationException("Unsupported PCF controller information layout.");
        uint mask = BitConverter.ToUInt32(buffer, 0);
        for (int index = 0; index < 32; index++)
        {
            bool reported = (mask & (1u << index)) != 0;
            byte valid = buffer[ControllerEntryOffset + index * ControllerEntryStride];
            if ((reported && valid != 1) || (!reported && valid == 1))
                throw new InvalidOperationException("PCF controller bitmap and valid entries disagree.");
        }
        return mask;
    }

    internal static uint ReadMasterMask(byte[] buffer)
    {
        if (buffer.Length != MasterInfoSize || BitConverter.ToUInt32(buffer, 4) != MasterInfoVersion)
            throw new InvalidOperationException("Unsupported PCF master information layout.");
        // This bitmap indexes MASTER entries, not controller entries. Do not use
        // its types or bits to choose a controller; their semantics are separate.
        return BitConverter.ToUInt32(buffer, 0);
    }

    internal static bool KnownLayout(uint version) => version is 0x00010C88 or 0x00021640;

    internal static bool Plausible(PcfPowerValues? values) => values != null &&
        values.ACTargetTPPLimitInMilliwatts is > 0 and <= MaximumPlausibleMilliwatts &&
        values.ACMinGPULimitInMilliwatts > 0 &&
        values.ACMinGPULimitInMilliwatts <= values.ACDefaultGPULimitInMilliwatts &&
        values.ACDefaultGPULimitInMilliwatts <= values.ACMaxGPULimitInMilliwatts &&
        values.ACMaxGPULimitInMilliwatts <= MaximumPlausibleMilliwatts;

    private static PcfControllerCandidate ProbeController(uint index)
    {
        using var controller = new PcfPowerController(index);
        return new(index, controller.LayoutVersion, controller.GetPowerValues());
    }

    private static PcfInformationBuffers ReadInformationBuffers()
    {
        bool initialized = false, modeInitialized = false;
        IntPtr library = IntPtr.Zero;
        try
        {
            NVIDIA.Initialize();
            initialized = true;
            PcfApi.InitializeMode1();
            modeInitialized = true;
            library = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "nvapi64.dll"));
            var query = Marshal.GetDelegateForFunctionPointer<QueryInterface>(NativeLibrary.GetExport(library, "nvapi_QueryInterface"));
            byte[] controllerInfo = ReadBuffer(query, ControllerInfoId, ControllerInfoVersion, ControllerInfoSize);
            byte[]? masterInfo = null;
            string? masterDiagnostic = null;
            try { masterInfo = ReadBuffer(query, MasterInfoId, MasterInfoVersion, MasterInfoSize); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { masterDiagnostic = SafeError.Describe(ex); }
            return new(controllerInfo, masterInfo, masterDiagnostic);
        }
        finally
        {
            // Do not leave this temporary metadata session alive, or unload the
            // returned controller's later session. Open creates that session afterward.
            if (modeInitialized) { try { PcfApi.UnloadMode1(); } catch { } }
            if (initialized) { try { PcfApi.UnloadNvApi(); } catch { } }
            if (library != IntPtr.Zero) NativeLibrary.Free(library);
        }
    }

    private static byte[] ReadBuffer(QueryInterface query, uint id, uint version, int size)
    {
        IntPtr function = query(id);
        if (function == IntPtr.Zero)
            throw new InvalidOperationException($"PCF information query 0x{id:X8} is unsupported.");
        const int extraBytes = 256;
        IntPtr allocation = Marshal.AllocHGlobal(size + extraBytes);
        try
        {
            Marshal.Copy(new byte[size + extraBytes], 0, allocation, size + extraBytes);
            Marshal.WriteInt32(allocation, 4, unchecked((int)version));
            int status = Marshal.GetDelegateForFunctionPointer<ReadInformation>(function)(allocation);
            if (status != 0)
                throw new InvalidOperationException($"PCF information query 0x{id:X8} returned status {status}.");
            var result = new byte[size];
            Marshal.Copy(allocation, result, 0, size);
            return result;
        }
        finally { Marshal.FreeHGlobal(allocation); }
    }
}
