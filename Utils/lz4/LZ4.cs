using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RainMeadow.lz4;

public static class LZ4
{
    public static byte[] CompressBytes(byte[] input)
    {
        int outputSize = LZ4_compressBound(input.Length);
        byte[] output = new byte[outputSize];
        var result = LZ4_compress_default(input, output, input.Length, outputSize);
        if (result < 0) throw new Exception("Compression failed!");
        return output;
    }

    public static byte[] DecompressBytes(byte[] input)
    {
        byte[] output = new byte[input.Length];
        int outputSize = LZ4_decompress_safe(input, output, input.Length, output.Length);
        if (outputSize < 0) throw new Exception("Decompression failed!");
        Array.Resize(ref output, outputSize);
        return output;
    }

    [DllImport("liblz4.dll")]
    private static extern int LZ4_compressBound(int size);
    [DllImport("liblz4.dll")]
    private static extern int LZ4_compress_default(byte[] src, byte[] dst, int srcSize, int dstCapacity);
    [DllImport("liblz4.dll")]
    private static extern int LZ4_decompress_safe(byte[] src, byte[] dst, int compressedSize, int dstCapacity);
}
