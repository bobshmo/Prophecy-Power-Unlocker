namespace NvpwrControlBlackwell;

internal static class DriverValidationGuard
{
    internal static int NoopTarget(uint? sourceF7Mw, double? liveMaxW)
    {
        if (!sourceF7Mw.HasValue || sourceF7Mw.Value == 0 || sourceF7Mw.Value > 1_000_000 ||
            !liveMaxW.HasValue || !double.IsFinite(liveMaxW.Value) || liveMaxW.Value <= 0 ||
            liveMaxW.Value > 1000 || sourceF7Mw.Value > Math.Round(liveMaxW.Value * 1000) + 10)
            throw new InvalidOperationException("A bounded source F7 contribution and live NVIDIA MAX are required for driver no-op validation. No request was inferred from the public power ceiling.");
        return checked((int)sourceF7Mw.Value);
    }

    internal static void Test()
    {
        // Public ceiling can be fractional while the existing F7 request is 140 W.
        // No-op validation must retain the source value, not replay that ceiling.
        if (NoopTarget(140000, 140) != 140000 || NoopTarget(115000, 140) != 115000)
            throw new Exception("Driver no-op validation lost the source contribution.");
        foreach (uint? invalid in new uint?[] { null, 0, uint.MaxValue, 140011 })
        {
            bool rejected = false;
            try { NoopTarget(invalid, 140); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Unavailable/released/out-of-range F7 accepted for driver validation.");
        }
        foreach (double? invalid in new double?[] { null, 0, double.NaN, double.PositiveInfinity, 1001, 100 })
        {
            bool rejected = false;
            try { NoopTarget(140000, invalid); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid/MAX-incompatible no-op accepted.");
        }
    }
}
