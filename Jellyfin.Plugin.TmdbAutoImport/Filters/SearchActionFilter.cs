using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TmdbAutoImport.Configuration;
using Jellyfin.Plugin.TmdbAutoImport.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbAutoImport.Filters;

public sealed class SearchActionFilter(
    ILibraryManager libraryManager,
    TmdbClient tmdbClient,
    ImportService importService,
    IMemoryCache memoryCache,
    ILogger<SearchActionFilter> logger
) : IAsyncActionFilter, IOrderedFilter
{
    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(10);

    public int Order => 1;

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        if (!IsSearchAction(ctx) || !TryGetSearchTerm(ctx, out var searchTerm))
        {
            await next();
            return;
        }

        var requestedTypes = GetRequestedItemTypes(ctx);
        if (requestedTypes.Count == 0)
        {
            await next();
            return;
        }

        var normalizedSearchTerm = NormalizeSearchTerm(searchTerm);
        var cacheKey = BuildCacheKey(normalizedSearchTerm, requestedTypes);

        if (memoryCache.TryGetValue(cacheKey, out QueryResult<BaseItemDto>? cachedResult) && cachedResult is not null)
        {
            ctx.Result = new OkObjectResult(cachedResult);
            return;
        }

        if (HasLocalMatches(searchTerm, requestedTypes))
        {
            await next();
            return;
        }

        try
        {
            var remoteDtos = new List<BaseItemDto>();

            foreach (var requestedType in requestedTypes)
            {
                var tmdbType = requestedType == BaseItemKind.Series ? "series" : "movie";
                var response = await tmdbClient
                    .SearchAsync(searchTerm, tmdbType, ctx.HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                var top = response.Results.FirstOrDefault();
                if (top is null)
                {
                    continue;
                }

                var importPath = requestedType == BaseItemKind.Series
                    ? await importService
                        .ImportSeriesAsync(top, ctx.HttpContext.RequestAborted)
                        .ConfigureAwait(false)
                    : await importService
                        .ImportMovieAsync(top, ctx.HttpContext.RequestAborted)
                        .ConfigureAwait(false);

                remoteDtos.Add(CreateDto(top, requestedType, importPath));
            }

            if (remoteDtos.Count == 0)
            {
                await next();
                return;
            }

            var start = GetIntArgument(ctx, "startIndex", 0);
            var limit = GetIntArgument(ctx, "limit", 25);
            var paged = remoteDtos.Skip(start).Take(limit).ToArray();

            var result = new QueryResult<BaseItemDto>
            {
                Items = paged,
                TotalRecordCount = remoteDtos.Count,
            };

            memoryCache.Set(cacheKey, result, SearchCacheTtl);

            logger.LogInformation(
                "TMDb Auto Import intercepted search \"{Query}\" types=[{Types}] results={Count}",
                searchTerm,
                string.Join(",", requestedTypes),
                remoteDtos.Count
            );

            ctx.Result = new OkObjectResult(result);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDb Auto Import search interception failed for {Query}", searchTerm);
            await next();
        }
    }

    private bool HasLocalMatches(string searchTerm, HashSet<BaseItemKind> requestedTypes)
    {
        var localItems = libraryManager.GetItemList(
            new InternalItemsQuery
            {
                SearchTerm = searchTerm,
                IncludeItemTypes = requestedTypes.ToArray(),
                Recursive = true,
                Limit = 1,
            }
        );

        return localItems.Count > 0;
    }

    private static bool IsSearchAction(ActionExecutingContext ctx)
    {
        return ctx.ActionDescriptor is ControllerActionDescriptor descriptor
            && (descriptor.ActionName.Equals("GetItems", StringComparison.OrdinalIgnoreCase)
                || descriptor.ActionName.Equals("GetItemsByUserIdLegacy", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetSearchTerm(ActionExecutingContext ctx, out string searchTerm)
    {
        if (TryGetActionArgument(ctx, "searchTerm", out searchTerm) && !string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        if (TryGetActionArgument(ctx, "searchQuery", out searchTerm) && !string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        if (TryGetActionArgument(ctx, "query", out searchTerm) && !string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        if (TryGetActionArgument(ctx, "q", out searchTerm) && !string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        searchTerm = string.Empty;
        return false;
    }

    private static HashSet<BaseItemKind> GetRequestedItemTypes(ActionExecutingContext ctx)
    {
        var requested = new HashSet<BaseItemKind>([BaseItemKind.Movie, BaseItemKind.Series]);

        if (TryGetActionArgument(ctx, "includeItemTypes", out BaseItemKind[]? includeTypes)
            && includeTypes is { Length: > 0 })
        {
            requested = new HashSet<BaseItemKind>(includeTypes);
            requested.IntersectWith([BaseItemKind.Movie, BaseItemKind.Series]);
        }

        if (TryGetActionArgument(ctx, "excludeItemTypes", out BaseItemKind[]? excludeTypes)
            && excludeTypes is { Length: > 0 })
        {
            requested.ExceptWith(excludeTypes);
        }

        if (TryGetActionArgument(ctx, "mediaTypes", out MediaType[]? mediaTypes)
            && mediaTypes is { Length: > 0 }
            && mediaTypes.Contains(MediaType.Video))
        {
            requested.Remove(BaseItemKind.Series);
        }

        return requested;
    }

    private static BaseItemDto CreateDto(TmdbSearchItem item, BaseItemKind kind, string importPath)
    {
        var title = item.Title ?? item.Name ?? string.Empty;
        var year = TryParseYear(item.ReleaseDate ?? item.FirstAirDate);

        return new BaseItemDto
        {
            Id = Guid.NewGuid(),
            Name = title,
            Type = kind,
            Overview = item.Overview,
            Path = importPath,
            ProductionYear = year,
            ProviderIds = new Dictionary<string, string>
            {
                ["Tmdb"] = item.Id.ToString(CultureInfo.InvariantCulture),
            },
        };
    }

    private static int? TryParseYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed
        )
            ? parsed.Year
            : null;
    }

    private static bool TryGetActionArgument<T>(ActionExecutingContext ctx, string key, out T value)
    {
        if (ctx.ActionArguments.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    private static int GetIntArgument(ActionExecutingContext ctx, string key, int defaultValue)
    {
        return TryGetActionArgument(ctx, key, out int value) ? value : defaultValue;
    }

    private static string NormalizeSearchTerm(string searchTerm)
    {
        return searchTerm.Trim().ToLowerInvariant();
    }

    private static string BuildCacheKey(string searchTerm, HashSet<BaseItemKind> requestedTypes)
    {
        var typesKey = string.Join(",", requestedTypes.OrderBy(type => type).Select(type => type.ToString()));
        return $"tmdb-auto-import-search:{searchTerm}:{typesKey}";
    }
}