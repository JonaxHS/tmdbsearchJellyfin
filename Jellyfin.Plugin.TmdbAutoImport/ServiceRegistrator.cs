using System;
using MediaBrowser.Controller;
using Jellyfin.Plugin.TmdbAutoImport.Configuration;
using Jellyfin.Plugin.TmdbAutoImport.Services;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TmdbAutoImport;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost _)
    {
        serviceCollection.AddSingleton(_ => Plugin.Instance?.Configuration ?? new PluginConfiguration());
        serviceCollection.AddSingleton<ImportService>();
        serviceCollection.AddHttpClient<TmdbClient>();
    }
}
