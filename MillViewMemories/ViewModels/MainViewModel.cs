using System.Collections.ObjectModel;
using System.Text.Json;
using MillViewMemories.Models;
using MillViewMemories.Services;

namespace MillViewMemories.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    // In-memory history only. Persisted pages are always SVG.
    sealed record Snapshot(string Manifest, string Pages);
    readonly Stack<Snapshot> undo = new(), redo = new();
    Snapshot? saved;
    public Scrapbook Book { get; private set; } = new();
    public string? Root { get; set; }
    public string WorkingRoot { get; private set; } = "";
    public ObservableCollection<ScrapPage> Pages { get; } = [];
    public ObservableCollection<Resource> Resources { get; } = [];
    ScrapPage? selectedPage;
    PageElement? selectedElement;
    PageElement? clipboard;
    public ScrapPage? SelectedPage { get => selectedPage; set { SetProperty(ref selectedPage, value); SelectedElement = null; Changed?.Invoke(); } }
    public PageElement? SelectedElement { get => selectedElement; set { SetProperty(ref selectedElement, value); Changed?.Invoke(); } }
    public bool IsDirty => saved != Capture();
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public event Action? Changed;
    public MainViewModel(string? draftRoot = null)
    {
        Load(new Scrapbook(), null, draftRoot ?? Path.Combine(UserSettings.DataFolder, "drafts", Guid.NewGuid().ToString("N")));
        saved = Capture(); // The untouched welcome draft should not prompt before New/Open.
    }
    Snapshot Capture() => new(JsonSerializer.Serialize(Book, ProjectStore.Json), JsonSerializer.Serialize(Book.Pages, ProjectStore.Json));
    public void Load(Scrapbook book, string? root, string workingRoot)
    {
        Book = book; Root = root; WorkingRoot = workingRoot; Directory.CreateDirectory(workingRoot);
        undo.Clear(); redo.Clear(); clipboard = null; saved = root is null ? null : Capture(); Refresh();
    }
    public void MarkSaved() { saved = Capture(); Changed?.Invoke(); }
    public void SetStorage(string root) { Root = root; WorkingRoot = root; }
    void Remember(Snapshot snapshot)
    {
        undo.Push(snapshot);
        if (undo.Count > 100) { var retained = undo.Take(100).Reverse().ToArray(); undo.Clear(); foreach (var item in retained) undo.Push(item); }
    }
    public void BeginEdit() { Remember(Capture()); redo.Clear(); }
    public void Edit(Action action)
    {
        var previous = Capture();
        try { action(); ProjectStore.Validate(Book); }
        catch { Restore(previous); throw; }
        if (previous != Capture()) { Remember(previous); redo.Clear(); }
        Refresh();
    }
    public void CompleteEdit() { ProjectStore.Validate(Book); Refresh(); }
    public void Undo() { if (undo.TryPop(out var snapshot)) { redo.Push(Capture()); Restore(snapshot); } }
    public void Redo() { if (redo.TryPop(out var snapshot)) { undo.Push(Capture()); Restore(snapshot); } }
    void Restore(Snapshot snapshot)
    {
        Book = JsonSerializer.Deserialize<Scrapbook>(snapshot.Manifest)!;
        Book.Pages = JsonSerializer.Deserialize<List<ScrapPage>>(snapshot.Pages)!; Refresh();
    }
    public void Refresh()
    {
        var pageId = selectedPage?.Id; var elementId = selectedElement?.Id;
        Pages.Clear(); foreach (var page in Book.Pages) Pages.Add(page);
        Resources.Clear(); foreach (var r in Book.Resources) Resources.Add(r);
        selectedPage = Book.Pages.FirstOrDefault(p => p.Id == pageId) ?? Book.Pages[0];
        selectedElement = selectedPage.Elements.FirstOrDefault(e => e.Id == elementId);
        OnPropertyChanged(nameof(SelectedPage)); OnPropertyChanged(nameof(SelectedElement)); Changed?.Invoke();
    }
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    public void AddPage()
    {
        var page = new ScrapPage { Name = $"Page {Book.Pages.Count + 1}" }; Edit(() => Book.Pages.Add(page)); SelectedPage = page;
    }
    public void DuplicatePage()
    {
        if (SelectedPage is not { } source) return;
        var page = Copy(source); page.Id = Guid.NewGuid().ToString("N"); page.Name += " (copy)";
        foreach (var item in page.Elements) item.Id = Guid.NewGuid().ToString("N");
        Edit(() => Book.Pages.Insert(Book.Pages.IndexOf(source) + 1, page)); SelectedPage = page;
    }
    public void MovePage(int direction)
    {
        if (SelectedPage is not { } page) return;
        var index = Book.Pages.IndexOf(page); var target = index + direction;
        if (target < 0 || target >= Book.Pages.Count) return;
        Edit(() => { Book.Pages.RemoveAt(index); Book.Pages.Insert(target, page); });
    }
    public void AddText()
    {
        var item = new PageElement(); Edit(() => SelectedPage!.Elements.Add(item)); SelectedElement = item;
    }
    public void Place(Resource resource)
    {
        var width = Math.Min(Book.Width * .6, resource.Width);
        var item = new PageElement { Kind = "image", ResourceId = resource.Id, Width = width, Height = width * resource.Height / resource.Width };
        Edit(() => SelectedPage!.Elements.Add(item)); SelectedElement = item;
    }
    public void DuplicateElement()
    {
        if (SelectedElement is not { } source) return;
        var item = Copy(source); item.Id = Guid.NewGuid().ToString("N"); item.X += 12; item.Y += 12;
        Edit(() => SelectedPage!.Elements.Add(item)); SelectedElement = item;
    }
    public void DeleteElement() { if (SelectedElement is { } item) Edit(() => SelectedPage!.Elements.Remove(item)); }
    public void CopyElement() { if (SelectedElement is { } item) clipboard = Copy(item); }
    public void PasteElement()
    {
        if (clipboard is null) return;
        if (clipboard.Kind == "image" && !Book.Resources.Any(r => r.Id == clipboard.ResourceId))
            throw new InvalidOperationException("The copied image resource was undone. Redo its import before pasting.");
        var item = Copy(clipboard); item.Id = Guid.NewGuid().ToString("N"); item.X += 12; item.Y += 12;
        Edit(() => SelectedPage!.Elements.Add(item)); SelectedElement = item;
    }
    public void MoveElement(int direction)
    {
        if (SelectedElement is not { } item) return;
        var list = SelectedPage!.Elements; var index = list.IndexOf(item); var target = index + direction;
        if (target < 0 || target >= list.Count) return;
        Edit(() => { list.RemoveAt(index); list.Insert(target, item); });
    }
}
