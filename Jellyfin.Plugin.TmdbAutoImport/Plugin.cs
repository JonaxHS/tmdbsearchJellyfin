using System;
using System.Collections.Generic;
using Jellyfin.Plugin.TmdbAutoImport.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.TmdbAutoImport;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "TMDb Auto Import";

    public override Guid Id => Guid.Parse("9d4fb3ad-76c7-4eb4-b17a-ec6fbc7ab637");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "tmdbAutoImportConfig",
                EmbeddedResourcePath = "Jellyfin.Plugin.TmdbAutoImport.Configuration.configPage.html"
            },
            new PluginPageInfo
            {
                Name = "tmdbAutoImportSearch",
                EmbeddedResourcePath = "Jellyfin.Plugin.TmdbAutoImport.Configuration.searchPage.html",
                EnableInMainMenu = true,
                MenuSection = "plugins"
            }
        };
    }
}
