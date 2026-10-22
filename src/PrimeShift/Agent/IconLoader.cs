using System.Buffers.Binary;

namespace PrimeShift.Agent;

internal static unsafe class IconLoader
{
    /// <summary>
    /// Create an HICON of the requested size from an .ico file embedded as a managed resource,
    /// picking the smallest image at least that large.
    /// </summary>
    public static nint Load(string resourceName, int size)
    {
        using var stream = typeof(IconLoader).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"缺少资源 {resourceName}");
        var ico = new byte[stream.Length];
        stream.ReadExactly(ico);

        // ICONDIR: reserved(2) type(2) count(2), then 16-byte ICONDIRENTRY records.
        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        (int Size, int Offset, int Length) best = default;
        for (var i = 0; i < count; i++)
        {
            var entry = ico.AsSpan(6 + i * 16, 16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            var better = best.Size == 0
                || (width >= size && (best.Size < size || width < best.Size))
                || (width < size && best.Size < size && width > best.Size);
            if (better)
            {
                best = (width, offset, length);
            }
        }

        fixed (byte* image = &ico[best.Offset])
        {
            return Win32.CreateIconFromResourceEx(image, (uint)best.Length, true, 0x00030000, size, size, Win32.LR_DEFAULTCOLOR);
        }
    }
}
