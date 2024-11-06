using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Meilisearch;

public static class InjectActionFilter
{
    public static void AddDynamicFilter<T>(
        this IActionDescriptorCollectionProvider provider,
        IServiceProvider serviceProvider,
        Func<ControllerActionDescriptor, bool> matcher)
        where T : IFilterMetadata
    {
        // Access the Action Descriptor Collection Provider to modify filter metadata
        var actionDescriptors = provider.ActionDescriptors.Items;

        // Find actions on the specified controller type
        var targetActions = actionDescriptors.Where(ad =>
        {
            var cad = ad as ControllerActionDescriptor;
            return cad != null && matcher(cad);
        });

        // Add the filter to each action on the specified controller
        foreach (var action in targetActions)
        {
            var filter = ActivatorUtilities.CreateInstance<T>(serviceProvider);

            var filterMetadata = action.FilterDescriptors;
            filterMetadata.Add(new FilterDescriptor(filter, FilterScope.Global));
        }
    }
}