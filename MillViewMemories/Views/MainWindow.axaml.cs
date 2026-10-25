using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MillViewMemories.Models;
using MillViewMemories.Services;
using MillViewMemories.ViewModels;

namespace MillViewMemories.Views;

public partial class MainWindow : Window
{
    readonly MainViewModel model = new();
    readonly UserSettings settings = UserSettings.Load();
    readonly ListBox pages = new(), layers = new(), resources = new();
    readonly StackPanel properties = new() { Spacing = 8, Margin = new Thickness(12) };
    readonly StackPanel workspace = new() { Orientation = Orientation.Horizontal, Spacing = 24, Margin = new Thickness(24) };
    readonly StackPanel resourcePanel = new() { Spacing = 8, Margin = new Thickness(12) };
    readonly TextBlock status = new() { Margin = new Thickness(12, 6), TextWrapping = TextWrapping.Wrap };
    readonly List<PageCanvas> canvases = [];
    readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap> thumbnails = [];
    readonly DispatcherTimer recoveryTimer = new() { Interval = TimeSpan.FromMinutes(2) };
    FileStream? projectLock;
    bool refreshing, busy, closing;
    double zoom = .65;
    bool twoPages;
    Grid shell = null!;
    Border progressOverlay = null!;
    CancellationTokenSource? operation;
    CancellationToken Token => operation?.Token ?? CancellationToken.None;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = model;
        Width = 1400; Height = 900; MinWidth = 1100; MinHeight = 700;
        shell = this.FindControl<Grid>("Shell")!;
        shell.RowDefinitions = new RowDefinitions("Auto,*,Auto");
        var toolbar = new WrapPanel { Margin = new Thickness(10), Orientation = Orientation.Horizontal };
        toolbar.Children.Add(Button("New project", () => Run(NewProject)));
        toolbar.Children.Add(Button("Open", () => Run(() => OpenProject(null))));
        toolbar.Children.Add(Button("Recent", () => Run(OpenRecent)));
        toolbar.Children.Add(Button("Recover draft", () => Run(RecoverDraft)));
        toolbar.Children.Add(Button("Save", () => Run(() => Save(false))));
        toolbar.Children.Add(Button("Save as", () => Run(() => Save(true))));
        toolbar.Children.Add(Button("Export album", () => Run(Export)));
        toolbar.Children.Add(Button("Undo", model.Undo)); toolbar.Children.Add(Button("Redo", model.Redo));
        toolbar.Children.Add(Button("−", () => SetZoom(zoom / 1.2)));
        toolbar.Children.Add(Button("+", () => SetZoom(zoom * 1.2)));
        toolbar.Children.Add(Button("Fit page", () => SetZoom(Math.Min(650 / model.Book.Width, 650 / model.Book.Height))));
        toolbar.Children.Add(Button("One / two pages", () => { twoPages = !twoPages; Refresh(); }));
        toolbar.Children.Add(Button("Preferences", () => Run(Preferences)));
        shell.Children.Add(toolbar);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,280") };
        Grid.SetRow(body, 1); shell.Children.Add(body);
        var left = new Grid { RowDefinitions = new RowDefinitions("*,*") };
        var pagePanel = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        pagePanel.Children.Add(Heading("Pages")); pages.ItemsSource = model.Pages; pages.MinHeight = 100; pages.MaxHeight = 240;
        pagePanel.Children.Add(pages);
        var pageButtons = new WrapPanel();
        pageButtons.Children.Add(Button("Add", model.AddPage)); pageButtons.Children.Add(Button("Copy", model.DuplicatePage));
        pageButtons.Children.Add(Button("↑", () => model.MovePage(-1))); pageButtons.Children.Add(Button("↓", () => model.MovePage(1)));
        pageButtons.Children.Add(Button("Rename", () => Run(RenamePage)));
        pageButtons.Children.Add(Button("Delete", () => { if (model.Book.Pages.Count > 1) model.Edit(() => model.Book.Pages.Remove(model.SelectedPage!)); }));
        pagePanel.Children.Add(pageButtons); left.Children.Add(new ScrollViewer { Content = pagePanel });
        resourcePanel.Children.Add(Heading("Photographs & decorations"));
        resourcePanel.Children.Add(Button("Import JPEG / PNG", () => Run(ImportFiles)));
        resourcePanel.Children.Add(Button("Browse resource folders", () => Run(BrowseLibrary)));
        resourcePanel.Children.Add(resources); resources.ItemsSource = model.Resources; resources.MaxHeight = 200;
        resources.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Resource>((resource, _) =>
        {
            if (resource is null) return new TextBlock();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(2) };
            var path = ProjectStore.SafePath(model.WorkingRoot, $"previews/{resource.Id}.png");
            if (!thumbnails.TryGetValue(path, out var thumbnail) && File.Exists(path))
            {
                try { using var file = File.OpenRead(path); thumbnail = new Avalonia.Media.Imaging.Bitmap(file); thumbnails.Add(path, thumbnail); }
                catch (Exception ex) when (ex is IOException or ArgumentException) { /* A cache is not authoritative. */ }
            }
            row.Children.Add(new Image { Source = thumbnail, Width = 48, Height = 48, Stretch = Stretch.Uniform });
            var uses = model.Book.Pages.Sum(p => p.Elements.Count(e => e.ResourceId == resource.Id));
            row.Children.Add(new TextBlock { Text = $"{resource.Name}\nUsed {uses} time(s)", TextWrapping = TextWrapping.Wrap, MaxWidth = 120 }); return row;
        });
        resourcePanel.Children.Add(Button("Place selected resource", () => { if (resources.SelectedItem is Resource r) model.Place(r); }));
        var resourceScroll = new ScrollViewer { Content = resourcePanel }; Grid.SetRow(resourceScroll, 1); left.Children.Add(resourceScroll); body.Children.Add(left);
        var scroll = new ScrollViewer { Content = workspace, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Background = Brush.Parse("#E9ECEF") };
        Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        Grid.SetColumn(properties, 2); var propertyScroll = new ScrollViewer { Content = properties }; Grid.SetColumn(propertyScroll, 2); body.Children.Add(propertyScroll);
        Grid.SetRow(status, 2); shell.Children.Add(status);
        var progress = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        progress.Children.Add(Heading("Working…"));
        progress.Children.Add(new TextBlock { Text = "Cancellation takes effect between files or page objects.", TextWrapping = TextWrapping.Wrap });
        progress.Children.Add(Button("Cancel operation", () => operation?.Cancel()));
        progressOverlay = new Border { Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
            Child = progress, MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        this.FindControl<Grid>("Host")!.Children.Add(progressOverlay);
        pages.SelectionChanged += (_, _) => { if (!refreshing && pages.SelectedItem is ScrapPage page) model.SelectedPage = page; };
        layers.SelectionChanged += (_, _) => { if (!refreshing) model.SelectedElement = layers.SelectedItem as PageElement; };
        model.Changed += Refresh;
        KeyDown += OnKeyDown;
        Closing += OnClosing;
        Closed += (_, _) => { recoveryTimer.Stop(); projectLock?.Dispose(); foreach (var canvas in canvases) canvas.Dispose(); foreach (var bitmap in thumbnails.Values) bitmap.Dispose(); };
        recoveryTimer.Tick += (_, _) => { if (!busy && model.IsDirty) Run(Autosave); };
        recoveryTimer.Start(); Refresh();
        status.Text = "Start with New project, or Open an album. Add a photograph or text, then drag it on the page. All editing is local.";
    }

    static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 16, TextWrapping = TextWrapping.Wrap };
    Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(2), Padding = new Thickness(9, 6) };
        button.Click += async (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { status.Text = ex.Message; await Message("Operation failed", ex.Message); }
        };
        return button;
    }
    async void Run(Func<Task> action)
    {
        if (busy) return;
        busy = true; shell.IsEnabled = false; operation = new CancellationTokenSource(); progressOverlay.IsVisible = true;
        try { await action(); }
        catch (OperationCanceledException) { status.Text = "Operation cancelled. Completed imports/exports are retained; the previous project save is unchanged."; }
        catch (Exception ex) { status.Text = "Could not complete the operation: " + ex.Message; await Message("Operation failed", ex.Message); }
        finally { busy = false; shell.IsEnabled = true; progressOverlay.IsVisible = false; operation.Dispose(); operation = null; }
    }
    void SetZoom(double value) { zoom = Math.Clamp(value, .05, 3); Refresh(); }
    void Refresh()
    {
        if (shell is null) return;
        refreshing = true;
        try
        {
            Title = $"{model.Book.Title}{(model.IsDirty ? " *" : "")} — MillViewMemories";
            pages.SelectedItem = model.SelectedPage;
            var index = model.Book.Pages.IndexOf(model.SelectedPage!);
            var visible = model.Book.Pages.Skip(Math.Max(0, index)).Take(twoPages ? 2 : 1).ToList();
            if (!canvases.Select(c => c.Page?.Id).SequenceEqual(visible.Select(p => p.Id)))
            {
                foreach (var canvas in canvases) canvas.Dispose(); canvases.Clear(); workspace.Children.Clear();
                foreach (var page in visible)
                {
                    var canvas = new PageCanvas(model) { Page = page, Zoom = zoom }; canvases.Add(canvas);
                    workspace.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = canvas, VerticalAlignment = VerticalAlignment.Top });
                }
            }
            for (var i = 0; i < canvases.Count; i++) { canvases[i].Page = visible[i]; canvases[i].Zoom = zoom; canvases[i].Refresh(); }
            BuildProperties();
        }
        finally { refreshing = false; }
    }
    TextBox Field(StackPanel panel, string label, string value, bool multiline = false)
    {
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        var box = new TextBox { Text = value, AcceptsReturn = multiline, MinHeight = multiline ? 80 : 30, PlaceholderText = label };
        Avalonia.Automation.AutomationProperties.SetName(box, label); panel.Children.Add(box); return box;
    }
    static double Number(TextBox box) => double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value) && double.IsFinite(value)
        ? value : throw new InvalidDataException("Enter a valid finite number.");
    void BuildProperties()
    {
        properties.Children.Clear(); properties.Children.Add(Heading("Page & objects"));
        properties.Children.Add(new TextBlock { Text = $"{model.Book.Width / 72:0.##} × {model.Book.Height / 72:0.##} inches · {zoom:P0}", TextWrapping = TextWrapping.Wrap });
        var background = Field(properties, "Page background (#RRGGBB)", model.SelectedPage!.Background);
        properties.Children.Add(Button("Apply background", () => Run(() => { model.Edit(() => model.SelectedPage!.Background = background.Text ?? ""); return Task.CompletedTask; })));
        properties.Children.Add(Button("Add text", model.AddText));
        properties.Children.Add(Button("Paste copied object", model.PasteElement));
        layers.ItemsSource = model.SelectedPage.Elements.ToList(); layers.SelectedItem = model.SelectedElement; layers.MaxHeight = 100;
        properties.Children.Add(new TextBlock { Text = "Objects (bottom to top)" }); properties.Children.Add(layers);
        if (model.SelectedElement is not { } item) { properties.Children.Add(new TextBlock { Text = "Select an object on the page to edit it. Drag its corner to resize with proportions locked.", TextWrapping = TextWrapping.Wrap }); return; }
        var x = Field(properties, "X (points)", item.X.ToString("0.###")); var y = Field(properties, "Y (points)", item.Y.ToString("0.###"));
        var width = Field(properties, "Width (points)", item.Width.ToString("0.###")); var height = Field(properties, "Height (points)", item.Height.ToString("0.###"));
        var rotation = Field(properties, "Rotation (degrees, centre pivot)", item.Rotation.ToString("0.###"));
        var opacity = Field(properties, "Opacity (0–1)", item.Opacity.ToString("0.###"));
        TextBox? text = null, font = null, size = null, colour = null; ComboBox? alignment = null;
        if (item.Kind == "text")
        {
            text = Field(properties, "Text (newlines supported)", item.Text, true); font = Field(properties, "Font family", item.FontFamily);
            size = Field(properties, "Font size (points)", item.FontSize.ToString()); colour = Field(properties, "Text colour (#RRGGBB)", item.Colour);
            alignment = new ComboBox { ItemsSource = new[] { "left", "center", "right" }, SelectedItem = item.Alignment }; properties.Children.Add(alignment);
        }
        properties.Children.Add(Button("Apply object changes", () => Run(() =>
        {
            model.Edit(() =>
            {
                item.X = Number(x); item.Y = Number(y); item.Width = Number(width); item.Height = Number(height); item.Rotation = Number(rotation); item.Opacity = Number(opacity);
                if (text is not null) { item.Text = text.Text ?? ""; item.FontFamily = font!.Text ?? "Arial"; item.FontSize = Number(size!); item.Colour = colour!.Text ?? ""; item.Alignment = (string)alignment!.SelectedItem!; }
            }); return Task.CompletedTask;
        })));
        var buttons = new WrapPanel(); buttons.Children.Add(Button("Duplicate", model.DuplicateElement)); buttons.Children.Add(Button("Delete", model.DeleteElement));
        buttons.Children.Add(Button("Copy object", model.CopyElement));
        buttons.Children.Add(Button("Backward", () => model.MoveElement(-1))); buttons.Children.Add(Button("Forward", () => model.MoveElement(1))); properties.Children.Add(buttons);
    }

    async Task<string?> ChooseFolder(string title)
    {
        var suggested = Directory.Exists(settings.DefaultProjectFolder) ? await StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(settings.DefaultProjectFolder))) : null;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = suggested });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
    async Task<string?> Dialog(string title, StackPanel content, params string[] choices)
    {
        var dialog = new Window { Title = title, Width = 480, MaxHeight = 750, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        panel.Children.Add(content); var buttons = new WrapPanel(); panel.Children.Add(buttons);
        foreach (var choice in choices) buttons.Children.Add(Button(choice, () => dialog.Close(choice)));
        dialog.Content = new ScrollViewer { Content = panel }; return await dialog.ShowDialog<string?>(this);
    }
    Task<string?> Message(string title, string message) => Dialog(title, new StackPanel { Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } } }, "OK");
    async Task<bool> ConfirmLeave()
    {
        if (!model.IsDirty) return true;
        var answer = await Dialog("Unsaved changes", new StackPanel { Children = { new TextBlock { Text = "Save your changes before continuing?" } } }, "Save", "Discard", "Cancel");
        if (answer == "Save") return await Save(false);
        if (answer == "Discard")
        {
            var recovery = Path.Combine(model.Root ?? model.WorkingRoot, ".recovery.mvm");
            if (File.Exists(recovery)) File.Delete(recovery);
            return true;
        }
        return false;
    }
    async Task NewProject()
    {
        if (!await ConfirmLeave()) return;
        var panel = new StackPanel { Spacing = 8 };
        var title = Field(panel, "1. Album title", "My memories");
        panel.Children.Add(new TextBlock { Text = "2. Choose a page size (all pages share it)" });
        var preset = new ComboBox { ItemsSource = new[] { "12 × 12 inches", "7 × 7 inches", "A4 portrait", "A4 landscape", "Custom inches" }, SelectedIndex = 0 }; panel.Children.Add(preset);
        var width = Field(panel, "Custom width in inches", "12"); var height = Field(panel, "Custom height in inches", "12");
        panel.Children.Add(new TextBlock { Text = "3. Continue to choose a parent folder. A new album folder will be created.", TextWrapping = TextWrapping.Wrap });
        if (await Dialog("Create a scrapbook", panel, "Continue", "Cancel") != "Continue") return;
        var name = title.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..") throw new InvalidDataException("Choose a valid album title for its folder name.");
        var dimensions = preset.SelectedIndex switch { 0 => (864d, 864d), 1 => (504d, 504d), 2 => (595.275590551, 841.88976378), 3 => (841.88976378, 595.275590551), _ => (Number(width) * 72, Number(height) * 72) };
        var book = new Scrapbook { Title = name, Width = dimensions.Item1, Height = dimensions.Item2 }; ProjectStore.Validate(book);
        var parent = await ChooseFolder("Choose parent folder for your new album"); if (parent is null) return;
        var root = ProjectStore.SafePath(parent, name);
        if (Directory.Exists(root)) throw new IOException("That album folder already exists. Choose another title or folder.");
        using var prospective = new LockTransfer(ProjectStore.AcquireLock(root));
        await Task.Run(() => ProjectStore.Save(book, root, root, cancellationToken: Token));
        projectLock?.Dispose(); projectLock = prospective.Take(); model.Load(book, root, root); settings.Remember(root);
        status.Text = "Album created. Import a photograph or add text to get started.";
    }
    // Ensures a cancelled or failed switch never releases the current project's lock.
    sealed class LockTransfer(FileStream stream) : IDisposable
    {
        FileStream? value = stream;
        public FileStream Take() { var result = value!; value = null; return result; }
        public void Dispose() => value?.Dispose();
    }
    async Task OpenProject(string? root)
    {
        if (!await ConfirmLeave()) return;
        if (root is null)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open project.mvm", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("MillViewMemories project") { Patterns = ["*.mvm"] }] });
            var path = files.FirstOrDefault()?.TryGetLocalPath(); if (path is null) return;
            if (!string.Equals(Path.GetFileName(path), "project.mvm", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose the album's project.mvm file. Use Recover draft for unsaved drafts.");
            root = Path.GetDirectoryName(path)!;
        }
        root = Path.GetFullPath(root);
        var sameProject = string.Equals(root, model.Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        using var prospective = sameProject ? null : new LockTransfer(ProjectStore.AcquireLock(root));
        var recovered = false;
        Scrapbook book;
        try { book = await Task.Run(() => ProjectStore.Open(root, cancellationToken: Token)); }
        catch (Exception ex) when (ex is not OperationCanceledException && File.Exists(Path.Combine(root, "project.mvm.previous")))
        {
            if (await Dialog("Previous save available", new StackPanel { Children = { new TextBlock { Text = "The current save could not be read. Try the previous save?", TextWrapping = TextWrapping.Wrap } } }, "Open previous", "Cancel") != "Open previous") return;
            book = await Task.Run(() => ProjectStore.Open(root, "project.mvm.previous", Token));
            recovered = true;
        }
        var recoveryPath = Path.Combine(root, ".recovery.mvm");
        if (File.Exists(recoveryPath) && File.GetLastWriteTimeUtc(recoveryPath) > File.GetLastWriteTimeUtc(Path.Combine(root, "project.mvm")))
        {
            if (await Dialog("Recovery available", new StackPanel { Children = { new TextBlock { Text = "A newer recovery snapshot exists. Restore it without replacing your saved project?", TextWrapping = TextWrapping.Wrap } } }, "Restore", "Use saved") == "Restore")
            { book = await Task.Run(() => ProjectStore.Open(root, ".recovery.mvm", Token)); recovered = true; }
        }
        if (prospective is not null) { projectLock?.Dispose(); projectLock = prospective.Take(); }
        model.Load(book, root, root);
        if (recovered) model.Edit(() => model.Book.ModifiedUtc = DateTime.UtcNow);
        settings.Remember(root); status.Text = recovered ? "Recovery restored. Save to keep it." : "Album opened.";
    }
    async Task OpenRecent()
    {
        var panel = new StackPanel { Spacing = 8 }; var list = new ListBox { ItemsSource = settings.RecentProjects, MaxHeight = 250 }; panel.Children.Add(list);
        if (await Dialog("Recent albums", panel, "Open", "Cancel") == "Open" && list.SelectedItem is string root) await OpenProject(root);
    }
    async Task RecoverDraft()
    {
        var drafts = Path.Combine(UserSettings.DataFolder, "drafts");
        var available = Directory.Exists(drafts) ? Directory.EnumerateDirectories(drafts).Where(d => File.Exists(Path.Combine(d, ".recovery.mvm"))).OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, ".recovery.mvm"))).ToList() : [];
        if (available.Count == 0) { await Message("Recover draft", "No unsaved draft snapshots are available. Recovery snapshots are created every two minutes."); return; }
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(new TextBlock { Text = "Choose an unsaved draft folder (newest first)." });
        var list = new ListBox { ItemsSource = available, MaxHeight = 300 }; panel.Children.Add(list);
        if (await Dialog("Recover draft", panel, "Restore", "Cancel") != "Restore" || list.SelectedItem is not string root) return;
        if (!await ConfirmLeave()) return;
        using var prospective = new LockTransfer(ProjectStore.AcquireLock(root));
        var book = await Task.Run(() => ProjectStore.Open(root, ".recovery.mvm", Token));
        projectLock?.Dispose(); projectLock = prospective.Take(); model.Load(book, null, root); status.Text = "Draft restored. Use Save to choose a permanent album folder.";
    }
    async Task<bool> Save(bool saveAs)
    {
        var root = model.Root; FileStream? newLock = null;
        if (saveAs || root is null)
        {
            root = await ChooseFolder("Choose an empty destination folder for the album"); if (root is null) return false;
            if (Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Choose an empty folder to keep existing files safe.");
            newLock = ProjectStore.AcquireLock(root);
        }
        try
        {
            var book = MainViewModel.Copy(model.Book); book.Pages = MainViewModel.Copy(model.Book.Pages); book.ModifiedUtc = DateTime.UtcNow;
            await Task.Run(() => ProjectStore.Save(book, root, model.WorkingRoot, cancellationToken: Token));
            if (newLock is not null) { projectLock?.Dispose(); projectLock = newLock; newLock = null; }
            model.SetStorage(root); model.MarkSaved(); settings.Remember(root); status.Text = "All changes saved."; return true;
        }
        finally { newLock?.Dispose(); }
    }
    async Task Autosave()
    {
        var snapshot = MainViewModel.Copy(model.Book); snapshot.Pages = MainViewModel.Copy(model.Book.Pages);
        var root = model.Root ?? model.WorkingRoot;
        await Task.Run(() => ProjectStore.Save(snapshot, root, model.WorkingRoot, ".recovery.mvm", cancellationToken: Token));
        status.Text = model.Root is null ? $"Draft recovery saved. Use Save to choose an album location. Draft folder: {root}" : "Recovery snapshot saved; use Save to keep your changes.";
    }
    async Task RenamePage()
    {
        var panel = new StackPanel { Spacing = 8 }; var name = Field(panel, "Page name", model.SelectedPage!.Name);
        if (await Dialog("Rename page", panel, "Apply", "Cancel") == "Apply") model.Edit(() => model.SelectedPage!.Name = name.Text?.Trim() ?? "");
    }
    async Task ImportFiles()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import photographs and decorations", AllowMultiple = true, FileTypeFilter = [new FilePickerFileType("JPEG and PNG") { Patterns = ["*.jpg", "*.jpeg", "*.png"] }] });
        await ImportPaths(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }
    async Task ImportPaths(IEnumerable<string> paths)
    {
        var failures = new List<string>(); var count = 0;
        foreach (var path in paths)
        {
            Token.ThrowIfCancellationRequested();
            try
            {
                var resource = await Task.Run(() => ProjectStore.Import(path, model.WorkingRoot, Token));
                if (model.Book.Resources.All(r => r.Hash != resource.Hash)) { model.Edit(() => model.Book.Resources.Add(resource)); count++; }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { failures.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        status.Text = $"Imported {count} resources. Select one, then choose Place selected resource.";
        if (failures.Count > 0) await Message("Some files were not imported", string.Join("\n", failures));
    }
    async Task BrowseLibrary()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Configured folders are scanned on demand, including subfolders. Only selected images are copied into the album.", TextWrapping = TextWrapping.Wrap });
        var list = new ListBox { SelectionMode = SelectionMode.Multiple, MaxHeight = 350 };
        var paths = await Task.Run(() => settings.ResourceLocations.Where(Directory.Exists).SelectMany(root => Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Where(p => { Token.ThrowIfCancellationRequested(); return Path.GetExtension(p).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png"; })).Take(10000).ToList());
        list.ItemsSource = paths; panel.Children.Add(list);
        if (await Dialog("Resource folders", panel, "Import selected", "Cancel") == "Import selected") await ImportPaths(list.SelectedItems!.Cast<string>().ToArray());
    }
    async Task Preferences()
    {
        var panel = new StackPanel { Spacing = 8 };
        var folder = Field(panel, "Default project parent folder", settings.DefaultProjectFolder);
        var locations = Field(panel, "Resource folders (one absolute path per line)", string.Join("\n", settings.ResourceLocations), true);
        if (await Dialog("Preferences", panel, "Save preferences", "Cancel") != "Save preferences") return;
        var proposed = (locations.Text ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
        if (!Directory.Exists(folder.Text) || proposed.Any(p => !Path.IsPathFullyQualified(p) || !Directory.Exists(p))) throw new InvalidDataException("Enter existing folders using absolute paths.");
        settings.DefaultProjectFolder = folder.Text!; settings.ResourceLocations = proposed; settings.Save();
    }
    async Task Export()
    {
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(new TextBlock { Text = "Export all pages in album order." });
        var format = new ComboBox { ItemsSource = new[] { "PNG", "JPEG", "PDF" }, SelectedIndex = 0 }; panel.Children.Add(format);
        var dpi = Field(panel, "Raster resolution (36–600 DPI; PDF preserves vectors)", "300");
        if (await Dialog("Export album", panel, "Choose destination", "Cancel") != "Choose destination") return;
        var selected = (string)format.SelectedItem!; var resolution = Number(dpi); string? destination;
        if (selected == "PDF")
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export album PDF", SuggestedFileName = "album.pdf", DefaultExtension = "pdf", ShowOverwritePrompt = true }); destination = file?.TryGetLocalPath();
        }
        else
        {
            destination = await ChooseFolder("Choose output folder for page images");
            if (destination is not null && Enumerable.Range(1, model.Book.Pages.Count).Any(i => File.Exists(Path.Combine(destination, $"page-{i:D3}.{(selected == "JPEG" ? "jpg" : "png")}"))))
                if (await Dialog("Replace exported pages?", new StackPanel { Children = { new TextBlock { Text = "Some page images already exist in this folder." } } }, "Replace", "Cancel") != "Replace") return;
        }
        if (destination is null) return;
        var book = MainViewModel.Copy(model.Book); book.Pages = MainViewModel.Copy(model.Book.Pages);
        await Task.Run(() => { using var renderer = new PageRenderer(); renderer.Export(book, model.WorkingRoot, destination, selected, resolution, Token); });
        status.Text = $"Exported {book.Pages.Count} pages as {selected}.";
    }
    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (busy) return;
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (command && e.Key == Key.S) { Run(() => Save(e.KeyModifiers.HasFlag(KeyModifiers.Shift))); e.Handled = true; }
        else if (command && e.Key == Key.O) { Run(() => OpenProject(null)); e.Handled = true; }
        else if (command && e.Key == Key.N) { Run(NewProject); e.Handled = true; }
        else if (e.Source is not TextBox)
        {
            if (command && e.Key == Key.Z) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) model.Redo(); else model.Undo(); e.Handled = true; }
            else if (command && e.Key == Key.Y) { model.Redo(); e.Handled = true; }
            else if (command && e.Key == Key.D) { Run(() => { model.DuplicateElement(); return Task.CompletedTask; }); e.Handled = true; }
            else if (command && e.Key == Key.C) { model.CopyElement(); e.Handled = true; }
            else if (command && e.Key == Key.V) { Run(() => { model.PasteElement(); return Task.CompletedTask; }); e.Handled = true; }
            else if (e.Key == Key.Delete) { model.DeleteElement(); e.Handled = true; }
        }
    }
    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closing) return;
        e.Cancel = true; if (busy) return;
        busy = true; shell.IsEnabled = false;
        try { if (await ConfirmLeave()) { closing = true; Close(); } }
        catch (Exception ex) { await Message("Could not close", ex.Message); }
        finally { busy = false; if (!closing) shell.IsEnabled = true; }
    }
}
