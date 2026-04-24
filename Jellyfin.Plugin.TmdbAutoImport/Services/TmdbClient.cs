using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TmdbAutoImport.Configuration;

namespace Jellyfin.Plugin.TmdbAutoImport.Services;

public sealed class TmdbClient
{
    private readonly HttpClient _httpClient;
    private readonly PluginConfiguration _config;

    public TmdbClient(HttpClient httpClient, PluginConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<TmdbSearchResponse> SearchAsync(string query, string type, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.TmdbApiKey))
        {
            throw new InvalidOperationException("TMDb API key is not configured.");
        }

        var endpoint = type.Equals("series", StringComparison.OrdinalIgnoreCase) || type.Equals("tv", StringComparison.OrdinalIgnoreCase)
            ? "tv"
            : "movie";

        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"https://api.themoviedb.org/3/search/{endpoint}?api_key={Uri.EscapeDataString(_config.TmdbApiKey)}&query={Uri.EscapeDataString(query)}&language={Uri.EscapeDataString(_config.Language)}&region={Uri.EscapeDataString(_config.Country)}");

        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var parsed = await JsonSerializer.DeserializeAsync<TmdbSearchResponse>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return parsed ?? new TmdbSearchResponse();
    }
}
