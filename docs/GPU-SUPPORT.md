# Laptop GPU mapping

Source: [NVIDIA supportedchips 580.126.18](https://download.nvidia.com/XFree86/Linux-x86_64/580.126.18/README/supportedchips.html). All listed devices use PCI vendor 10DE.

| Laptop model | PCI device ID |
| --- | --- |
| RTX 4090 Laptop GPU | `2717` |
| RTX 4090 Laptop GPU | `2757` |
| RTX 4080 Laptop GPU | `27A0` |
| RTX 4080 Laptop GPU | `27E0` |
| RTX 4070 Laptop GPU | `2820` |
| RTX 4070 Laptop GPU | `2860` |
| RTX 4060 Laptop GPU | `28A0` |
| RTX 4050 Laptop GPU | `28A1` |
| RTX 4060 Laptop GPU | `28E0` |
| RTX 4050 Laptop GPU | `28E1` |
| RTX 5090 Laptop GPU | `2C18` |
| RTX 5080 Laptop GPU | `2C19` |
| RTX 5090 Laptop GPU | `2C58` |
| RTX 5080 Laptop GPU | `2C59` |
| RTX 5070 Laptop GPU | `2D18` |
| RTX 5060 Laptop GPU | `2D19` |
| RTX 5070 Laptop GPU | `2D58` |
| RTX 5060 Laptop GPU | `2D59` |
| RTX 5050 Laptop GPU | `2D98` |
| RTX 5050 Laptop GPU | `2DD8` |
| RTX 5070 Ti Laptop GPU | `2F18` |
| RTX 5070 Ti Laptop GPU | `2F58` |

These 22 mappings cover 11 laptop models. Unknown IDs and contradictory model/PCI identities are rejected. A known PCI ID can identify a model even if its display name is missing or generic.

This table verifies identification only. Driver transport validation, device-matched VBIOS resolution, OEM policy and readback still determine whether a power operation is available. No claim is made that every mapped laptop has been physically tested.
