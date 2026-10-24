using System.Runtime.CompilerServices;
using DeskShot.Domain;

namespace DeskShot.Infrastructure;

public static class RedactionProcessor
{
    private sealed record CachedTile(Int32Rect Bounds, AnnotationTool Tool, int Size, BitmapSource Image);
    private static readonly ConditionalWeakTable<BitmapSource, Dictionary<Guid, CachedTile>> Cache = new();
    public static bool IsRedaction(AnnotationTool tool) => tool is AnnotationTool.Redact or AnnotationTool.Pixelate or AnnotationTool.Blur;

    public static Int32Rect Bounds(Annotation annotation, int width, int height)
    {
        var b = AnnotationGeometry.Bounds(annotation);
        return SelectionMath.FromPoints(b.TopLeft, b.BottomRight, width, height);
    }

    public static BitmapSource Filter(BitmapSource original, Annotation annotation, Int32Rect bounds)
    {
        var cache = Cache.GetValue(original, _ => []);
        lock (cache)
        {
            if (cache.TryGetValue(annotation.Id, out var tile) && tile.Bounds == bounds && tile.Tool == annotation.Tool && tile.Size == annotation.EffectSize)
                return tile.Image;
            var crop = new CroppedBitmap(original, bounds);
            var input = new FormatConvertedBitmap(crop, PixelFormats.Bgr32, null, 0);
            int width = bounds.Width, height = bounds.Height, stride = width * 4;
            byte[] pixels = new byte[checked(stride * height)]; input.CopyPixels(pixels, stride, 0);
            byte[] output = annotation.Tool == AnnotationTool.Pixelate ? Pixelate(pixels, width, height, annotation.EffectSize)
                : Blur(pixels, width, height, annotation.EffectSize);
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, output, stride); image.Freeze();
            // Una entrada por objeto, incluso mientras se redimensiona; memoria acotada.
            long cachedBytes = cache.Where(entry => entry.Key != annotation.Id).Sum(entry => (long)entry.Value.Image.PixelWidth * entry.Value.Image.PixelHeight * 4);
            if (cache.Count >= 16 && !cache.ContainsKey(annotation.Id) || cachedBytes + (long)width * height * 4 > 64_000_000) cache.Clear();
            cache[annotation.Id] = new(bounds, annotation.Tool, annotation.EffectSize, image);
            return image;
        }
    }

    private static byte[] Pixelate(byte[] input, int width, int height, int block)
    {
        byte[] result = new byte[input.Length];
        for (int top = 0; top < height; top += block)
        for (int left = 0; left < width; left += block)
        {
            int bottom = Math.Min(height, top + block), right = Math.Min(width, left + block);
            long b = 0, g = 0, r = 0; int count = (bottom - top) * (right - left);
            for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
            { int i = (y * width + x) * 4; b += input[i]; g += input[i + 1]; r += input[i + 2]; }
            for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
            { int i = (y * width + x) * 4; result[i] = (byte)(b / count); result[i + 1] = (byte)(g / count); result[i + 2] = (byte)(r / count); }
        }
        return result;
    }

    private static byte[] Blur(byte[] input, int width, int height, int radius)
    {
        // Tres pasadas de caja separables. Trabajo lineal, sin efectos de GPU.
        for (int pass = 0; pass < 3; pass++)
        {
            byte[] horizontal = new byte[input.Length], output = new byte[input.Length];
            for (int y = 0; y < height; y++)
            for (int c = 0; c < 3; c++)
            {
                long sum = 0;
                for (int x = 0; x < Math.Min(width, radius + 1); x++) sum += input[(y * width + x) * 4 + c];
                for (int x = 0; x < width; x++)
                {
                    horizontal[(y * width + x) * 4 + c] = (byte)(sum / (Math.Min(width - 1, x + radius) - Math.Max(0, x - radius) + 1));
                    if (x - radius >= 0) sum -= input[(y * width + x - radius) * 4 + c];
                    if (x + radius + 1 < width) sum += input[(y * width + x + radius + 1) * 4 + c];
                }
            }
            for (int x = 0; x < width; x++)
            for (int c = 0; c < 3; c++)
            {
                long sum = 0;
                for (int y = 0; y < Math.Min(height, radius + 1); y++) sum += horizontal[(y * width + x) * 4 + c];
                for (int y = 0; y < height; y++)
                {
                    output[(y * width + x) * 4 + c] = (byte)(sum / (Math.Min(height - 1, y + radius) - Math.Max(0, y - radius) + 1));
                    if (y - radius >= 0) sum -= horizontal[((y - radius) * width + x) * 4 + c];
                    if (y + radius + 1 < height) sum += horizontal[((y + radius + 1) * width + x) * 4 + c];
                }
            }
            input = output;
        }
        return input;
    }
}
