using System.Text.Json;
namespace MillViewMemories.Services;
public sealed class UserSettings
{
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MillViewMemories");
    public string DefaultProjectFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public List<string> RecentProjects { get; set; } = [];
    public List<string> ResourceLocations { get; set; } = [];
    public static UserSettings Load()
    {
        try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(Path.Combine(DataFolder, "settings.json"))) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataFolder);
        var path = Path.Combine(DataFolder, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, ProjectStore.Json)); File.Move(path + ".tmp", path, true);
    }
    public void Remember(string root)
    {
        RecentProjects.Remove(root); RecentProjects.Insert(0, root); RecentProjects = RecentProjects.Take(10).ToList(); Save();
    }
}
