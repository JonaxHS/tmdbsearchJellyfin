using System;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TmdbAutoImport.Services;

namespace Jellyfin.Plugin.TmdbAutoImport.Filters;

internal sealed class TmdbVirtualItem
{
    public BaseItemKind Kind { get; init; }

    public TmdbSearchItem Item { get; init; } = new();
}

internal static class TmdbVirtualItemCache
{
    public static readonly TimeSpan VirtualItemTtl = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan SearchResultTtl = TimeSpan.FromMinutes(3);

    public static string BuildVirtualItemCacheKey(Guid id)
    {
        return $"tmdb-auto-import:virtual:{id:N}";
    }

    public static string BuildSearchResultCacheKey(string searchTerm, string typesKey)
    {
        return $"tmdb-auto-import:search:{searchTerm}:{typesKey}";
    }

    public static Guid BuildVirtualGuid(BaseItemKind kind, int tmdbId)
    {
        var bytes = Encoding.UTF8.GetBytes($"tmdb-auto-import:{kind}:{tmdbId}");
        var hash = MD5.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16));
    }
}