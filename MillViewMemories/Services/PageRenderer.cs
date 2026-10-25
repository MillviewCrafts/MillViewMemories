using MillViewMemories.Models;
using SkiaSharp;

namespace MillViewMemories.Services;

/// <summary>One geometry/text pipeline for editor previews, images and PDF.</summary>
public sealed class PageRenderer : IDisposable
{
    readonly Dictionary<string, SKImage> images = [];
    public void Draw(SKCanvas canvas, Scrapbook book, ScrapPage page, string root, CancellationToken cancellationToken = default)
    {
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, (float)book.Width, (float)book.Height));
        canvas.DrawColor(SKColor.Parse(page.Background));
        foreach (var item in page.Elements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            canvas.Save();
            canvas.Translate((float)item.X, (float)item.Y);
            canvas.RotateDegrees((float)item.Rotation, (float)item.Width / 2, (float)item.Height / 2);
            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black.WithAlpha((byte)(item.Opacity * 255)) };
            if (item.Kind == "image")
            {
                var resource = book.Resources.Single(r => r.Id == item.ResourceId);
                var path = ProjectStore.SafePath(root, resource.Path);
                if (!images.TryGetValue(path, out var image))
                {
                    image = SKImage.FromEncodedData(path) ?? throw new InvalidDataException($"Damaged image: {resource.Name}");
                    if (images.Count >= 16) { var oldest = images.First(); oldest.Value.Dispose(); images.Remove(oldest.Key); }
                    images.Add(path, image);
                }
                canvas.DrawImage(image, new SKRect(0, 0, (float)item.Width, (float)item.Height), new SKSamplingOptions(SKFilterMode.Linear), paint);
            }
            else
            {
                paint.Color = SKColor.Parse(item.Colour).WithAlpha((byte)(item.Opacity * 255));
                using var typeface = SKTypeface.FromFamilyName(item.FontFamily) ?? SKTypeface.Default;
                using var font = new SKFont(typeface, (float)item.FontSize);
                var align = item.Alignment == "center" ? SKTextAlign.Center : item.Alignment == "right" ? SKTextAlign.Right : SKTextAlign.Left;
                var x = item.Alignment == "center" ? item.Width / 2 : item.Alignment == "right" ? item.Width : 0;
                var lines = item.Text.Replace("\r", "").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                    canvas.DrawText(lines[i], (float)x, (float)(item.FontSize * (1 + i * 1.2)), align, font, paint);
            }
            canvas.Restore();
        }
        canvas.Restore();
    }

    public byte[] Raster(Scrapbook book, ScrapPage page, string root, double dpi, bool jpeg = false, CancellationToken cancellationToken = default)
    {
        var width = (int)Math.Ceiling(book.Width * dpi / 72); var height = (int)Math.Ceiling(book.Height * dpi / 72);
        if (!double.IsFinite(dpi) || dpi < 1 || dpi > 600 || width < 1 || height < 1 || (long)width * height > 64_000_000)
            throw new InvalidDataException("Choose 36–600 DPI and at most 64 megapixels per exported page.");
        using var surface = SKSurface.Create(new SKImageInfo(width, height)) ?? throw new InvalidOperationException("Unable to allocate page bitmap.");
        surface.Canvas.Scale((float)(dpi / 72));
        Draw(surface.Canvas, book, page, root, cancellationToken);
        using var image = surface.Snapshot();
        using var data = image.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, 92);
        return data.ToArray();
    }

    public void Export(Scrapbook book, string root, string destination, string format, double dpi, CancellationToken cancellationToken = default)
    {
        ProjectStore.Validate(book);
        if (!double.IsFinite(dpi) || dpi < 36 || dpi > 600) throw new InvalidDataException("Export resolution must be 36–600 DPI.");
        if (format is not ("PNG" or "JPEG" or "PDF")) throw new InvalidDataException("Choose PNG, JPEG or PDF.");
        if (format == "PDF")
        {
            CommitFile(destination, temporary =>
            {
                using var output = File.Create(temporary);
                using var pdf = SKDocument.CreatePdf(output) ?? throw new IOException("PDF creation failed.");
                foreach (var page in book.Pages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var canvas = pdf.BeginPage((float)book.Width, (float)book.Height);
                    Draw(canvas, book, page, root, cancellationToken); pdf.EndPage();
                }
                pdf.Close();
            });
        }
        else
        {
            Directory.CreateDirectory(destination);
            for (var i = 0; i < book.Pages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = Raster(book, book.Pages[i], root, dpi, format == "JPEG", cancellationToken);
                CommitFile(Path.Combine(destination, $"page-{i + 1:D3}.{(format == "JPEG" ? "jpg" : "png")}"), temporary => File.WriteAllBytes(temporary, bytes));
            }
        }
    }
    static void CommitFile(string destination, Action<string> write)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { write(temporary); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose() { foreach (var image in images.Values) image.Dispose(); images.Clear(); }
}
