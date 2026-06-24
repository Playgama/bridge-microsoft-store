using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Playgama.Packager;

internal sealed class PublisherProfile
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("publisher")] public string? Publisher { get; set; }
    [JsonPropertyName("publisherDisplayName")] public string? PublisherDisplayName { get; set; }
    [JsonPropertyName("pfx")] public string? Pfx { get; set; }
    [JsonPropertyName("pfxPassword")] public string? PfxPassword { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "(unnamed)" : Name;
}

// Loads/saves publishers.json (repo root). The file is git-ignored, so it stays local.
internal static class PublisherStore
{
    public static string PathFor(string root) => Path.Combine(root, "publishers.json");

    public static List<PublisherProfile> Load(string root)
    {
        var file = PathFor(root);
        if (!File.Exists(file)) return new();
        try { return JsonSerializer.Deserialize<List<PublisherProfile>>(File.ReadAllText(file)) ?? new(); }
        catch { return new(); }
    }

    public static void Save(string root, List<PublisherProfile> list)
    {
        File.WriteAllText(PathFor(root), JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }
}

// Tiny per-user settings (e.g. last selected publisher), stored in LocalAppData — never in the repo.
internal static class LocalSettings
{
    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlaygamaPackager", "settings.json");

    public static string GetLastPublisher()
    {
        try
        {
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o)
                return (string?)o["lastPublisher"] ?? "";
        }
        catch { }
        return "";
    }

    public static void SetLastPublisher(string name)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, new JsonObject { ["lastPublisher"] = name }.ToJsonString());
        }
        catch { }
    }
}
