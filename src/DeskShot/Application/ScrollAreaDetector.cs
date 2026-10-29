namespace DeskShot.Application;

public static class ScrollAreaDetector
{
    // Solo propone un rectángulo: la sesión exige después una traslación única antes de aceptarlo.
    public static Int32Rect? Detect(BitmapSource first, BitmapSource next)
    {
        int width = first.PixelWidth, height = first.PixelHeight;
        if (next.PixelWidth != width || next.PixelHeight != height) return null;
        byte[] Read(BitmapSource image)
        {
            var source = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
            byte[] bytes = new byte[checked(width * height * 4)]; source.CopyPixels(bytes, width * 4, 0); return bytes;
        }
        var a = Read(first); var b = Read(next);
        int[] rows = new int[height], columns = new int[width];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int p = (y * width + x) * 4;
            if (a[p] == b[p] && a[p + 1] == b[p + 1] && a[p + 2] == b[p + 2]) continue;
            rows[y]++; columns[x]++;
        }
        int top = Array.FindIndex(rows, r => r >= 2), bottom = Array.FindLastIndex(rows, r => r >= 2);
        int left = Array.FindIndex(columns, c => c >= 2), right = Array.FindLastIndex(columns, c => c >= 2);
        if (top < 0 || bottom - top + 1 < 100 || left < 0 || right - left + 1 < 64) return null;
        var candidate = new Int32Rect(left, top, right - left + 1, bottom - top + 1);
        // Evita aceptar movimiento del scrollbar, un cursor o un elemento animado como documento.
        if ((long)candidate.Width * candidate.Height < (long)width * height / 8) return null;
        try
        {
            int Shift(Int32Rect rect)
            {
                var verifier = new ScrollStitcher(Crop(first, rect));
                var result = verifier.Append(Crop(next, rect));
                return result.Status == StitchStatus.Added ? result.AddedRows : 0;
            }
            int shift = Shift(candidate);
            if (shift == 0)
            {
                // Una barra de desplazamiento cambia, pero no se traslada con el contenido.
                int bandWidth = Math.Min(candidate.Width, Math.Max(64, candidate.Width / 4));
                var votes = new List<int>();
                for (int i = 0; i < 4; i++)
                {
                    int x = candidate.X + (candidate.Width - bandWidth) * i / 3;
                    int vote = Shift(new Int32Rect(x, candidate.Y, bandWidth, candidate.Height));
                    if (vote > 0) votes.Add(vote);
                }
                var groups = votes.GroupBy(v => v).OrderByDescending(g => g.Count()).ToArray();
                if (groups.Length != 1 || groups[0].Count() < 2) return null;
                shift = groups[0].Key;
            }
            int overlap = candidate.Height - shift;
            // Escoger el bloque continuo de columnas que sigue la traslación validada.
            int bestLeft = -1, bestWidth = 0, start = candidate.X;
            for (int x = candidate.X; x <= candidate.X + candidate.Width; x++)
            {
                int equal = 0;
                if (x < candidate.X + candidate.Width)
                    for (int y = candidate.Y; y < candidate.Y + overlap; y++)
                    {
                        int p = ((y + shift) * width + x) * 4, q = (y * width + x) * 4;
                        if (a[p] == b[q] && a[p + 1] == b[q + 1] && a[p + 2] == b[q + 2]) equal++;
                    }
                if (x == candidate.X + candidate.Width || equal < overlap * .98)
                {
                    if (x - start > bestWidth) { bestLeft = start; bestWidth = x - start; }
                    start = x + 1;
                }
            }
            if (bestWidth < 64) return null;
            var trimmed = new Int32Rect(bestLeft, candidate.Y, bestWidth, candidate.Height);
            return Shift(trimmed) == shift ? trimmed : null;
        }
        catch (ArgumentException) { return null; }
    }
    public static BitmapSource Crop(BitmapSource image, Int32Rect region)
    { var crop = new CroppedBitmap(image, region); crop.Freeze(); return crop; }
}
