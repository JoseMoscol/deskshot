namespace DeskShot.Application;

public enum StitchStatus { Added, Unchanged, Uncertain, LimitReached }
public sealed record StitchResult(StitchStatus Status, int AddedRows = 0);

// Unión por traslación vertical entera: nunca interpola, mezcla ni escala píxeles.
public sealed class ScrollStitcher
{
    public const long DefaultMaxPixels = 32_000_000;
    public const int MaxHeight = 60_000;
    private readonly long _maxPixels;
    private readonly List<byte[]> _strips = [];
    private ulong[] _previousHashes;
    public int Width { get; }
    public int ViewHeight { get; }
    public int Height { get; private set; }
    public int Frames { get; private set; } = 1;

    public ScrollStitcher(BitmapSource first, long maxPixels = DefaultMaxPixels)
    {
        Width = first.PixelWidth; ViewHeight = Height = first.PixelHeight; _maxPixels = maxPixels;
        if (Width < 64 || ViewHeight < 100) throw new ArgumentException("Selecciona un área de al menos 64 × 100 píxeles.");
        if ((long)Width * Height > maxPixels || Height > MaxHeight) throw new ArgumentException("El área inicial excede el límite de captura.");
        byte[] pixels = ReadPixels(first);
        _previousHashes = HashRows(pixels);
        _strips.Add(pixels);
    }

    public StitchResult Append(BitmapSource next)
    {
        if (next.PixelWidth != Width || next.PixelHeight != ViewHeight) throw new ArgumentException("El área cambió de tamaño durante la captura.");
        byte[] pixels = ReadPixels(next);
        ulong[] hashes = HashRows(pixels);
        // No basta con encontrar una franja blanca: exigimos variedad y un único desplazamiento.
        if (hashes.SequenceEqual(_previousHashes)) return new(StitchStatus.Unchanged);
        int minOverlap = Math.Max(80, ViewHeight / 3);
        var matches = new List<int>();
        for (int shift = 1; shift <= ViewHeight - minOverlap; shift++)
        {
            int overlap = ViewHeight - shift;
            int checkedRows = 0, equal = 0;
            for (int row = 0; row < overlap; row += Math.Max(1, overlap / 96))
            {
                checkedRows++;
                if (hashes[row] == _previousHashes[row + shift]) equal++;
            }
            if (equal < checkedRows * .98) continue;
            equal = 0;
            var distinct = new HashSet<ulong>();
            for (int row = 0; row < overlap; row++)
                if (hashes[row] == _previousHashes[row + shift]) { equal++; distinct.Add(hashes[row]); }
            if (equal >= overlap * .98 && distinct.Count >= 8) matches.Add(shift);
        }
        if (matches.Count != 1) return new(StitchStatus.Uncertain);
        int added = matches[0];
        if ((long)Width * (Height + added) > _maxPixels || Height + added > MaxHeight) return new(StitchStatus.LimitReached);
        var strip = new byte[checked(Width * added * 4)];
        Buffer.BlockCopy(pixels, (ViewHeight - added) * Width * 4, strip, 0, strip.Length);
        _strips.Add(strip); _previousHashes = hashes; Height += added; Frames++;
        return new(StitchStatus.Added, added);
    }

    public BitmapSource BuildImage()
    {
        byte[] pixels = new byte[checked(Width * Height * 4)];
        int offset = 0;
        foreach (byte[] strip in _strips) { Buffer.BlockCopy(strip, 0, pixels, offset, strip.Length); offset += strip.Length; }
        var image = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgr32, null, pixels, Width * 4);
        image.Freeze(); return image;
    }

    private static byte[] ReadPixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
        byte[] pixels = new byte[checked(source.PixelWidth * source.PixelHeight * 4)];
        converted.CopyPixels(pixels, source.PixelWidth * 4, 0); return pixels;
    }

    private ulong[] HashRows(byte[] pixels)
    {
        var hashes = new ulong[ViewHeight];
        int margin = Math.Min(16, Width / 16);
        for (int y = 0; y < ViewHeight; y++)
        {
            ulong hash = 14695981039346656037;
            for (int x = margin; x < Width - margin; x++)
            {
                int i = (y * Width + x) * 4;
                for (int channel = 0; channel < 3; channel++) hash = unchecked((hash ^ pixels[i + channel]) * 1099511628211);
            }
            hashes[y] = hash;
        }
        return hashes;
    }
}
