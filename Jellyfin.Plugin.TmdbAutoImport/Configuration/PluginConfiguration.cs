using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TmdbAutoImport.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string TmdbApiKey { get; set; } = string.Empty;

    // Legacy fallback path. Kept for backward compatibility.
    public string ImportRootPath { get; set; } = string.Empty;

    public string MoviesImportPath { get; set; } = string.Empty;

    public string SeriesImportPath { get; set; } = string.Empty;

    public string Language { get; set; } = "es-ES";

    public string Country { get; set; } = "ES";

    public string MovieStrmUrlTemplate { get; set; } = "https://example.invalid/movie/{tmdbId}";
}
