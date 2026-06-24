using System.Xml.Linq;

namespace RefractorForge.Viewer;

/// <summary>
/// A RefractorForge Project XML file that captures everything needed to re-open a project: which game it targets,
/// which mod folder was used, and the level/mesh/texture archive paths chosen in the picker.
/// </summary>
public sealed class ProjectFile
{
    public string Name { get; set; } = "";
    public string Game { get; set; } = "BFVietnam";   // "BF1942" or "BFVietnam"
    public string? ModFolder { get; set; }
    public string[] LevelArchives { get; set; } = Array.Empty<string>();
    public string[] MeshArchives { get; set; } = Array.Empty<string>();
    public string[] TextureArchives { get; set; } = Array.Empty<string>();

    public static ProjectFile? Load(string path)
    {
        try
        {
            var root = XDocument.Load(path).Root;
            if (root is null) return null;
            return new ProjectFile
            {
                Name = root.Element("Name")?.Value ?? Path.GetFileNameWithoutExtension(path),
                Game = root.Element("Game")?.Value?.Trim() ?? "BFVietnam",
                ModFolder = NullIfEmpty(root.Element("ModFolder")?.Value),
                LevelArchives = Elems(root, "LevelArchives"),
                MeshArchives = Elems(root, "MeshArchives"),
                TextureArchives = Elems(root, "TextureArchives"),
            };
        }
        catch { return null; }
    }

    public void Save(string path)
    {
        var root = new XElement("RefractorForgeProject",
            new XElement("Name", Name),
            new XElement("Game", Game),
            ModFolder is not null ? new XElement("ModFolder", ModFolder) : null!,
            new XElement("LevelArchives", LevelArchives.Select(a => new XElement("Archive", a))),
            new XElement("MeshArchives", MeshArchives.Select(a => new XElement("Archive", a))),
            new XElement("TextureArchives", TextureArchives.Select(a => new XElement("Archive", a)))
        );
        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(path);
    }

    private static string[] Elems(XElement root, string parent)
        => root.Element(parent)?.Elements("Archive").Select(e => e.Value).Where(v => v.Length > 0).ToArray()
           ?? Array.Empty<string>();

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
