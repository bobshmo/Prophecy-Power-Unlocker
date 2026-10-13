# PCF GPU baseline and tester

Compatibility includes the restored **ACMaxGPULimit — PCF Max (HP unlock)** toggle. **Detect / enable HP unlock** reads the device and prepares its HP performance mode before applying a restricted baseline. Other manufacturers require a confirmation dialog. The resolved board VBIOS stock maximum takes precedence over a higher GPU-model baseline. This keeps the HP OMEN 115 W board at a 115 W starting target rather than assuming the generic RTX 5070 Ti 140 W target. Without a resolved VBIOS, the GPU-model target is explicitly labeled as a fallback.

The toggle does not require the BIOS to advertise PCF minimum/maximum metadata. Disabling it restores the saved original PCF GPU cap. NVIDIA CURRENT/MAX and ACTargetTPPLimit are separate controls; the toggle does not increase either of them. A successful PCF readback is not proof of sustained draw.

**Test all PCF GPU values** requires AC power, CPU load below 25%, GPU load no greater than 10%, and a readable GPU temperature no greater than 65°C. It checks every whole-watt ACMaxGPULimit command from 0 through a firmware-advertised maximum. If none is advertised, it uses the supported GPU profile's test ceiling and explicitly reports **maximum not determined**. The test ceiling is a protective software bound, not a discovered hardware maximum. Zero is tested but may have special controller semantics; zero is not offered as a normal operating cap.

The tester records matched, clamped, and rejected targets, including gaps. It restores all four original PCF power fields after completion, cancellation, load changes, or failure, and verifies restoration. Restoration failure is reported as an error, and no successful result is saved. Run only while idle with other power-control applications closed.

Positive matching targets become adjustable in the ACMaxGPULimit control. Rejected gaps cannot be applied. Results are tied to the full laptop/GPU/BIOS/VBIOS/driver identity and the TPP at test time; changing TPP invalidates them. Results are saved under `%ProgramData%\ProphecyPowerUnlocker\validation\pcf-gpu-test-<identity hash>.json`. These results do not establish electrical/thermal safety or the highest target supported under load.

The original TPP calibration remains available alongside the GPU-cap tester. Local driver validation and exact GPU/VBIOS resolution are still required for NVIDIA MAX/CURRENT operations. Validation from another laptop is not included or reused.
