using System.Globalization;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Meilisearch;

// ReSharper disable once ClassNeverInstantiated.Global
public class Plugin : BasePlugin<Config>, IHasWebPages
{
    private readonly MeilisearchClientHolder _clientHolder;
    public readonly Indexer Indexer;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger,
        IServiceProvider serviceProvider,
        MeilisearchClientHolder clientHolder, Indexer indexer, IActionDescriptorCollectionProvider provider) : base(
        applicationPaths,
        xmlSerializer)
    {
        _clientHolder = clientHolder;
        Indexer = indexer;

        DbPath = Path.Combine(applicationPaths.DataPath, "library.db");
        logger.LogInformation("db_path={DB}", DbPath);
        Instance = this;

        ReloadMeilisearch += (_, _) =>
        {
            logger.LogInformation("Configuration changed, reloading meilisearch...");
            TryCreateMeilisearchClient().Wait();
        };
        TryCreateMeilisearchClient().Wait();
        TryAddFilter(provider, serviceProvider);
    }

    private EventHandler<BasePluginConfiguration> ReloadMeilisearch { get; }

    public string DbPath { get; }

    public override string Name => "Meilisearch";
    public override Guid Id => Guid.Parse("974395db-b31d-46a2-bc86-ef9aa5ac04dd");
    public static Plugin? Instance { get; private set; }


    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.config.html",
                    GetType().Namespace)
            }
        ];
    }

    private void TryAddFilter(IActionDescriptorCollectionProvider provider, IServiceProvider serviceProvider)
    {
        provider.AddDynamicFilter<MeilisearchMutateFilter>(serviceProvider, t =>
        {
            var sig = $"{t.ControllerTypeInfo.FullName}#{t.MethodInfo.Name}";
            if (Configuration.Debug) Console.WriteLine($"\tmethod: {sig}");

            return sig == "Jellyfin.Api.Controllers.ItemsController#GetItems";
        });
    }

    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var config = (Config)configuration;
        var skipReload = Configuration.Url == config.Url && Configuration.ApiKey == config.ApiKey;

        Configuration = config;
        SaveConfiguration(Configuration);
        ConfigurationChanged?.Invoke(this, configuration);
        if (!skipReload)
            ReloadMeilisearch?.Invoke(this, configuration);
    }

    public async Task TryCreateMeilisearchClient()
    {
        await _clientHolder.Set(Configuration);
        await Indexer.Index();
    }
}