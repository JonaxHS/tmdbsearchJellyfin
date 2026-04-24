using System;
using MediaBrowser.Controller;
using Jellyfin.Plugin.TmdbAutoImport.Configuration;
using Jellyfin.Plugin.TmdbAutoImport.Services;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TmdbAutoImport;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost _)
    {
        serviceCollection.AddSingleton(_ => Plugin.Instance?.Configuration ?? new PluginConfiguration());
        serviceCollection.AddSingleton<Jellyfin.Plugin.TmdbAutoImport.Filters.ImportOnDemandActionFilter>();
        serviceCollection.AddSingleton<Jellyfin.Plugin.TmdbAutoImport.Filters.SearchActionFilter>();
        serviceCollection.AddSingleton<ImportService>();
        serviceCollection.AddHttpClient<TmdbClient>();

        serviceCollection.PostConfigure<MvcOptions>(options =>
        {
            options.Filters.AddService<Jellyfin.Plugin.TmdbAutoImport.Filters.ImportOnDemandActionFilter>(order: 0);
            options.Filters.AddService<Jellyfin.Plugin.TmdbAutoImport.Filters.SearchActionFilter>(order: 1);
        });
    }
}
