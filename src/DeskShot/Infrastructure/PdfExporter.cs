using System.Globalization;
using System.IO.Compression;
using System.Text;
using DeskShot.Application;

namespace DeskShot.Infrastructure;

// Subconjunto PDF 1.4: páginas con imágenes RGB /FlateDecode, sin dependencias ni impresora.
// Cada tira conserva todos los píxeles originales; la matriz solo fija el tamaño en papel.
public sealed class PdfExporter
{
    public void Save(IReadOnlyList<PdfCapture> captures, PdfOptions options, string path,
        IProgress<int>? progress = null, CancellationToken token = default)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Elige una ruta absoluta con extensión .pdf.");
        var pages = PdfLayout.Plan(captures, options);
        var size = options.PageSize;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var offsets = new long[3 + pages.Count * 3];
                void Write(string value) => output.Write(Encoding.ASCII.GetBytes(value));
                string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
                void Begin(int id) { offsets[id] = output.Position; Write($"{id} 0 obj\n"); }
                void End() => Write("\nendobj\n");
                Write("%PDF-1.4\n%"); output.Write([0xE2, 0xE3, 0xCF, 0xD3, 10]);
                Begin(1); Write("<< /Type /Catalog /Pages 2 0 R >>"); End();
                Begin(2); Write($"<< /Type /Pages /Count {pages.Count} /Kids [" + string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{3 + i * 3} 0 R")) + "] >>"); End();
                for (int index = 0; index < pages.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var page = pages[index]; int id = 3 + index * 3;
                    Begin(id); Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(size.Width)} {Number(size.Height)}] /Resources << /XObject << /Capture {id + 2} 0 R >> >> /Contents {id + 1} 0 R >>"); End();
                    string commands = $"q\n{Number(page.Width)} 0 0 {Number(page.Height)} 24 {Number(size.Height - 24 - page.Height)} cm\n/Capture Do\nQ\n";
                    Begin(id + 1); Write($"<< /Length {Encoding.ASCII.GetByteCount(commands)} >>\nstream\n{commands}endstream"); End();
                    var crop = new CroppedBitmap(captures[page.CaptureIndex].Image, page.Pixels);
                    var converted = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
                    int stride = checked(page.Pixels.Width * 4);
                    byte[] row = new byte[stride], rgb = new byte[checked(page.Pixels.Width * 3)];
                    using var compressed = new MemoryStream();
                    using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                        for (int y = 0; y < page.Pixels.Height; y++)
                        {
                            token.ThrowIfCancellationRequested();
                            converted.CopyPixels(new Int32Rect(0, y, page.Pixels.Width, 1), row, stride, 0);
                            for (int x = 0; x < page.Pixels.Width; x++)
                            {
                                int p = x * 4, q = x * 3, alpha = row[p + 3];
                                rgb[q] = (byte)((row[p + 2] * alpha + 255 * (255 - alpha) + 127) / 255);
                                rgb[q + 1] = (byte)((row[p + 1] * alpha + 255 * (255 - alpha) + 127) / 255);
                                rgb[q + 2] = (byte)((row[p] * alpha + 255 * (255 - alpha) + 127) / 255);
                            }
                            zlib.Write(rgb);
                        }
                    Begin(id + 2); Write($"<< /Type /XObject /Subtype /Image /Width {page.Pixels.Width} /Height {page.Pixels.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Interpolate false /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n");
                    compressed.Position = 0; compressed.CopyTo(output); Write("\nendstream"); End();
                    progress?.Report((index + 1) * 100 / pages.Count);
                }
                long xref = output.Position;
                Write($"xref\n0 {offsets.Length}\n0000000000 65535 f \n");
                for (int i = 1; i < offsets.Length; i++) Write(offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
                Write($"trailer\n<< /Size {offsets.Length} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
                output.Flush(true);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
