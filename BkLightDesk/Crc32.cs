namespace BkLightDesk;

public static class Crc32
{
    private static readonly uint[] Table;
    static Crc32()
    {
        uint poly = 0xedb88320;
        Table = new uint[256];
        for (uint i = 0; i < 256; ++i)
        {
            uint temp = i;
            for (int j = 8; j > 0; --j)
                if ((temp & 1) == 1) temp = (temp >> 1) ^ poly;
                else temp >>= 1;
            Table[i] = temp;
        }
    }
    public static uint Compute(byte[] bytes)
    {
        uint crc = 0xffffffff;
        foreach (byte b in bytes)
        {
            byte index = (byte)((crc & 0xff) ^ b);
            crc = (crc >> 8) ^ Table[index];
        }
        return ~crc;
    }
}