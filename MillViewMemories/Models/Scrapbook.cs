namespace MillViewMemories.Models;

public sealed class Scrapbook
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "My memories";
    public double Width { get; set; } = 864;
    public double Height { get; set; } = 864;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public List<PageReference> PageFiles { get; set; } = [];
    public List<Resource> Resources { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public List<ScrapPage> Pages { get; set; } = [new()];
}
public sealed record PageReference(string Id, string Name, string Path);
public sealed class Resource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? OriginalPath { get; set; }
    public string MediaType { get; set; } = "image/png";
    public int Width { get; set; }
    public int Height { get; set; }
    public string Hash { get; set; } = "";
    public override string ToString() => Name;
}
public sealed class ScrapPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Page 1";
    public string Background { get; set; } = "#FFFFFF";
    public List<PageElement> Elements { get; set; } = [];
    public override string ToString() => Name;
}
public sealed class PageElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "text";
    public string? ResourceId { get; set; }
    public double X { get; set; } = 48;
    public double Y { get; set; } = 48;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 120;
    public double Rotation { get; set; }
    public double Opacity { get; set; } = 1;
    public string Text { get; set; } = "A memory to keep";
    public string FontFamily { get; set; } = "Arial";
    public double FontSize { get; set; } = 28;
    public string Colour { get; set; } = "#333333";
    public string Alignment { get; set; } = "left";
    public override string ToString() => Kind == "text" ? Text.Replace('\n', ' ') : "Photograph";
}
