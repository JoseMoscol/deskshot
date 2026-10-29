namespace DeskShot.Application;

public enum PdfPaper { A4, Letter }
public sealed record PdfOptions(PdfPaper Paper = PdfPaper.A4, bool Landscape = false)
{
    public (double Width, double Height) PageSize
    {
        get
        {
            if (!Enum.IsDefined(Paper)) throw new ArgumentException("El papel no es válido.");
            var size = Paper == PdfPaper.A4 ? (595.276, 841.89) : (612d, 792d);
            return Landscape ? (size.Item2, size.Item1) : size;
        }
    }
}
public sealed record PdfCapture(string Name, BitmapSource Image);
public sealed record PdfPage(int CaptureIndex, Int32Rect Pixels, double Width, double Height);

public static class PdfLayout
{
    public const int MaxCaptures = 50, MaxPages = 500;
    public const long MaxTotalPixels = 160_000_000;
    public static IReadOnlyList<PdfPage> Plan(IReadOnlyList<PdfCapture> captures, PdfOptions options)
    {
        if (captures.Count is < 1 or > MaxCaptures) throw new ArgumentException("Añade entre 1 y 50 capturas.");
        var (width, height) = options.PageSize;
        var pages = new List<PdfPage>(); long total = 0;
        for (int i = 0; i < captures.Count; i++)
        {
            var image = captures[i].Image;
            total += (long)image.PixelWidth * image.PixelHeight;
            if (image.PixelWidth < 1 || image.PixelHeight < 1 || (long)image.PixelWidth * image.PixelHeight > ScrollStitcher.DefaultMaxPixels || total > MaxTotalPixels)
                throw new ArgumentException("Cada captura admite hasta 32 megapíxeles y el PDF hasta 160 megapíxeles en total.");
            double scale = (width - 48) / image.PixelWidth;
            int rows = Math.Max(1, (int)Math.Floor((height - 48) / scale));
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
            byte[] sample = new byte[checked(image.PixelWidth * 4)];
            for (int y = 0; y < image.PixelHeight;)
            {
                int count = Math.Min(rows, image.PixelHeight - y);
                if (y + count < image.PixelHeight)
                {
                    // Preferir un espacio uniforme próximo al corte; no reducir ni duplicar filas.
                    int quiet = 0;
                    for (int scan = y + count - 1; scan >= y + count - Math.Min(160, Math.Max(2, count / 8)); scan--)
                    {
                        if (scan <= y) break;
                        converted.CopyPixels(new Int32Rect(0, scan, image.PixelWidth, 1), sample, image.PixelWidth * 4, 0);
                        bool uniform = sample[0] >= 224 && sample[1] >= 224 && sample[2] >= 224;
                        for (int x = 1; uniform && x < image.PixelWidth; x++)
                            uniform = Math.Abs(sample[x * 4] - sample[0]) <= 8 && Math.Abs(sample[x * 4 + 1] - sample[1]) <= 8 && Math.Abs(sample[x * 4 + 2] - sample[2]) <= 8;
                        quiet = uniform ? quiet + 1 : 0;
                        if (quiet >= 2) { count = scan + 1 - y; break; }
                    }
                }
                pages.Add(new(i, new Int32Rect(0, y, image.PixelWidth, count), image.PixelWidth * scale, count * scale));
                if (pages.Count > MaxPages) throw new ArgumentException("El PDF supera el límite de 500 páginas.");
                y += count;
            }
        }
        return pages;
    }
}
