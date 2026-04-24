using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TmdbAutoImport.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbAutoImport.Filters;

public sealed class SearchActionFilter(
    TmdbClient tmdbClient,
    IMemoryCache memoryCache,
    ILogger<SearchActionFilter> logger
) : IAsyncActionFilter, IOrderedFilter
{
    private const int MaxTmdbResultsPerType = 5;

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

        await next();

        if (!TryGetExistingQueryResult(ctx, out var existingResult))
        {
            return;
        }

        var localItems = existingResult.Items?.ToList() ?? new List<BaseItemDto>();
        var remoteItems = await GetRemoteDtosAsync(searchTerm, requestedTypes, ctx.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (remoteItems.Count == 0)
        {
            return;
        }

        var merged = new List<BaseItemDto>(localItems.Count + remoteItems.Count);
        merged.AddRange(localItems);
        merged.AddRange(remoteItems);

        existingResult.Items = merged.ToArray();
        existingResult.TotalRecordCount += remoteItems.Count;

        logger.LogInformation(
            "TMDb Auto Import merged search \"{Query}\" local={LocalCount} tmdb={TmdbCount}",
            searchTerm,
            localItems.Count,
            remoteItems.Count
        );

        ctx.Result = new OkObjectResult(existingResult);
    }

    private async Task<List<BaseItemDto>> GetRemoteDtosAsync(
        string searchTerm,
        HashSet<BaseItemKind> requestedTypes,
        System.Threading.CancellationToken cancellationToken)
    {
        var normalizedSearchTerm = NormalizeSearchTerm(searchTerm);
        var typesKey = string.Join(",", requestedTypes.OrderBy(type => type).Select(type => type.ToString()));
        var cacheKey = TmdbVirtualItemCache.BuildSearchResultCacheKey(normalizedSearchTerm, typesKey);

        if (memoryCache.TryGetValue(cacheKey, out List<BaseItemDto>? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var remoteDtos = new List<BaseItemDto>();

            foreach (var requestedType in requestedTypes)
            {
                var tmdbType = requestedType == BaseItemKind.Series ? "series" : "movie";
                var response = await tmdbClient
                    .SearchAsync(searchTerm, tmdbType, cancellationToken)
                    .ConfigureAwait(false);

                var topResults = response.Results.Take(MaxTmdbResultsPerType).ToList();
                if (topResults.Count == 0)
                {
                    continue;
                }

                foreach (var item in topResults)
                {
                    remoteDtos.Add(CreateDto(item, requestedType, memoryCache));
                }
            }

            memoryCache.Set(cacheKey, remoteDtos, TmdbVirtualItemCache.SearchResultTtl);
            return remoteDtos;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDb Auto Import search interception failed for {Query}", searchTerm);
            return new List<BaseItemDto>();
        }
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

    private static BaseItemDto CreateDto(TmdbSearchItem item, BaseItemKind kind, IMemoryCache memoryCache)
    {
        var virtualId = TmdbVirtualItemCache.BuildVirtualGuid(kind, item.Id);
        var virtualCacheKey = TmdbVirtualItemCache.BuildVirtualItemCacheKey(virtualId);

        memoryCache.Set(
            virtualCacheKey,
            new TmdbVirtualItem
            {
                Kind = kind,
                Item = item,
            },
            TmdbVirtualItemCache.VirtualItemTtl
        );

        var title = item.Title ?? item.Name ?? string.Empty;
        var year = TryParseYear(item.ReleaseDate ?? item.FirstAirDate);

        return new BaseItemDto
        {
            Id = virtualId,
            Name = title,
            Type = kind,
            Overview = item.Overview,
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

    private static bool TryGetExistingQueryResult(ActionExecutingContext ctx, out QueryResult<BaseItemDto> queryResult)
    {
        if (ctx.Result is OkObjectResult { Value: QueryResult<BaseItemDto> direct })
        {
            queryResult = direct;
            return true;
        }

        queryResult = new QueryResult<BaseItemDto>
        {
            Items = Array.Empty<BaseItemDto>(),
            TotalRecordCount = 0,
        };
        return false;
    }

    private static string NormalizeSearchTerm(string searchTerm)
    {
        return searchTerm.Trim().ToLowerInvariant();
    }

}