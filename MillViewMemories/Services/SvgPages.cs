using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using MillViewMemories.Models;

namespace MillViewMemories.Services;

/// <summary>Version-one SVG subset. Unknown content is rejected, never silently dropped.</summary>
public static class SvgPages
{
    public static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    public static readonly XNamespace Mvm = "urn:millviewmemories:page:1";
    static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    static double D(XElement e, XName name) => double.Parse((string?)e.Attribute(name)
        ?? throw new InvalidDataException($"Missing SVG attribute: {name}"), CultureInfo.InvariantCulture);

    public static XDocument Write(Scrapbook book, ScrapPage page, string pagePath, string root)
    {
        var svg = new XElement(Svg + "svg", new XAttribute("xmlns", Svg), new XAttribute(XNamespace.Xmlns + "mvm", Mvm),
            new XAttribute("width", N(book.Width) + "pt"), new XAttribute("height", N(book.Height) + "pt"),
            new XAttribute("viewBox", $"0 0 {N(book.Width)} {N(book.Height)}"), new XAttribute("id", page.Id),
            new XElement(Svg + "metadata", new XElement(Mvm + "page", new XAttribute("version", "1"))),
            new XElement(Svg + "rect", new XAttribute("id", "background"), new XAttribute("width", N(book.Width)),
                new XAttribute("height", N(book.Height)), new XAttribute("fill", page.Background)));
        foreach (var item in page.Elements)
        {
            var group = new XElement(Svg + "g", new XAttribute("id", item.Id),
                new XAttribute("transform", $"translate({N(item.X)} {N(item.Y)}) rotate({N(item.Rotation)} {N(item.Width / 2)} {N(item.Height / 2)})"),
                new XAttribute("opacity", N(item.Opacity)));
            if (item.Kind == "image")
            {
                var resource = book.Resources.Single(r => r.Id == item.ResourceId);
                group.Add(new XElement(Svg + "image", new XAttribute("width", N(item.Width)), new XAttribute("height", N(item.Height)),
                    new XAttribute("preserveAspectRatio", "none"), new XAttribute(Mvm + "resource", resource.Id),
                    new XAttribute("href", Path.GetRelativePath(Path.GetDirectoryName(pagePath)!, ProjectStore.SafePath(root, resource.Path)).Replace('\\', '/'))));
            }
            else
            {
                var text = new XElement(Svg + "text", new XAttribute("font-family", item.FontFamily),
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    new XAttribute("font-size", N(item.FontSize)), new XAttribute("fill", item.Colour),
                    new XAttribute("text-anchor", item.Alignment == "center" ? "middle" : item.Alignment == "right" ? "end" : "start"),
                    new XAttribute(Mvm + "width", N(item.Width)), new XAttribute(Mvm + "height", N(item.Height)));
                var x = item.Alignment == "center" ? item.Width / 2 : item.Alignment == "right" ? item.Width : 0;
                var lines = item.Text.Replace("\r", "").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                    text.Add(new XElement(Svg + "tspan", new XAttribute("x", N(x)), new XAttribute("y", N(item.FontSize * (1 + i * 1.2))), lines[i]));
                group.Add(text);
            }
            svg.Add(group);
        }
        return new XDocument(svg);
    }

    public static ScrapPage Read(Scrapbook book, PageReference reference, string path, string root)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8_000_000 });
        var document = XDocument.Load(reader);
        var svg = document.Root ?? throw new InvalidDataException("Empty SVG page.");
        if (svg.Name != Svg + "svg" || (string?)svg.Attribute("id") != reference.Id ||
            (string?)svg.Element(Svg + "metadata")?.Element(Mvm + "page")?.Attribute("version") != "1")
            throw new InvalidDataException("Unsupported SVG page or page version.");
        if ((string?)svg.Attribute("width") != N(book.Width) + "pt" || (string?)svg.Attribute("height") != N(book.Height) + "pt" ||
            (string?)svg.Attribute("viewBox") != $"0 0 {N(book.Width)} {N(book.Height)}")
            throw new InvalidDataException("SVG dimensions disagree with the project.");
        var page = new ScrapPage { Id = reference.Id, Name = reference.Name };
        var children = svg.Elements().ToList();
        var background = children.ElementAtOrDefault(1);
        if (background?.Name != Svg + "rect" || (string?)background.Attribute("id") != "background")
            throw new InvalidDataException("Missing SVG background.");
        page.Background = (string?)background.Attribute("fill") ?? "#FFFFFF";
        foreach (var group in children.Skip(2))
        {
            if (group.Name != Svg + "g" || group.Elements().Count() != 1) throw new InvalidDataException("Unsupported SVG element.");
            var item = new PageElement { Id = (string?)group.Attribute("id") ?? "", Opacity = D(group, "opacity") };
            var transform = (string?)group.Attribute("transform") ?? "";
            var values = transform.Replace("translate(", "").Replace(") rotate(", " ").Replace(")", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != 5) throw new InvalidDataException("Unsupported SVG transform.");
            item.X = double.Parse(values[0], CultureInfo.InvariantCulture); item.Y = double.Parse(values[1], CultureInfo.InvariantCulture);
            item.Rotation = double.Parse(values[2], CultureInfo.InvariantCulture);
            var content = group.Elements().Single();
            if (content.Name == Svg + "image")
            {
                item.Kind = "image"; item.Width = D(content, "width"); item.Height = D(content, "height");
                item.ResourceId = (string?)content.Attribute(Mvm + "resource");
                var resource = book.Resources.SingleOrDefault(r => r.Id == item.ResourceId) ?? throw new InvalidDataException("Unknown image resource.");
                var href = (string?)content.Attribute("href") ?? "";
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, href));
                if (resolved != ProjectStore.SafePath(root, resource.Path)) throw new InvalidDataException("Invalid SVG resource reference.");
            }
            else if (content.Name == Svg + "text")
            {
                item.Width = D(content, Mvm + "width"); item.Height = D(content, Mvm + "height");
                item.FontSize = D(content, "font-size"); item.FontFamily = (string?)content.Attribute("font-family") ?? "Arial";
                item.Colour = (string?)content.Attribute("fill") ?? "#333333";
                item.Alignment = (string?)content.Attribute("text-anchor") switch { "middle" => "center", "end" => "right", _ => "left" };
                item.Text = string.Join("\n", content.Elements(Svg + "tspan").Select(t => t.Value));
            }
            else throw new InvalidDataException("Unsupported SVG content.");
            page.Elements.Add(item);
        }
        var canonical = Write(book, page, path, root);
        if (!XNode.DeepEquals(Normalize(document.Root!), Normalize(canonical.Root!)))
            throw new InvalidDataException("This SVG contains unsupported edits. The file has not been modified.");
        return page;
    }

    static XElement Normalize(XElement element) => new(element.Name,
        element.Attributes().Where(a => !a.IsNamespaceDeclaration).OrderBy(a => a.Name.ToString()).Select(a => new XAttribute(a)),
        element.Nodes().Where(n => n is not XText t || !string.IsNullOrWhiteSpace(t.Value) || !element.HasElements)
            .Select(n => n is XElement child ? (object)Normalize(child) : n));
}
