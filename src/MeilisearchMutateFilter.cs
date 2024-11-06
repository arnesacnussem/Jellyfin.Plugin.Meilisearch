using System.Collections.ObjectModel;
using System.Globalization;
using Jellyfin.Data.Enums;
using Meilisearch;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Jellyfin.Plugin.Meilisearch;

// ReSharper disable once ClassNeverInstantiated.Global
public class MeilisearchMutateFilter(MeilisearchClientHolder ch, ILogger<MeilisearchMutateFilter> logger)
    : IActionFilter
{
    private static readonly Dictionary<string, string> JellyfinTypeMap = new()
    {
        { "Movie", "MediaBrowser.Controller.Entities.Movies.Movie" },
        { "Episode", "MediaBrowser.Controller.Entities.TV.Episode" },
        { "Series", "MediaBrowser.Controller.Entities.TV.Series" },
        { "Playlist", "MediaBrowser.Controller.Playlists.Playlist" },
        { "MusicAlbum", "MediaBrowser.Controller.Entities.Audio.MusicAlbum" },
        { "MusicArtist", "MediaBrowser.Controller.Entities.Audio.MusicArtist" },
        { "Audio", "MediaBrowser.Controller.Entities.Audio.Audio" },
        { "Video", "MediaBrowser.Controller.Entities.Video" },
        { "TvChannel", "MediaBrowser.Controller.LiveTv.LiveTvChannel" },
        { "LiveTvProgram", "MediaBrowser.Controller.LiveTv.LiveTvProgram" },
        { "PhotoAlbum", "MediaBrowser.Controller.Entities.PhotoAlbum" },
        { "Photo", "MediaBrowser.Controller.Entities.Photo" },
        { "Person", "MediaBrowser.Controller.Entities.Person" },
        { "Book", "MediaBrowser.Controller.Entities.Book" },
        { "AudioBook", "MediaBrowser.Controller.Entities.AudioBook" },
        { "BoxSet", "MediaBrowser.Controller.Entities.Movies.BoxSet" }
    };

    private static readonly Collection<string> MatchingPaths = ["/Items"];
    // ["/Users/{userId}/Items", "/Persons", "/Artists/AlbumArtists", "/Artists", "/Genres"];

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var path = context.HttpContext.Request.Path.ToString();
        logger.LogDebug("path={path} query={query}", path, context.HttpContext.Request.QueryString);

        if (!MatchingPaths.Contains(context.HttpContext.Request.Path)) return;
        if (!context.ActionArguments.TryGetValue("searchTerm", out var searchTermObj)) return;
        var searchTerm = (string?)searchTermObj;
        if (searchTerm is not { Length: > 0 }) return;

        logger.LogDebug("path={path} searchTerm={searchTerm}", path, searchTerm);
        Mutate(context, searchTerm).Wait();
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }


    /// <summary>
    ///     Mutates the current search request context by overriding the ids with the results of the Meilisearch query.
    ///     This part code is somewhat copied or adapted from Jellysearch.
    /// </summary>
    /// <param name="context">The action context.</param>
    /// <param name="searchTerm">The search term.</param>
    /// <remarks>
    ///     If the search term is empty, or if there are no results, the method does nothing.
    /// </remarks>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task Mutate(ActionExecutingContext context, string searchTerm)
    {
        var includeItemTypes = (BaseItemKind[]?)context.ActionArguments["includeItemTypes"] ?? [];

        var filteredTypes = new List<string>();
        var additionalFilters = new List<string>();
        if (!includeItemTypes.IsNullOrEmpty())
        {
            // Get item type(s) from URL
            var itemTypes = includeItemTypes.Select(x => JellyfinTypeMap[x.ToString()]).ToList();
            filteredTypes.AddRange(itemTypes);
        }
        else
        {
            var path = context.HttpContext.Request.Path.ToString();
            // Handle direct endpoints and their types
            if (path.EndsWith("/Persons", true, CultureInfo.InvariantCulture))
            {
                filteredTypes.Add("MediaBrowser.Controller.Entities.Person");
            }
            else if (path.EndsWith("/Artists", true, CultureInfo.InvariantCulture))
            {
                filteredTypes.Add("MediaBrowser.Controller.Entities.Audio.MusicArtist");
            }
            else if (path.EndsWith("/AlbumArtists", true, CultureInfo.InvariantCulture))
            {
                filteredTypes.Add("MediaBrowser.Controller.Entities.Audio.MusicArtist");
                additionalFilters.Add("isFolder = 1"); // Album artists are marked as folder
            }
            else if (path.EndsWith("/Genres", true, CultureInfo.InvariantCulture))
            {
                filteredTypes.Add("MediaBrowser.Controller.Entities.Genre"); // TODO: Handle genre search properly
            }
        }

        // Override the limit if it is less than 20 from request
        var limit = (int?)context.ActionArguments["limit"] ?? 20;
        var items = new List<MeilisearchItem>();
        if (ch.Index == null)
            return;
        if (filteredTypes.Count == 0)
        {
            // Search without filtering the type
            var results = await ch.Index.SearchAsync<MeilisearchItem>(searchTerm, new SearchQuery
            {
                Limit = limit
            });

            items.AddRange(results.Hits);
        }
        else
        {
            var additionFilter = additionalFilters.Count > 0 ? " AND " + string.Join(" AND ", additionalFilters) : "";
            // Loop through each requested type and search
            foreach (var filter in filteredTypes.Select(f => $"type = {f}{additionFilter}"))
            {
                var results = await ch.Index.SearchAsync<MeilisearchItem>(searchTerm, new SearchQuery
                {
                    Filter = filter,
                    Limit = limit
                });

                items.AddRange(results.Hits);
            }
        }

        if (items.Count == 0)
        {
            logger.LogDebug("No hints, not mutate request");
        }
        else
        {
            logger.LogInformation("Mutating search request with {hits} results", items.Count);
            // Get all query arguments to pass along to Jellyfin
            // Remove searchterm since we already searched
            // Remove sortby and sortorder since we want to display results as Meilisearch returns them
            // Remove limit since we are requesting by specific IDs and don't want Jellyfin to remove some of them
            context.ActionArguments["searchTerm"] = null;
            context.ActionArguments["sortBy"] = (ItemSortBy[]) [];
            context.ActionArguments["sortOrder"] = (SortOrder[]) [];
            context.ActionArguments["limit"] = limit < 20 ? 20 : limit;
            context.ActionArguments["ids"] = items.Select(x => Guid.Parse(x.Guid)).ToArray();
        }
    }
}