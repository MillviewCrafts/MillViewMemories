using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MillViewMemories.Models;
using SkiaSharp;

namespace MillViewMemories.Services;

public static class ProjectStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("Invalid project path.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("A resource path escapes the project folder.");
        // Do not follow filesystem links from an untrusted project.
        for (var current = path; current != root; current = Path.GetDirectoryName(current)!)
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked project files are unsupported.");
        return path;
    }

    public static void Validate(Scrapbook book)
    {
        static bool Id(string id) => Regex.IsMatch(id ?? "", "^[a-f0-9]{32}$");
        static bool Range(double n, double min, double max) => double.IsFinite(n) && n >= min && n <= max;
        static bool Colour(string s) => Regex.IsMatch(s ?? "", "^#[0-9a-fA-F]{6}$");
        if (book.SchemaVersion != 1) throw new InvalidDataException("Unsupported project version. This application supports version 1.");
        if (!Id(book.Id) || string.IsNullOrWhiteSpace(book.Title) || !Range(book.Width, 36, 7200) || !Range(book.Height, 36, 7200))
            throw new InvalidDataException("Invalid project title or dimensions (0.5–100 inches).");
        if (book.Pages.Count is < 1 or > 500 || book.Resources.Count > 10000) throw new InvalidDataException("Project limit exceeded (500 pages / 10,000 resources).");
        var ids = new HashSet<string>();
        foreach (var resource in book.Resources)
            if (!Id(resource.Id) || !ids.Add(resource.Id) || resource.Width <= 0 || resource.Height <= 0 || (long)resource.Width * resource.Height > 100_000_000)
                throw new InvalidDataException("Invalid or duplicate resource.");
        foreach (var page in book.Pages)
        {
            if (!Id(page.Id) || !ids.Add(page.Id) || string.IsNullOrWhiteSpace(page.Name) || !Colour(page.Background) || page.Elements.Count > 2000)
                throw new InvalidDataException("Invalid page or background colour (#RRGGBB).");
            foreach (var e in page.Elements)
                if (!Id(e.Id) || !ids.Add(e.Id) || e.Kind is not ("text" or "image") ||
                    !Range(e.X, -72000, 72000) || !Range(e.Y, -72000, 72000) || !Range(e.Width, 1, 72000) || !Range(e.Height, 1, 72000) ||
                    !Range(e.Rotation, -36000, 36000) || !Range(e.Opacity, 0, 1) || !Range(e.FontSize, 1, 2000) || !Colour(e.Colour) ||
                    e.Text.Length > 100000 || e.Alignment is not ("left" or "center" or "right") ||
                    (e.Kind == "image" && !book.Resources.Any(r => r.Id == e.ResourceId)))
                    throw new InvalidDataException("Invalid element geometry, colour, text, or resource.");
        }
    }

    public static FileStream AcquireLock(string root)
    {
        Directory.CreateDirectory(root);
        try { return new FileStream(SafePath(root, ".mvm.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("This project is already open, or its folder is not writable. Close the other instance and try again.", ex); }
    }

    public static Scrapbook Open(string root, string manifest = "project.mvm", CancellationToken cancellationToken = default)
    {
        var file = SafePath(root, manifest);
        if (new FileInfo(file).Length > 16_000_000) throw new InvalidDataException("Project manifest is too large.");
        var book = JsonSerializer.Deserialize<Scrapbook>(File.ReadAllText(file), Json) ?? throw new InvalidDataException("Empty project.");
        if (book.SchemaVersion != 1) throw new InvalidDataException("Unsupported project version.");
        if (book.PageFiles.Count is < 1 or > 500) throw new InvalidDataException("Invalid page count.");
        foreach (var resource in book.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resourcePath = SafePath(root, resource.Path);
            if (!File.Exists(resourcePath)) throw new FileNotFoundException($"Missing resource: {resource.Name}. Restore the resource before opening this project.");
            using var stream = File.OpenRead(resourcePath);
            if (stream.Length > 100_000_000) throw new InvalidDataException("Resource exceeds the 100 MB limit.");
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException($"Damaged resource: {resource.Name}");
            if (codec.Info.Width != resource.Width || codec.Info.Height != resource.Height || codec.EncodedFormat != SKEncodedImageFormat.Png)
                throw new InvalidDataException($"Resource dimensions or format changed: {resource.Name}");
            if (resource.OriginalPath is not null && !File.Exists(SafePath(root, resource.OriginalPath)))
                throw new FileNotFoundException($"Missing imported original: {resource.Name}");
        }
        book.Pages = book.PageFiles.Select(p => { cancellationToken.ThrowIfCancellationRequested(); return SvgPages.Read(book, p, SafePath(root, p.Path), root); }).ToList();
        Validate(book);
        foreach (var resource in book.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var image = SKImage.FromEncodedData(SafePath(root, resource.Path));
            if (image is not null) TryWritePreview(root, resource, image);
        }
        return book;
    }

    public static void Save(Scrapbook book, string root, string sourceRoot, string manifest = "project.mvm", Action? beforeCommit = null, CancellationToken cancellationToken = default)
    {
        Validate(book);
        Directory.CreateDirectory(root);
        foreach (var resource in book.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var relative in new[] { resource.Path, resource.OriginalPath }.OfType<string>())
            {
                var target = SafePath(root, relative);
                if (!File.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(SafePath(sourceRoot, relative), target, false);
                }
            }
        }
        foreach (var resource in book.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(SafePath(root, $"previews/{resource.Id}.png"))) continue;
            using var image = SKImage.FromEncodedData(SafePath(root, resource.Path));
            if (image is not null) TryWritePreview(root, resource, image);
        }
        // Immutable generation; only the final manifest replacement publishes it.
        var generation = Guid.NewGuid().ToString("N");
        var references = new List<PageReference>();
        foreach (var page in book.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = $"pages/{generation}/{page.Id}.svg";
            var path = SafePath(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { SvgPages.Write(book, page, path, root).Save(stream); stream.Flush(true); }
            references.Add(new(page.Id, page.Name, relative));
        }
        var previous = book.PageFiles;
        book.PageFiles = references;
        var targetManifest = SafePath(root, manifest);
        var temporary = SafePath(root, manifest + "." + generation + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, book, Json); stream.Flush(true); }
            cancellationToken.ThrowIfCancellationRequested();
            beforeCommit?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(targetManifest)) File.Replace(temporary, targetManifest, SafePath(root, manifest + ".previous"));
            else File.Move(temporary, targetManifest);
        }
        catch { book.PageFiles = previous; throw; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Resource Import(string source, string root, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(source);
        if (stream.Length > 100_000_000) throw new InvalidDataException("Images must be smaller than 100 MB.");
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("This image could not be read.");
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png)) throw new InvalidDataException("Import JPEG or PNG images.");
        if ((long)codec.Info.Width * codec.Info.Height > 100_000_000) throw new InvalidDataException("Image exceeds 100 megapixels.");
        // Normalise orientation so editor, SVG consumers and export agree.
        using var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Damaged image.");
        using var image = SKImage.FromBitmap(bitmap);
        var origin = codec.EncodedOrigin;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        using var surface = SKSurface.Create(new SKImageInfo(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height));
        var canvas = surface.Canvas;
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(bitmap.Width, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(bitmap.Width, bitmap.Height); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, bitmap.Height); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(bitmap.Height, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(bitmap.Height, bitmap.Width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, bitmap.Width); canvas.RotateDegrees(-90); break;
        }
        canvas.DrawImage(image, 0, 0);
        using var oriented = surface.Snapshot();
        using var data = oriented.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        if (bytes.Length > 100_000_000) throw new InvalidDataException("Normalised image exceeds 100 MB. Resize the source image before importing it.");
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var resource = new Resource { Name = Path.GetFileName(source), Width = oriented.Width, Height = oriented.Height, Hash = hash };
        resource.Path = $"resources/{resource.Id}.png";
        resource.OriginalPath = $"resources/originals/{resource.Id}.{(codec.EncodedFormat == SKEncodedImageFormat.Jpeg ? "jpg" : "png")}";
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(SafePath(root, "resources"));
        Directory.CreateDirectory(SafePath(root, "resources/originals"));
        File.Copy(source, SafePath(root, resource.OriginalPath));
        File.WriteAllBytes(SafePath(root, resource.Path), bytes);
        TryWritePreview(root, resource, oriented);
        return resource;
    }

    static void TryWritePreview(string root, Resource resource, SKImage image)
    {
        try
        {
            var scale = Math.Min(96d / image.Width, 96d / image.Height);
            var width = Math.Max(1, (int)(image.Width * scale)); var height = Math.Max(1, (int)(image.Height * scale));
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear));
            using var snapshot = surface.Snapshot(); using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            Directory.CreateDirectory(SafePath(root, "previews"));
            File.WriteAllBytes(SafePath(root, $"previews/{resource.Id}.png"), data.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Optional, regenerable cache. */ }
    }
}
