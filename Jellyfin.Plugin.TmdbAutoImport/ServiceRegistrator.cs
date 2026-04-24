using System;
using MediaBrowser.Controller;
using Jellyfin.Plugin.TmdbAutoImport.Services;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TmdbAutoImport;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost _)
    {
        var config = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Plugin instance is not initialized.");

        serviceCollection.AddSingleton(config);
        serviceCollection.AddSingleton<ImportService>();
        serviceCollection.AddHttpClient<TmdbClient>();
    }
}
