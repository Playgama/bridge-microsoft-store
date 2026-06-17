using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playgama.Packager;

internal sealed class PublisherProfile
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("identityName")] public string? IdentityName { get; set; }
    [JsonPropertyName("publisher")] public string? Publisher { get; set; }
    [JsonPropertyName("publisherDisplayName")] public string? PublisherDisplayName { get; set; }
    [JsonPropertyName("pfx")] public string? Pfx { get; set; }
    [JsonPropertyName("pfxPassword")] public string? PfxPassword { get; set; }
    [JsonPropertyName("clientId")] public string? ClientId { get; set; }
    [JsonPropertyName("serviceTicketBaseUrl")] public string? ServiceTicketBaseUrl { get; set; }

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
