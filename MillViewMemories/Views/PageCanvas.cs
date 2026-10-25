using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MillViewMemories.Models;
using MillViewMemories.Services;
using MillViewMemories.ViewModels;

namespace MillViewMemories.Views;

public sealed class PageCanvas : Control, IDisposable
{
    readonly MainViewModel model;
    readonly PageRenderer renderer = new();
    Bitmap? preview;
    Point start;
    double oldX, oldY, oldWidth, oldHeight;
    bool dragging, resizing, transaction;
    public double Zoom { get; set; } = .65;
    public ScrapPage? Page { get; set; }
    public PageCanvas(MainViewModel model) { this.model = model; Focusable = true; ClipToBounds = true; }
    public void Refresh()
    {
        if (Page is null) return;
        Width = model.Book.Width * Zoom; Height = model.Book.Height * Zoom;
        preview?.Dispose();
        using var bytes = new MemoryStream(renderer.Raster(model.Book, Page, model.WorkingRoot, Math.Min(72, 1000 * 72 / Math.Max(model.Book.Width, model.Book.Height))));
        preview = new Bitmap(bytes); InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (preview is not null) context.DrawImage(preview, new Rect(Bounds.Size));
        if (Page != model.SelectedPage || model.SelectedElement is not { } e) return;
        using (context.PushTransform(Matrix.CreateRotation(e.Rotation * Math.PI / 180, new Point((e.X + e.Width / 2) * Zoom, (e.Y + e.Height / 2) * Zoom))))
        {
            var rect = new Rect(e.X * Zoom, e.Y * Zoom, e.Width * Zoom, e.Height * Zoom);
            context.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 2), rect);
            context.DrawRectangle(Brushes.White, new Pen(Brushes.DodgerBlue, 2), new Rect(rect.BottomRight - new Vector(5, 5), new Size(10, 10)));
        }
    }
    static Point Local(Point point, PageElement e)
    {
        var x = point.X - e.X - e.Width / 2; var y = point.Y - e.Y - e.Height / 2;
        var r = -e.Rotation * Math.PI / 180;
        return new Point(x * Math.Cos(r) - y * Math.Sin(r) + e.Width / 2, x * Math.Sin(r) + y * Math.Cos(r) + e.Height / 2);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Page is null) return;
        Focus();
        if (Page != model.SelectedPage) model.SelectedPage = Page;
        var point = e.GetPosition(this); start = new Point(point.X / Zoom, point.Y / Zoom);
        var selected = model.SelectedElement;
        resizing = selected is not null && Math.Abs(Local(start, selected).X - selected.Width) * Zoom < 12 && Math.Abs(Local(start, selected).Y - selected.Height) * Zoom < 12;
        if (!resizing)
            model.SelectedElement = Page.Elements.LastOrDefault(item => { var p = Local(start, item); return p.X >= 0 && p.Y >= 0 && p.X <= item.Width && p.Y <= item.Height; });
        if (model.SelectedElement is not { } item) return;
        oldX = item.X; oldY = item.Y; oldWidth = item.Width; oldHeight = item.Height;
        dragging = true; transaction = false; e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!dragging || model.SelectedElement is not { } item) return;
        var point = e.GetPosition(this); var dx = point.X / Zoom - start.X; var dy = point.Y / Zoom - start.Y;
        if (!transaction && Math.Abs(dx) + Math.Abs(dy) < 1) return;
        if (!transaction) { model.BeginEdit(); transaction = true; }
        if (resizing)
        {
            // Aspect ratio stays locked; use the unrotated horizontal displacement.
            var r = -item.Rotation * Math.PI / 180;
            item.Width = Math.Clamp(oldWidth + dx * Math.Cos(r) - dy * Math.Sin(r), 1, 72000);
            item.Height = Math.Clamp(oldHeight * item.Width / oldWidth, 1, 72000);
        }
        else { item.X = Math.Clamp(oldX + dx, -72000, 72000); item.Y = Math.Clamp(oldY + dy, -72000, 72000); }
        Refresh(); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); Finish(); e.Pointer.Capture(null);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); Finish(); }
    void Finish() { dragging = false; if (transaction) { transaction = false; model.CompleteEdit(); } }
    public void Dispose() { preview?.Dispose(); renderer.Dispose(); }
}
