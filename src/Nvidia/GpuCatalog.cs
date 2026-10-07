using System;
using System.Collections.Generic;
namespace NvpwrControlBlackwell {
internal static class GpuCatalog {
    // NVIDIA supportedchips 580.126.18; see docs/GPU-SUPPORT.md.
    internal static readonly Dictionary<int,string> Devices = new Dictionary<int,string> {
        [0x2717] = "NVIDIA GeForce RTX 4090 Laptop GPU",
        [0x2757] = "NVIDIA GeForce RTX 4090 Laptop GPU",
        [0x27A0] = "NVIDIA GeForce RTX 4080 Laptop GPU",
        [0x27E0] = "NVIDIA GeForce RTX 4080 Laptop GPU",
        [0x2820] = "NVIDIA GeForce RTX 4070 Laptop GPU",
        [0x2860] = "NVIDIA GeForce RTX 4070 Laptop GPU",
        [0x28A0] = "NVIDIA GeForce RTX 4060 Laptop GPU",
        [0x28A1] = "NVIDIA GeForce RTX 4050 Laptop GPU",
        [0x28E0] = "NVIDIA GeForce RTX 4060 Laptop GPU",
        [0x28E1] = "NVIDIA GeForce RTX 4050 Laptop GPU",
        [0x2C18] = "NVIDIA GeForce RTX 5090 Laptop GPU",
        [0x2C19] = "NVIDIA GeForce RTX 5080 Laptop GPU",
        [0x2C58] = "NVIDIA GeForce RTX 5090 Laptop GPU",
        [0x2C59] = "NVIDIA GeForce RTX 5080 Laptop GPU",
        [0x2D18] = "NVIDIA GeForce RTX 5070 Laptop GPU",
        [0x2D19] = "NVIDIA GeForce RTX 5060 Laptop GPU",
        [0x2D58] = "NVIDIA GeForce RTX 5070 Laptop GPU",
        [0x2D59] = "NVIDIA GeForce RTX 5060 Laptop GPU",
        [0x2D98] = "NVIDIA GeForce RTX 5050 Laptop GPU",
        [0x2DD8] = "NVIDIA GeForce RTX 5050 Laptop GPU",
        [0x2F18] = "NVIDIA GeForce RTX 5070 Ti Laptop GPU",
        [0x2F58] = "NVIDIA GeForce RTX 5070 Ti Laptop GPU",
    };
    internal static string Name(int id) { string name; return Devices.TryGetValue(id, out name) ? name : null; }
}
}
