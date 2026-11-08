# PCF GPU baseline and tester

Compatibility contains **ACTargetTPPLimit**, **ACMaxGPULimit**, the **PCF Max (HP unlock)** baseline toggle, and separate acceptance tests. These are platform-controller controls. NVIDIA CURRENT and MAX remain separate GPU-driver controls.

## Per-device controller discovery

The app queries the installed driver's reported PCF controller topology and tests only the controller indices that it reports. It selects exactly one controller whose supported layout returns a coherent power budget. Missing, unsupported or ambiguous controllers stop the operation with a diagnostic; the app does not silently fall back to controller zero.

Controller discovery identifies the interface and its current values. It does not turn a current value into a supported range. Firmware-advertised bounds are used when available. An acceptance test's selected upper endpoint or GPU-profile ceiling is labeled separately from a detected firmware maximum.

## Baseline toggle

**Detect / enable HP unlock** prepares the detected HP GPU performance state before applying the baseline. Other manufacturers require a confirmation dialog. A resolved board VBIOS stock maximum takes precedence over a higher GPU-model baseline; an unresolved GPU-model target is labeled as a fallback.

The baseline is a minimum target for enabling this toggle. If the baseline is 115 W and the current PCF cap is already 140 W, the toggle reports enabled and enabling it leaves 140 W in place. It does not lower the cap to 115 W. Disabling an app-applied baseline restores the saved original PCF cap.

The toggle does not require firmware range metadata and does not itself raise NVIDIA CURRENT/MAX or ACTargetTPPLimit. A matching PCF readback confirms the configured field, not GPU power draw under load.

## Idle acceptance tests

**Test minimum → max** checks every whole-watt ACMaxGPULimit target from the controller-reported ACMinGPULimit through the selected **Test max (W)**, inclusively. Fractional minimum values round upward. A missing/released minimum or maximum below minimum stops before writes. A firmware-advertised maximum constrains the selected endpoint; otherwise the endpoint is a configured test ceiling, not a detected hardware maximum. No targets below the reported minimum are written.

Testing requires AC power, CPU load below 25%, GPU load no greater than 10%, a readable GPU temperature no greater than 65°C, and a validated NVIDIA CURRENT restoration path. Run while idle with other power-control applications closed.

For a low GPU-cap candidate, the tester also lowers the temporary PCF minimum/default fields as necessary to keep `minimum ≤ default ≤ maximum`. It never tests a maximum below unchanged, higher companion fields. TPP candidates retain the original GPU budget.

Calibration retains the active HP mode. After **each candidate**, it releases temporary PCF power overrides, then selectively reapplies the original values that differ from the returned OEM policy. This clears temporary low MIN/DEFAULT overrides instead of leaving them as explicit software limits. The app restores Dynamic Boost, verifies all four known PCF power fields and checks that live NVIDIA MAX is unchanged. Public CURRENT is observed as telemetry, not replayed through E633: the F7 source-specific getter is unavailable on the investigated driver. Cleanup also runs on completion, cancellation, a load change or failure. An unexpected MAX change or incomplete cleanup stops the test and prevents a successful cache from being saved. Getter/status buffers are not replayed as write commands.

Only fields that differ after the release are rewritten. The read interface does not expose prior override ownership, so reproducing an external application's exact private ownership state is not claimed. **Release PCF overrides** also provides an explicit controller reset outside calibration. GPU tester protocol 4 also records the tested minimum and invalidates old zero-based sweeps; TPP calibration retains protocol 3.

Matched positive targets become adjustable; clamped or rejected gaps cannot be applied. Manual applications also check companion state and attempt to restore the prior power state on failure.

Results are tied to laptop/GPU/BIOS/VBIOS/driver/controller identity and the companion power context, including TPP, GPU minimum/default, Dynamic Boost and NVIDIA MAX; observational CURRENT movement alone does not invalidate the result. A driver recovery or changed context requires retesting. Earlier cache protocols are not reused. GPU results are saved under `%ProgramData%\ProphecyPowerUnlocker\validation\pcf-gpu-test-<identity hash>.json`.

## Acceptance versus loaded verification

The original unlock experiment tested TPP candidates under load while holding the PCF GPU cap at 115 W and restoring the original state between candidates. TPP candidates of 125, 140 and 150 W produced enforced GPU limits of 93.91, 108.12 and 115 W respectively. The test stopped at 150 W after reaching its GPU target. The later 180 W preset was not a firmware-reported maximum or the discovered boundary of that first test.

The current idle acceptance tests answer whether a target matches controller readback. Loaded verification separately observes GPU utilization, actual GPU draw and the reported CURRENT target. A 140 W target paired with 70 W measured draw must not be reported as a working 140 W unlock. Insufficient GPU load cannot establish whether the target is reached.

Per-device discovery and testing make the method portable; they do not guarantee that every laptop can exceed its OEM cap. Accepted targets do not establish a physical maximum, sustained operation or electrical/thermal suitability. Local driver validation and exact GPU/VBIOS resolution remain required; another laptop's validation is not reused. See [Power recovery](POWER-RECOVERY.md) for restoring a coordinated state after a driver restart.
