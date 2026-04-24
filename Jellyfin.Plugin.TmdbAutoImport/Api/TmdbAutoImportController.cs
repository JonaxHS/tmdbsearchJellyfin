using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TmdbAutoImport.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TmdbAutoImport.Api;

[ApiController]
[Route("Plugins/TmdbAutoImport")]
public sealed class TmdbAutoImportController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;

    public TmdbAutoImportController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    [HttpGet("search-or-import")]
    public async Task<ActionResult<SearchOrImportResult>> SearchOrImportAsync(
        [FromServices] TmdbClient tmdbClient,
        [FromServices] ImportService importService,
        [FromQuery] string query,
        [FromQuery] string type = "movie",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest("query is required.");
        }

        var existing = FindInLocalLibrary(query, type);
        if (existing.Count > 0)
        {
            return Ok(new SearchOrImportResult
            {
                Source = "library",
                Imported = false,
                Message = "Found items in local library.",
                Items = existing
            });
        }

        var remote = await tmdbClient.SearchAsync(query, type, cancellationToken).ConfigureAwait(false);
        var top = remote.Results.FirstOrDefault();
        if (top is null)
        {
            return Ok(new SearchOrImportResult
            {
                Source = "none",
                Imported = false,
                Message = "No results found on TMDb."
            });
        }

        var importPath = IsSeries(type)
            ? await importService.ImportSeriesAsync(top, cancellationToken).ConfigureAwait(false)
            : await importService.ImportMovieAsync(top, cancellationToken).ConfigureAwait(false);

        return Ok(new SearchOrImportResult
        {
            Source = "tmdb",
            Imported = true,
            Message = "Item imported as STRM/NFO. Trigger a Jellyfin library scan to index it.",
            ImportPath = importPath,
            TmdbId = top.Id,
            TmdbTitle = top.Title ?? top.Name
        });
    }

    private List<LocalLibraryItem> FindInLocalLibrary(string query, string type)
    {
        var itemType = IsSeries(type) ? BaseItemKind.Series : BaseItemKind.Movie;

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            SearchTerm = query,
            IncludeItemTypes = new[] { itemType },
            Recursive = true,
            Limit = 20
        });

        return items.Select(item => new LocalLibraryItem
        {
            Id = item.Id.ToString("N"),
            Name = item.Name,
            Path = item.Path
        }).ToList();
    }

    private static bool IsSeries(string type)
    {
        return type.Equals("series", StringComparison.OrdinalIgnoreCase)
            || type.Equals("tv", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class SearchOrImportResult
{
    public string Source { get; set; } = "none";

    public bool Imported { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? ImportPath { get; set; }

    public int? TmdbId { get; set; }

    public string? TmdbTitle { get; set; }

    public List<LocalLibraryItem> Items { get; set; } = new();
}

public sealed class LocalLibraryItem
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Path { get; set; }
}
