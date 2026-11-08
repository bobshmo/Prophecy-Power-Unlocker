# Power recovery

The platform controller's PCF budget, HP GPU performance state and NVIDIA CURRENT/MAX are separate parts of the power policy. Their readbacks can look correct while actual GPU draw remains lower. The recovery controls coordinate those settings and provide a separate way to observe loaded behavior.

## Restore Defaults on CPU / HP

This control restores the supported CPU's tested default values and HP concurrent CPU setting, requests HP's default mode, and then releases the PCF power overrides. It reads the returned OEM TPP/GPU budget instead of retaining the previous custom budget. On the tested Victus, the known-working default is TPP 125 W, GPU cap 100 W, GPU DEFAULT 80 W and MIN 70 W, with PLGPU 35 W and HPCM disabled. These values are observations of that platform, not a universal laptop preset. Other PCF budgets come from the controller after the reset. Advanced clock/voltage tuning still has its own restore controls.

## Recover GPU power

Use recovery while the GPU is idle. The app requires AC power, CPU load below 25%, GPU load no greater than 10%, a readable GPU temperature no greater than 65°C and a validated NVIDIA CURRENT write path.

Recovery captures the known PCF power fields, Dynamic Boost state and live NVIDIA CURRENT/MAX. It prepares the HP GPU performance state, reads that mode's PCF policy, then establishes the selected TPP budget before the PCF GPU cap and NVIDIA CURRENT. Without a selected TPP budget, the previous TPP target is retained and must provide sufficient headroom. An existing prepared GPU cap above the requested target is retained. Recovery does not write CPU limits, HP concurrent CPU power, clock offsets or an overclock profile.

HP's mode switch can change GPU MIN and DEFAULT as well as TPP and MAX. Recovery retains MIN/DEFAULT from the prepared mode instead of converting the previous mode's base values into explicit overrides. A measured default-to-Performance transition changed TPP/default/min/max from115/80/70/90 W to125/90/80/100 W. This is a platform observation, not a universal preset. A regression test reproduces that transition and verifies recovery does not write the old base values back.

The requested CURRENT must be at or below **live** NVIDIA MAX. A saved MAX override is not proof that the driver loaded it. If a driver/display restart drops live MAX, reboot and recheck MAX before applying a higher CURRENT. Recovery does not bypass this check or claim that a display restart reloads the saved MAX override.

The app verifies PCF fields, Dynamic Boost and MAX after applying. E633 completion confirms request acceptance; the public GPU ceiling is not a verified getter for source F7. A mismatch is reported as accepted but unverified, without manufacturing a rollback from telemetry. PCF failures restore known PCF/boost fields. Source-specific request restoration is unavailable on this native path. Use loaded verification to assess actual draw.

## Release PCF overrides

Releasing PCF overrides asks the platform controller to return its overrides to the OEM policy and then reads the resulting budget. The returned values are device-specific; release is not a fixed 100 W or 115 W preset. It does not write the saved NVIDIA MAX override or CPU tuning.

Release can change the platform budget and its effect on the GPU. Read the resulting PCF and NVIDIA state before applying another target. This control is separate from deleting a saved NVIDIA MAX override.

## Verify under load

Run the same game or benchmark preset used for comparison and use the read-only loaded verification control. It observes actual GPU power, utilization, temperature and the reported CURRENT target without changing limits. A sufficiently loaded GPU is needed to compare draw with the target; an idle or partly loaded sample is inconclusive.

Keep the configured limit and measured draw separate. For example, CURRENT 140 W with approximately 70 W draw demonstrates that the workload did not reach the configured target during the observation. It does not identify TPP, PCF, CPU tuning or thermals as the cause by itself.

## Retesting after recovery

Acceptance caches include the device identity and companion power context. Old cache protocols or changed controller/power state are not reused. If adjustment becomes unavailable after a restart or policy change, inspect the discovery diagnostic and run the relevant idle acceptance test again.

Firmware-advertised maxima, accepted candidate ranges and loaded observations have different meanings. A test ceiling of 180 W is a chosen search endpoint, not a discovered ACTargetTPPLimit maximum. A readback-matched PCF cap is not a measured GPU draw guarantee. See [PCF GPU baseline and tester](PCF-GPU-TESTER.md) for the discovery and restoration method.

## Coordinated recovery and GPS target (1.2.9 development)

Recovery can take an explicit selected TPP budget. It establishes that budget before the PCF GPU cap and E633 request; a shared budget at or below the GPU target is rejected before writes. The selected budget must be tested on the laptop. No universal CPU subtraction formula or 180 W hardware maximum is inferred.

The original HP 8DD2/F.15 ACPI GM22 writes byte 3 of the four-byte GPU power payload to PEGP.GPST. GM21 returns it, and the GPU GPS DSM returns GPST as TGPU. The previous zero-padding assumption was wrong for this board. GPU preparation now reads and preserves the nonzero target before mode changes. A zero target on this exact board/BIOS is restored to its ACPI initial 87 C value and verified with raw GET. Other board/BIOS combinations do not receive that assumed factory repair. Existing lower nonzero OEM targets remain unchanged; this does not implement HP's chassis-temperature control loop.

The private board status GET can expose a source F7 contribution even when ClientGET F7 is unsupported. Its version, returned masks, bounded source count and unique source marker are validated before interpreting it. Explicit E633 applications report source contribution separately from the public ceiling and measured draw; this status does not establish override ownership, so PCF scans still do not replay it as a prior authored request.

Local hardware investigation confirmed TPP180/PCF140/E633140 readbacks but has not yet demonstrated restored140 W draw in the previously working FurMark preset. GPS repair, a successful NVML clock reset, separate device restarts and a same-value original-order replay did not independently restore that draw. An experimental DEFAULT115 request clamped to100 and was restored; it is not used by recovery. Loaded samples have shown different power/clock limiting states despite matching power targets. Do not describe this build as a verified140 W fix until measured under load.
