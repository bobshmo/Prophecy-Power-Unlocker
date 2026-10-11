# Prophecy Power Unlocker

**ACTargetTPPLimit** and **ACMaxGPULimit** are on Compatibility. Scan laptop / GPU reads the current PCF values and looks for explicit firmware default and upper-bound metadata in watts. Valid reported bounds enable adjustment from stock to detected maximum. If no valid bounds are advertised, adjustment is unavailable. Current targets and GPU-model tables are not treated as detected platform maxima. Apply rechecks identity/range and verifies PCF readback.

## Resolving an unknown VBIOS

This local package includes the Blackwell `nvflashb.exe` release from https://github.com/WillyBilly06/NVFLASH/releases/tag/Release in `tools`. Click the automatic VBIOS resolver to use it. If it is missing, select `nvflashb.exe` or `nvflash64.exe` when prompted. The app remembers its location, uses read-only ROM dump commands for resolution, and validates the dump against your GPU/VBIOS. You can also select your own GPU's ROM directly. Local driver/VBIOS validation persists in `%ProgramData%\ProphecyPowerUnlocker\validation`; no other user's validation is bundled.

Windows control panel combining NVIDIA laptop power control and telemetry, HP Victus platform controls, and optional mVolt profile import.

[Download the Windows release](https://github.com/bobshmo/Prophecy-Power-Unlocker/releases/latest)

## Features

- RTX 40/50-series **laptop** detection: 11 models, 22 PCI device IDs. See [GPU mapping](docs/GPU-SUPPORT.md).
- NVIDIA MAX override, runtime CURRENT power target, driver/VBIOS validation, restore controls, and telemetry.
- NVIDIA clock and voltage tuning where the installed driver exposes the controls.
- HP Victus Fan Max with readback. HPCM performance mode is requested immediately before each NVIDIA CURRENT write on Victus; rejection stops the write.
- Separate CPU sustained/STAPM, Fast PPT, Slow PPT, APU Slow, skin power and temperature controls for Ryzen AI 7 350, Family 26 Model 96.
- Platform PCF controls on Compatibility, labelled ACTargetTPPLimit and ACMaxGPULimit, plus HP PLGPU controls on CPU / HP.
- Optional official mVolt integration: select its executable, import your own saved v3 profile, review and apply it against a matching adapter/VBIOS.

No personal overclock profile, saved tuning settings, ROM dump, resolver cache or registry backup is included. Clock offsets and automatic reapplication start disabled.

## Run

1. Extract the entire release ZIP to a writable folder. Keep its DLLs and `drivers` folder alongside the executable.
2. Run **Prophecy Power Unlocker.exe** as administrator. Windows 10/11 x64 and an installed NVIDIA driver with `nvidia-smi` are required.
3. Open **NVIDIA power & telemetry**, inspect compatibility and validate the driver when requested. Driver validation performs a no-op CURRENT write at the existing power limit.
4. To resolve VBIOS, select a ROM saved from the same physical GPU, or supply `tools/nvflash64.exe` and use the automatic read-only dump. NVFlash is not bundled. The app does not flash GPU firmware.
5. Save a supported MAX override, reboot, and verify live MAX before applying a higher CURRENT target. A saved override is not proof that the driver accepted it after reboot.

NVIDIA controls use the installed NVIDIA driver; no custom unsigned NVIDIA driver or Windows test-signing mode is required. CPU SMU controls separately require the official [PawnIO installation](https://pawnio.eu/). HP controls appear only on HP Victus. The CPU gate remains limited to the processor listed above.

One NVIDIA GPU is supported per system; an AMD/Intel integrated GPU alongside it is fine. Multiple NVIDIA adapters are rejected when identity is ambiguous. Desktop GPUs are outside this release's scope.

## Optional mVolt

Select an existing official `mVolt+.exe`, or run `setup-mvolt.ps1` next to this application to download the latest [official mVolt release](https://github.com/b00nz/mVolt/releases/latest). The downloader checks GitHub's SHA-256 asset digest before installing it. It neither starts mVolt nor applies tuning.

Import a profile from your own mVolt profile store. Imported data stays in memory. The saved profile must remain unchanged, and its adapter and VBIOS must match before applying. mVolt itself manages its own files and settings. Use **Open mVolt** for its full editor. Latest version checked during release preparation: v0.47.3.

## Compatibility and validation

PCI mapping identifies models; it does **not** establish that every laptop can unlock to the listed target. Firmware, cooling, power delivery, driver policy and OEM configuration still matter. The inherited target ranges are experimental software bounds, not recommended settings. Failed driver/VBIOS/policy checks remain blocking checks.

Automated checks cover all 22 mappings, mismatched/unknown identities, MAX-versus-CURRENT routing, HPCM ordering and failure handling, and CPU field limits. The integrated backend was previously exercised on an RTX 5060 Laptop; the expanded models have not all been hardware-tested. A successful mailbox command is not CPU power-limit readback.

## Build

Install a .NET SDK capable of targeting .NET 8 on Windows, then run:

```powershell
.\build.ps1
```

This restores dependencies, builds, runs hardware-free self-tests, and publishes a self-contained Windows x64 folder and ZIP under `artifacts`. No GPU setting is changed by the tests. Bundled managed libraries remain separate and replaceable.

The original NVIDIA backend is credited to [timmyy123/nvidia-power-control](https://github.com/timmyy123/nvidia-power-control), with local fixes for active registry mapping, VBIOS identity handling, telemetry, integration and laptop mapping. HP/CPU access uses [OmenCore](https://github.com/theantipopau/omencore). See [third-party notices](THIRD-PARTY-NOTICES.md) for component provenance and licensing; this repository does not apply a blanket license to third-party code.

## Firmware controls

Compatibility includes Backup ROM, Import ROM, and Flash imported ROM. Import checks NVIDIA PCI images against the detected GPU. Flash requires confirmation, rechecks GPU/file identity, retains a before-flash backup, and opens the interactive NVFlashB window. The app adds no board-ID bypass or protection-disable flags. A successful utility exit is not independent verification of the activated firmware. Firmware flashing has not been tested on physical hardware during development.

## Unified controls and HP WMI scan

One NVIDIA-style navigation bar contains Power, Tuning, Telemetry, Compatibility, CPU / HP, Profiles and Settings. CURRENT accepts positive whole-watt values up to live MAX in 1 W steps. HP WMI scanning reports thermal policy and Extreme/Unleashed support/unlock flags. Its toggle applies Legacy Performance for policy V0 or Performance for V1+, and prepares subsequent Victus CURRENT writes. This is a firmware-compatible recommendation, not a measurement of optimal performance. An unverified Unleashed command is not exposed. Actual hardware behavior of the new automatic PCF and WMI toggles remains pending confirmation.
