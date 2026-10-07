# Third-party components

Component rights remain with their respective authors. No blanket license is asserted for the combined repository.

## NVIDIA power-control backend

`src/Nvidia` derives from [timmyy123/nvidia-power-control](https://github.com/timmyy123/nvidia-power-control), revision `349fddfab04caa309f2671869613f8199825d686` (2.4.2 backend). Local changes integrate the suite, repair active PCI/registry association, capture VBIOS identity before dumping, serialize NVAPI sessions, fix process-output handling, add HP CURRENT preparation, and expand laptop mapping. The upstream snapshot did not supply a license file; none is invented here. Consult upstream for reuse terms.

## OmenCore

`lib/OmenCore.dll` comes from the local OmenCore 4.2 build used for the existing Victus controls. Upstream: [theantipopau/omencore](https://github.com/theantipopau/omencore), base revision `0da2438d6f04b3f9cfae1cfb3b4f21c68d3a6d2f`. Copyright 2024–2026 TheAntiPopAU; MIT notice in `licenses/OmenCore-MIT.txt`. The application uses its HP WMI/PCF and AMD SMU interfaces. It does not launch OmenCore's UI.

## PawnIO library and module

`lib/drivers/PawnIOLib.dll` and `RyzenSMU.bin` are the unmodified companion files from the OmenCore distribution. The kernel driver is not bundled or installed automatically.

- [PawnIOLib source](https://github.com/namazso/PawnIO/tree/master/PawnIOLib), copyright namazso, LGPL-2.1-or-later.
- [RyzenSMU module source and build files](https://github.com/namazso/PawnIO.Modules), copyright namazso, LGPL-2.1-or-later.
- License text: `licenses/PawnIO-Modules-LGPL.txt`.
- Official driver installation: [pawnio.eu](https://pawnio.eu/).

## NvAPIWrapper

NuGet package `LLT.NvAPIWrapper.Net` 1.1.24-pre.38, by Soroush Falahati and LenovoLegionToolkit-Team. [Source at package revision](https://github.com/LenovoLegionToolkit-Team/NvAPIWrapper/tree/77f074d6bb7f58d90dfe65cf3bf8e4ac9ceb6015). LGPL-3.0; see `licenses/NvAPIWrapper-LGPL.txt` and `licenses/GPL-3.0.txt`. Its DLL is separately replaceable in the portable distribution. Application source and build instructions are supplied to allow rebuilding against a modified library.

## Microsoft components

.NET 8 Windows Desktop runtime and Microsoft NuGet dependencies are restored by the project and included in self-contained releases under their respective licenses. Runtime license/notices files are preserved by publishing. Sources: [dotnet/runtime](https://github.com/dotnet/runtime) and [dotnet/wpf](https://github.com/dotnet/wpf).

## Optional external programs

[mVolt](https://github.com/b00nz/mVolt) is a separate application by b00nz. It is not included in this repository or release ZIP; the optional downloader fetches the official release unchanged and verifies its asset digest. User profiles are not distributed.

NVFlash and NVIDIA's installed driver libraries remain NVIDIA components. NVFlash, GPU ROMs, NVIDIA driver binaries, and private resolver caches are not bundled.
