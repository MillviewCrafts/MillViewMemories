using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using MillViewMemories.Models;
using MillViewMemories.Services;
using MillViewMemories.ViewModels;
using SkiaSharp;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MillViewMemories.Views;

// Executable integration runner: a failing check returns exit code 1.
var testRoot = Path.Combine(Path.GetTempPath(), "MillViewMemories-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var passed = 0; var failed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
string Folder(string name) { var root = Path.Combine(testRoot, name); Directory.CreateDirectory(root); return root; }
var source = Path.Combine(testRoot, "synthetic.png");
using (var surface = SKSurface.Create(new SKImageInfo(40, 20)))
{
    surface.Canvas.Clear(SKColors.Coral); using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(source, data.ToArray());
}
Scrapbook Fixture(string root)
{
    var book = new Scrapbook { Title = "Summer & family <memories>", Width = 504, Height = 504 };
    var resource = ProjectStore.Import(source, root); book.Resources.Add(resource);
    book.Pages[0].Elements.Add(new PageElement { Text = "Hello <world> & family\nSecond line", Alignment = "center", Rotation = 17.5, Opacity = .7 });
    book.Pages[0].Elements.Add(new PageElement { Kind = "image", ResourceId = resource.Id, X = 150, Y = 180, Width = 200, Height = 100 });
    book.Pages.Add(new ScrapPage { Name = "Second page", Background = "#DCF0EF" }); return book;
}

Test("SVG round-trip preserves pages, text, geometry and resources", () =>
{
    var root = Folder("roundtrip"); var book = Fixture(root); ProjectStore.Save(book, root, root); var loaded = ProjectStore.Open(root);
    Check(JsonSerializer.Serialize(book.Pages) == JsonSerializer.Serialize(loaded.Pages), "Page data changed.");
    Check(book.Resources[0].Hash == loaded.Resources[0].Hash && loaded.PageFiles.All(p => p.Path.EndsWith(".svg")), "Resources or SVG paths changed.");
    Check(!File.ReadAllText(Path.Combine(root, "project.mvm")).Contains("Hello"), "Page content leaked into JSON manifest.");
    Check(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(Path.Combine(root, loaded.Resources[0].OriginalPath!))), "Imported original was changed.");
});
Test("Committed version-one fixture remains readable", () =>
{
    var book = ProjectStore.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Version1"));
    Check(book.Width == 504 && book.Pages[0].Name == "Fixture page" && book.Pages[0].Elements[0].Text == "A memory to keep", "Version-one compatibility changed.");
});
Test("Missing previews are regenerated without losing the document", () =>
{
    var root = Folder("preview"); var book = Fixture(root); ProjectStore.Save(book, root, root);
    var preview = Path.Combine(root, "previews", book.Resources[0].Id + ".png"); File.Delete(preview);
    Check(ProjectStore.Open(root).Pages.Count == 2 && File.Exists(preview), "Preview regeneration failed.");
});
Test("SVG text preserves blank lines and intentional whitespace", () =>
{
    var root = Folder("whitespace"); var book = Fixture(root); var value = "  First  line  \n\n   \nLast line ";
    book.Pages[0].Elements[0].Text = value; ProjectStore.Save(book, root, root);
    Check(ProjectStore.Open(root).Pages[0].Elements[0].Text == value, "Text whitespace changed.");
});
Test("EXIF-oriented JPEG is normalised while preserving the original", () =>
{
    using var bitmap = SKBitmap.Decode(source); using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 95);
    var jpeg = encoded.ToArray();
    byte[] app1 = [0xff, 0xe1, 0x00, 0x22, 0x45, 0x78, 0x69, 0x66, 0, 0, 0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
    var bytes = jpeg.Take(2).Concat(app1).Concat(jpeg.Skip(2)).ToArray(); var path = Path.Combine(testRoot, "oriented.jpg"); File.WriteAllBytes(path, bytes);
    var root = Folder("orientation"); var resource = ProjectStore.Import(path, root);
    Check(resource.Width == 20 && resource.Height == 40, "EXIF rotation was not applied.");
    Check(File.ReadAllBytes(Path.Combine(root, resource.OriginalPath!)).SequenceEqual(bytes), "JPEG original was not preserved.");
});
Test("Interrupted save leaves the previous generation readable", () =>
{
    var root = Folder("interruption"); var book = Fixture(root); ProjectStore.Save(book, root, root);
    book.Pages[0].Elements[0].Text = "Unsaved replacement";
    Throws<IOException>(() => ProjectStore.Save(book, root, root, beforeCommit: () => throw new IOException("Simulated full disk")));
    Check(ProjectStore.Open(root).Pages[0].Elements[0].Text.StartsWith("Hello"), "Previous save was damaged.");
});
Test("Previous manifest and recovery stay independent", () =>
{
    var root = Folder("recovery"); var book = Fixture(root); ProjectStore.Save(book, root, root);
    book.Pages[0].Name = "Saved revision"; ProjectStore.Save(book, root, root);
    book.Pages[0].Name = "Recovery revision"; ProjectStore.Save(book, root, root, ".recovery.mvm");
    Check(ProjectStore.Open(root).Pages[0].Name == "Saved revision", "Recovery overwrote save.");
    Check(ProjectStore.Open(root, "project.mvm.previous").Pages[0].Name == "Page 1", "Previous save lost.");
    Check(ProjectStore.Open(root, ".recovery.mvm").Pages[0].Name == "Recovery revision", "Recovery lost.");
});
Test("Save As copies resources and survives moving the folder", () =>
{
    var root = Folder("original"); var book = Fixture(root); ProjectStore.Save(book, root, root);
    var target = Folder("copy"); ProjectStore.Save(book, target, root);
    var moved = Path.Combine(testRoot, "moved"); Directory.Move(target, moved);
    Check(ProjectStore.Open(moved).Resources.Count == 1, "Moved album not self-contained.");
});
Test("Cancellation before commit preserves the saved generation", () =>
{
    var root = Folder("cancel"); var book = Fixture(root); ProjectStore.Save(book, root, root);
    book.Pages[0].Name = "Cancelled revision"; using var cancellation = new CancellationTokenSource();
    Throws<OperationCanceledException>(() => ProjectStore.Save(book, root, root, beforeCommit: cancellation.Cancel, cancellationToken: cancellation.Token));
    Check(ProjectStore.Open(root).Pages[0].Name == "Page 1", "Cancelled save replaced the manifest.");
});
Test("Project lock prevents a second writer", () =>
{
    var root = Folder("lock"); using var first = ProjectStore.AcquireLock(root); Throws<IOException>(() => ProjectStore.AcquireLock(root));
});
Test("Path traversal and absolute paths are rejected", () =>
{
    var root = Folder("paths"); Throws<InvalidDataException>(() => ProjectStore.SafePath(root, "../outside.png"));
    Throws<InvalidDataException>(() => ProjectStore.SafePath(root, source));
});
Test("Future schema is rejected without modifying source", () =>
{
    var root = Folder("future"); var book = Fixture(root); ProjectStore.Save(book, root, root);
    var path = Path.Combine(root, "project.mvm"); var content = File.ReadAllText(path).Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 999"); File.WriteAllText(path, content);
    Throws<InvalidDataException>(() => ProjectStore.Open(root)); Check(File.ReadAllText(path) == content, "Source changed.");
});
Test("Missing resources report failure", () =>
{
    var root = Folder("missing"); var book = Fixture(root); ProjectStore.Save(book, root, root);
    File.Move(Path.Combine(root, book.Resources[0].Path), Path.Combine(root, "hidden.png")); Throws<FileNotFoundException>(() => ProjectStore.Open(root));
});
Test("Unsupported SVG scripts and attributes are rejected", () =>
{
    var root = Folder("script"); var book = Fixture(root); ProjectStore.Save(book, root, root);
    var path = Path.Combine(root, book.PageFiles[0].Path); var svg = XDocument.Load(path);
    svg.Root!.Add(new XElement(SvgPages.Svg + "script", "alert('no')")); svg.Save(path);
    Throws<InvalidDataException>(() => ProjectStore.Open(root));
    svg.Root.Elements().Last().Remove(); svg.Root.SetAttributeValue("onclick", "bad()"); svg.Save(path);
    Throws<InvalidDataException>(() => ProjectStore.Open(root));
});
Test("DTD and external entities are prohibited", () =>
{
    var root = Folder("dtd"); var book = Fixture(root); ProjectStore.Save(book, root, root);
    var path = Path.Combine(root, book.PageFiles[0].Path);
    File.WriteAllText(path, "<!DOCTYPE svg [<!ENTITY e SYSTEM 'file:///private'>]><svg xmlns='http://www.w3.org/2000/svg'>&e;</svg>");
    Throws<XmlException>(() => ProjectStore.Open(root));
});
Test("SVG dimension mismatch is rejected", () =>
{
    var root = Folder("dimensions"); var book = Fixture(root); ProjectStore.Save(book, root, root);
    var path = Path.Combine(root, book.PageFiles[0].Path); var svg = XDocument.Load(path); svg.Root!.SetAttributeValue("width", "100pt"); svg.Save(path);
    Throws<InvalidDataException>(() => ProjectStore.Open(root));
});
Test("Validation rejects invalid geometry and duplicate IDs", () =>
{
    var book = new Scrapbook(); book.Pages[0].Elements.Add(new PageElement { X = double.NaN }); Throws<InvalidDataException>(() => ProjectStore.Validate(book));
    book.Pages[0].Elements.Clear(); book.Pages.Add(MainViewModel.Copy(book.Pages[0])); Throws<InvalidDataException>(() => ProjectStore.Validate(book));
});
Test("Undo and redo restore saved revision and page ordering", () =>
{
    var vm = new MainViewModel(Folder("history")); vm.MarkSaved(); vm.AddText(); Check(vm.IsDirty, "Edit not dirty.");
    vm.Undo(); Check(!vm.IsDirty && vm.SelectedPage!.Elements.Count == 0, "Undo did not restore saved revision.");
    vm.Redo(); Check(vm.IsDirty && vm.SelectedPage!.Elements.Count == 1, "Redo lost edit.");
    var first = vm.SelectedPage!.Id; vm.AddPage(); vm.MovePage(-1); Check(vm.Book.Pages[1].Id == first, "Reordering failed.");
    vm.Undo(); Check(vm.Book.Pages[0].Id == first, "Order undo failed.");
});
Test("Duplicate page gives objects new stable IDs", () =>
{
    var vm = new MainViewModel(Folder("duplicate")); vm.AddText(); vm.DuplicatePage(); ProjectStore.Validate(vm.Book);
    Check(vm.Book.Pages.Count == 2 && vm.Book.Pages[0].Elements[0].Id != vm.Book.Pages[1].Elements[0].Id, "Duplicate IDs.");
});
Test("Invalid edits roll back without corrupting history", () =>
{
    var vm = new MainViewModel(Folder("rollback")); vm.MarkSaved(); Throws<InvalidDataException>(() => vm.Edit(() => vm.Book.Pages[0].Background = "invalid"));
    Check(!vm.IsDirty && !vm.CanUndo && vm.Book.Pages[0].Background == "#FFFFFF", "Failed edit was committed.");
});
Test("Project clipboard pastes independent objects onto another page", () =>
{
    var vm = new MainViewModel(Folder("clipboard")); vm.AddText(); var sourceId = vm.SelectedElement!.Id;
    vm.CopyElement(); vm.AddPage(); vm.PasteElement(); ProjectStore.Validate(vm.Book);
    Check(vm.SelectedPage!.Elements.Count == 1 && vm.SelectedElement!.Id != sourceId, "Paste did not create an independent object.");
    vm.Undo(); Check(vm.SelectedPage!.Elements.Count == 0, "Paste cannot be undone.");
});
Test("PNG and JPEG export use requested pixel dimensions", () =>
{
    var root = Folder("raster"); var book = Fixture(root); using var renderer = new PageRenderer();
    foreach (var jpeg in new[] { false, true })
    {
        var bytes = renderer.Raster(book, book.Pages[0], root, 144, jpeg); using var image = SKBitmap.Decode(bytes);
        Check(image.Width == 1008 && image.Height == 1008, "DPI conversion incorrect.");
        var corner = image.GetPixel(1007, 1007); Check(corner.Red > 240 && corner.Green > 240, "Background incorrect.");
    }
});
Test("Album raster export creates ordered page files", () =>
{
    var root = Folder("album-export"); var book = Fixture(root); using var renderer = new PageRenderer(); var output = Folder("images");
    renderer.Export(book, root, output, "PNG", 72); Check(File.Exists(Path.Combine(output, "page-001.png")) && File.Exists(Path.Combine(output, "page-002.png")), "Page outputs missing.");
});
Test("PDF export produces a multi-page document at physical size", () =>
{
    var root = Folder("pdf"); var book = Fixture(root); using var renderer = new PageRenderer(); var path = Path.Combine(root, "album.pdf");
    renderer.Export(book, root, path, "PDF", 300); var pdf = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
    Check(pdf.StartsWith("%PDF-") && pdf.Contains("/Count 2") && pdf.Contains("504"), "PDF page count/size incorrect.");
});
Test("Oversized raster output fails before allocation", () =>
{
    var book = new Scrapbook { Width = 7200, Height = 7200 }; using var renderer = new PageRenderer();
    Throws<InvalidDataException>(() => renderer.Raster(book, book.Pages[0], testRoot, 600));
});
Test("Desktop shell renders and supports object/page actions", () =>
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            AppBuilder.Configure<MillViewMemories.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var window = new MainWindow(); var vm = (MainViewModel)window.DataContext!;
            var root = Folder("desktop"); var book = Fixture(root); vm.Load(book, root, root); window.Show(); Dispatcher.UIThread.RunJobs();
            void Click(string label)
            {
                var button = window.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == label);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            }
            var count = vm.SelectedPage!.Elements.Count; Click("Add text"); Check(vm.SelectedPage.Elements.Count == count + 1, "Add text button failed.");
            Click("Undo"); Check(vm.SelectedPage.Elements.Count == count, "Undo button failed.");
            Click("Redo"); Check(vm.SelectedPage.Elements.Count == count + 1, "Redo button failed.");
            var canvas = window.GetVisualDescendants().OfType<PageCanvas>().Single();
            var start = canvas.TranslatePoint(new Point(200 * .65, 220 * .65), window)!.Value;
            window.MouseDown(start, Avalonia.Input.MouseButton.Left);
            window.MouseMove(start + new Vector(13, 13)); window.MouseUp(start + new Vector(13, 13), Avalonia.Input.MouseButton.Left);
            Check(Math.Abs(vm.SelectedPage.Elements[1].X - 170) < .1, "Pointer drag did not move the selected image.");
            vm.Undo(); Check(Math.Abs(vm.SelectedPage.Elements[1].X - 150) < .1, "A drag was not a single undo transaction.");
            Click("One / two pages"); Check(window.GetVisualDescendants().OfType<PageCanvas>().Count() == 2, "Adjacent page view failed.");
            using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rendered desktop frame.");
            Check(frame.PixelSize.Width >= 1100 && frame.PixelSize.Height >= 700, "Window layout is undersized.");
            frame.Save(Path.Combine(root, "desktop-smoke.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            vm.MarkSaved(); window.Close(); Dispatcher.UIThread.RunJobs();
        }
        catch (Exception ex) { failure = ex; }
    });
    if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
    thread.Start(); if (!thread.Join(TimeSpan.FromSeconds(45))) throw new Exception("Desktop test timed out.");
    if (failure is not null) throw failure;
});
Console.WriteLine($"{passed} passed; {failed} failed. Temporary fixtures: {testRoot}");
// Retain fixtures for diagnosis; no user folders or photographs are touched.
return failed == 0 ? 0 : 1;
