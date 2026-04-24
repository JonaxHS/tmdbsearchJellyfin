using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TmdbAutoImport.Configuration;

namespace Jellyfin.Plugin.TmdbAutoImport.Services;

public sealed class ImportService
{
    private readonly PluginConfiguration _config;

    public ImportService(PluginConfiguration config)
    {
        _config = config;
    }

    public async Task<string> ImportMovieAsync(TmdbSearchItem item, CancellationToken cancellationToken)
    {
        var title = Sanitize(item.Title ?? item.Name ?? $"movie-{item.Id}");
        var year = ExtractYear(item.ReleaseDate);
        var folderName = string.IsNullOrEmpty(year) ? title : $"{title} ({year})";

        var root = ResolveMoviesRootPath();
        var movieFolder = Path.Combine(root, folderName);
        Directory.CreateDirectory(movieFolder);

        var baseName = Path.Combine(movieFolder, folderName);
        var strmPath = baseName + ".strm";
        var nfoPath = baseName + ".nfo";

        var strm = _config.MovieStrmUrlTemplate.Replace("{tmdbId}", item.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        await File.WriteAllTextAsync(strmPath, strm + Environment.NewLine, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(nfoPath, BuildMovieNfo(item), Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        return movieFolder;
    }

    public async Task<string> ImportSeriesAsync(TmdbSearchItem item, CancellationToken cancellationToken)
    {
        var title = Sanitize(item.Name ?? item.Title ?? $"series-{item.Id}");
        var year = ExtractYear(item.FirstAirDate);
        var folderName = string.IsNullOrEmpty(year) ? title : $"{title} ({year})";

        var root = ResolveSeriesRootPath();
        var seriesFolder = Path.Combine(root, folderName);
        Directory.CreateDirectory(seriesFolder);

        var nfoPath = Path.Combine(seriesFolder, "tvshow.nfo");
        await File.WriteAllTextAsync(nfoPath, BuildSeriesNfo(item), Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        return seriesFolder;
    }

    private string ResolveMoviesRootPath()
    {
        if (!string.IsNullOrWhiteSpace(_config.MoviesImportPath))
        {
            var moviesRoot = Path.GetFullPath(_config.MoviesImportPath);
            Directory.CreateDirectory(moviesRoot);
            return moviesRoot;
        }

        return BuildLegacyRootPath("Movies");
    }

    private string ResolveSeriesRootPath()
    {
        if (!string.IsNullOrWhiteSpace(_config.SeriesImportPath))
        {
            var seriesRoot = Path.GetFullPath(_config.SeriesImportPath);
            Directory.CreateDirectory(seriesRoot);
            return seriesRoot;
        }

        return BuildLegacyRootPath("Series");
    }

    private string BuildLegacyRootPath(string mediaTypeFolder)
    {
        if (string.IsNullOrWhiteSpace(_config.ImportRootPath))
        {
            throw new InvalidOperationException("Configure MoviesImportPath and SeriesImportPath, or set legacy ImportRootPath.");
        }

        var fullRoot = Path.GetFullPath(_config.ImportRootPath);
        var root = Path.Combine(fullRoot, mediaTypeFolder);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ExtractYear(string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || date.Length < 4)
        {
            return string.Empty;
        }

        return date[..4];
    }

    private static string Sanitize(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Trim();
    }

    private static string BuildMovieNfo(TmdbSearchItem item)
    {
        var title = EscapeXml(item.Title ?? item.Name ?? string.Empty);
        var overview = EscapeXml(item.Overview ?? string.Empty);

        return $"""
<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>
<movie>
  <title>{title}</title>
  <plot>{overview}</plot>
  <tmdbid>{item.Id}</tmdbid>
  <uniqueid type=\"tmdb\" default=\"true\">{item.Id}</uniqueid>
</movie>
""";
    }

    private static string BuildSeriesNfo(TmdbSearchItem item)
    {
        var title = EscapeXml(item.Name ?? item.Title ?? string.Empty);
        var overview = EscapeXml(item.Overview ?? string.Empty);

        return $"""
<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>
<tvshow>
  <title>{title}</title>
  <plot>{overview}</plot>
  <tmdbid>{item.Id}</tmdbid>
  <uniqueid type=\"tmdb\" default=\"true\">{item.Id}</uniqueid>
</tvshow>
""";
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }
}
