using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TmdbAutoImport.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbAutoImport.Filters;

public sealed class ImportOnDemandActionFilter(
    IMemoryCache memoryCache,
    ILibraryManager libraryManager,
    ImportService importService,
    ILogger<ImportOnDemandActionFilter> logger
) : IAsyncActionFilter, IOrderedFilter
{
    public int Order => 0;

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        if (!TryGetRouteGuid(ctx, out var routeId))
        {
            await next();
            return;
        }

        var cacheKey = TmdbVirtualItemCache.BuildVirtualItemCacheKey(routeId);
        if (!memoryCache.TryGetValue(cacheKey, out TmdbVirtualItem? virtualItem) || virtualItem is null)
        {
            await next();
            return;
        }

        try
        {
            if (virtualItem.Kind == BaseItemKind.Series)
            {
                await importService.ImportSeriesAsync(virtualItem.Item, ctx.HttpContext.RequestAborted)
                    .ConfigureAwait(false);
            }
            else
            {
                await importService.ImportMovieAsync(virtualItem.Item, ctx.HttpContext.RequestAborted)
                    .ConfigureAwait(false);
            }

            var imported = FindImportedItem(virtualItem.Kind, virtualItem.Item.Id);
            if (imported is not null)
            {
                ReplaceGuid(ctx, routeId, imported.Id);
                memoryCache.Remove(cacheKey);
                logger.LogInformation(
                    "Imported TMDb item on-demand tmdbId={TmdbId} type={Type} => jellyfinId={JellyfinId}",
                    virtualItem.Item.Id,
                    virtualItem.Kind,
                    imported.Id
                );
            }
            else
            {
                logger.LogInformation(
                    "Imported TMDb item on-demand tmdbId={TmdbId} type={Type}; waiting for library scan to index",
                    virtualItem.Item.Id,
                    virtualItem.Kind
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "On-demand import failed for virtual id {VirtualId}", routeId);
        }

        await next();
    }

    private BaseItem? FindImportedItem(BaseItemKind kind, int tmdbId)
    {
        var providerId = tmdbId.ToString(CultureInfo.InvariantCulture);

        var items = libraryManager.GetItemList(
            new InternalItemsQuery
            {
                IncludeItemTypes = new[] { kind },
                HasAnyProviderId = new Dictionary<string, string>
                {
                    ["Tmdb"] = providerId,
                },
                Recursive = true,
                Limit = 1,
            }
        );

        return items.FirstOrDefault();
    }

    private static bool TryGetRouteGuid(ActionExecutingContext ctx, out Guid value)
    {
        value = Guid.Empty;
        var keys = new[] { "id", "Id", "ID", "itemId", "ItemId", "ItemID" };

        foreach (var key in keys)
        {
            if (ctx.RouteData.Values.TryGetValue(key, out var raw)
                && Guid.TryParse(raw?.ToString(), out var parsed)
                && parsed != Guid.Empty)
            {
                value = parsed;
                return true;
            }
        }

        return false;
    }

    private static void ReplaceGuid(ActionExecutingContext ctx, Guid oldValue, Guid newValue)
    {
        var keys = new[] { "id", "Id", "ID", "itemId", "ItemId", "ItemID" };
        foreach (var key in keys)
        {
            if (ctx.RouteData.Values.ContainsKey(key))
            {
                ctx.RouteData.Values[key] = newValue.ToString("D", CultureInfo.InvariantCulture);
            }
        }

        var actionKeys = ctx.ActionArguments.Keys.ToArray();
        foreach (var key in actionKeys)
        {
            var arg = ctx.ActionArguments[key];
            if (arg is Guid guidArg && guidArg == oldValue)
            {
                ctx.ActionArguments[key] = newValue;
            }
            else if (arg is string stringArg && Guid.TryParse(stringArg, out var parsed) && parsed == oldValue)
            {
                ctx.ActionArguments[key] = newValue.ToString("D", CultureInfo.InvariantCulture);
            }
        }
    }
}