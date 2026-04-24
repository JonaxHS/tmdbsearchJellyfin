using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.TmdbAutoImport.Services;

public sealed class TmdbSearchResponse
{
    [JsonPropertyName("results")]
    public List<TmdbSearchItem> Results { get; set; } = new();
}

public sealed class TmdbSearchItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }
}
