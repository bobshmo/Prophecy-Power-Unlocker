# NVIDIA error-reporting crash fix

The supplied PPU_Trace.txt identifies Prophecy 1.2.5, ScanPlatformPowerAsync, NVIDIAApiException.get_Message and GeneralApi.GetErrorMessage at the access violation. It does not contain the original NVIDIA status or establish why the scan failed.

REA artifact identity: ev_585106bed1b2730cd0951179cc0e6e2726dcf44296207d789336aac8c2b30601. NvAPIWrapper.dll SHA256: 90fbcd89714ab65343b3aa49853e7042419d694d8e8a1ef60d7bc12cf2c9855a.
REA member query ev_f7af9aaefe55687df40e06548b505818e87cf727155a4cffe66b2fe44d4988a6 could not admit metadata within its 64 MiB representation budget. It supplied no member-level evidence.

Fallback static analysis: installed ILSpy 10.1.0 decompiled NVIDIAApiException in that exact DLL. Message calls GeneralApi.GetErrorMessage(Status) before its enum fallback; Status is an auto-property assigned by its internal constructor. Static analysis is not a reproduction on the tester's laptop.

SafeError now formats NVIDIA status without calling Message or ToString. Aggregate and reflection wrappers are handled before their messages are accessed. Application exception display/logging paths use this helper, including the platform scanner. No driver validation gates or hardware setters were changed.

Hardware-free regression checks cover the real wrapper exception, aggregate and reflection wrappers, detailed logging, ordinary errors and a throwing Message getter. Error formatting must not add nvapi native modules to the process. Full application self-tests pass. The 616.92 failure itself has not been reproduced; retesting on the affected Legion remains required. This fix does not claim to cure other native driver access violations.
