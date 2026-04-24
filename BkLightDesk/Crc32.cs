using System;

namespace BkLightDesk;

/// <summary>
/// Provides a high-performance implementation of the CRC32 (Cyclic Redundancy Check) algorithm.
/// Used to generate checksums for data integrity verification in the BLE communication protocol.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table;

    static Crc32()
    {
        // Polynomial used by the IEEE 802.3 standard (standard for PNG and ZIP files)
        const uint polynomial = 0xEDB88320;
        Table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            uint entry = i;
            for (int j = 8; j > 0; j--)
            {
                if ((entry & 1) == 1)
                    entry = (entry >> 1) ^ polynomial;
                else
                    entry >>= 1;
            }
            Table[i] = entry;
        }
    }

    /// <summary>
    /// Computes the CRC32 checksum for the provided byte array.
    /// </summary>
    /// <param name="data">The byte data to process.</param>
    /// <returns>A 32-bit unsigned integer representing the checksum.</returns>
    public static uint Compute(byte[] data)
    {
        return Compute(data.AsSpan());
    }

    /// <summary>
    /// Computes the CRC32 checksum using ReadOnlySpan for optimized memory performance.
    /// </summary>
    /// <param name="data">The span of bytes to process.</param>
    /// <returns>A 32-bit unsigned integer representing the checksum.</returns>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte b in data)
        {
            byte index = (byte)((crc & 0xFF) ^ b);
            crc = (crc >> 8) ^ Table[index];
        }

        return ~crc;
    }
}