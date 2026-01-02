using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Data;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Downloaders
{
    /// <summary>
    /// Client for interacting with YouTube's internal API
    /// </summary>
    public class YouTubeClient
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        /// <summary>
        /// Logs a debug message only if debug logging is enabled in settings.
        /// </summary>
        private static void LogDebug(string message)
        {
            if (FileLogger.IsDebugLoggingEnabled)
            {
                LogDebug(message);
            }
        }

        private readonly HttpClient _httpClient;
        private readonly ErrorHandlerService _errorHandler;

        // JSON path selectors for YouTube API responses
        private const string ParserPlaylists = "..lockupViewModel";
        private const string ParserPlaylistName = "metadata.lockupMetadataViewModel.title.content";
        private const string ParserPlaylistId = "contentId";
        private const string ParserPlaylistThumbnail = "contentImage..image.sources[0].url";
        private const string ParserPlaylistCount = "contentImage..overlays[?(@..imageName=='PLAYLISTS')]..text";
        private const string ParserChannelId = "metadata.lockupMetadataViewModel.metadata.contentMetadataViewModel.metadataRows[0].metadataParts[0].text.commandRuns[0].onTap.innertubeCommand.browseEndpoint.browseId";
        private const string ParserChannelName = "metadata.lockupMetadataViewModel.metadata.contentMetadataViewModel.metadataRows[0].metadataParts[0].text.content";
        private const string ParserContinuationToken = "..continuationCommand.token";
        private const string ParserVisitorData = "..visitorData";
        private const string ParserPlaylistVideos = "..playlistPanelVideoRenderer";
        private const string SearchTypePlaylist = "EgIQAw%3D%3D";

        public YouTubeClient(HttpClient httpClient, ErrorHandlerService errorHandler = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _errorHandler = errorHandler;
        }

        /// <summary>
        /// Searches YouTube for playlists matching the query
        /// </summary>
        public List<YouTubeItem> Search(string searchQuery, int maxResults = 20, CancellationToken cancellationToken = default)
        {
            var results = new List<YouTubeItem>();
            string continuationToken = null;

            try
            {
                do
                {
                    var response = GetSearchResponseAsync(searchQuery, SearchTypePlaylist, continuationToken, cancellationToken)
                        .GetAwaiter().GetResult();

                    if (string.IsNullOrWhiteSpace(response))
                        break;

                    continuationToken = ParseSearchResults(response, results);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                } while (continuationToken != null && results.Count < maxResults);
            }
            catch (OperationCanceledException)
            {
                LogDebug("YouTube search cancelled");
                throw; // Re-throw cancellation
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "searching YouTube",
                    showUserMessage: false
                );
                // Re-throw to let caller handle (they can show error to user)
                throw;
            }

            return results;
        }

        /// <summary>
        /// Gets all videos from a YouTube playlist
        /// </summary>
        public List<YouTubeItem> GetPlaylist(string playlistId, CancellationToken cancellationToken = default)
        {
            var results = new List<YouTubeItem>();
            var encounteredIds = new HashSet<string>();
            string lastVideoId = null;
            int lastVideoIndex = 0;
            string visitorData = null;

            try
            {
                do
                {
                    var response = GetPlaylistResponseAsync(playlistId, lastVideoId, lastVideoIndex, visitorData, cancellationToken)
                        .GetAwaiter().GetResult();

                    if (string.IsNullOrWhiteSpace(response))
                        break;

                    var newItems = ParsePlaylistVideos(response, results, encounteredIds, ref lastVideoId, ref lastVideoIndex, ref visitorData);

                    if (cancellationToken.IsCancellationRequested || newItems == 0)
                        break;

                } while (true);
            }
            catch (OperationCanceledException)
            {
                LogDebug("YouTube playlist loading cancelled");
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "loading YouTube playlist",
                    showUserMessage: false
                );
            }

            return results;
        }

        private async Task<string> GetSearchResponseAsync(string searchQuery, string searchFilter, string continuationToken, CancellationToken cancellationToken)
        {
            try
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://www.youtube.com/youtubei/v1/search"
                );

                var content = continuationToken == null
                    ? $@"{{ ""query"": ""{WebUtility.UrlEncode(searchQuery)}"", ""params"": ""{searchFilter}"", ""context"": {{ ""client"": {{ ""clientName"": ""WEB"", ""clientVersion"": ""2.20210408.08.00"", ""hl"": ""en"", ""gl"": ""US"", ""utcOffsetMinutes"": 0 }} }} }}"
                    : $@"{{ ""continuation"": ""{continuationToken}"", ""context"": {{ ""client"": {{ ""clientName"": ""WEB"", ""clientVersion"": ""2.20210408.08.00"", ""hl"": ""en"", ""gl"": ""US"", ""utcOffsetMinutes"": 0 }} }} }}";

                request.Content = new StringContent(content);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                LogDebug($"YouTube search request: Query='{searchQuery}', Filter='{searchFilter}', Continuation={continuationToken != null}");
                
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Logger.Error($"YouTube API returned error: {response.StatusCode} - {errorContent}");
                    throw new HttpRequestException($"YouTube API error: {response.StatusCode}");
                }
                
                var responseContent = await response.Content.ReadAsStringAsync();
                LogDebug($"YouTube search response received: {responseContent.Length} characters");
                return responseContent;
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "getting YouTube search response",
                    showUserMessage: false
                );
                throw;
            }
        }

        private async Task<string> GetPlaylistResponseAsync(string playlistId, string videoId, int index, string visitorData, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://www.youtube.com/youtubei/v1/next"
            );

            var content = $@"{{ ""playlistId"": ""{playlistId}"", ""videoId"": ""{videoId ?? ""}"", ""playlistIndex"": {index}, ""context"": {{ ""client"": {{ ""clientName"": ""WEB"", ""clientVersion"": ""2.20210408.08.00"", ""hl"": ""en"", ""gl"": ""US"", ""utcOffsetMinutes"": 0, ""visitorData"": ""{visitorData ?? ""}"" }} }} }}";

            request.Content = new StringContent(content);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        private string ParseSearchResults(string json, List<YouTubeItem> results)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    Logger.Warn("ParseSearchResults: JSON response is empty");
                    return null;
                }

                dynamic jsonObj = Serialization.FromJson<dynamic>(json);
                var playlists = new List<dynamic>(jsonObj.SelectTokens(ParserPlaylists));
                
                LogDebug($"ParseSearchResults: Found {playlists.Count} playlists in JSON response");

                foreach (var playlist in playlists)
                {
                    try
                    {
                        var item = new YouTubeItem
                        {
                            Id = playlist.SelectToken(ParserPlaylistId)?.ToString(),
                            Title = playlist.SelectToken(ParserPlaylistName)?.ToString(),
                            ThumbnailUrl = new Uri(playlist.SelectToken(ParserPlaylistThumbnail)?.ToString() ?? "https://via.placeholder.com/120"),
                            Count = ParseCount(playlist.SelectToken(ParserPlaylistCount)?.ToString()),
                            ChannelId = playlist.SelectToken(ParserChannelId)?.ToString(),
                            ChannelName = playlist.SelectToken(ParserChannelName)?.ToString()
                        };

                        if (!string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Title))
                        {
                            results.Add(item);
                            LogDebug($"Added playlist: {item.Title} (ID: {item.Id}, Count: {item.Count}, Channel: {item.ChannelName})");
                        }
                        else
                        {
                            LogDebug($"Skipped playlist - missing ID or Title. ID: {item.Id}, Title: {item.Title}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _errorHandler?.HandleError(
                            ex,
                            context: "parsing individual playlist",
                            showUserMessage: false
                        );
                        // Continue with next playlist
                    }
                }

                // Use SelectTokens (plural) because the deep scan ".." operator may match multiple tokens
                // SelectToken (singular) throws "Path returned multiple tokens" when this happens
                var continuationTokens = new List<dynamic>(jsonObj.SelectTokens(ParserContinuationToken));
                var continuationToken = continuationTokens.FirstOrDefault()?.ToString();
                LogDebug($"ParseSearchResults: Parsed {results.Count} playlists, continuation token: {continuationToken != null}");
                return continuationToken;
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "parsing YouTube search results",
                    showUserMessage: false
                );
                return null;
            }
        }

        private int ParsePlaylistVideos(string json, List<YouTubeItem> results, HashSet<string> encounteredIds, 
            ref string lastVideoId, ref int lastVideoIndex, ref string visitorData)
        {
            try
            {
                dynamic jsonObj = Serialization.FromJson<dynamic>(json);
                // Use SelectTokens (plural) because the deep scan ".." operator may match multiple tokens
                var visitorDataTokens = new List<dynamic>(jsonObj.SelectTokens(ParserVisitorData));
                visitorData = visitorDataTokens.FirstOrDefault()?.ToString();
                var videos = new List<dynamic>(jsonObj.SelectTokens(ParserPlaylistVideos));

                int newItems = 0;
                foreach (var video in videos)
                {
                    var item = new YouTubeItem
                    {
                        Id = video.SelectToken("videoId")?.ToString(),
                        Title = video.SelectToken("title.simpleText")?.ToString(),
                        ThumbnailUrl = new Uri(video.SelectToken("thumbnail.thumbnails[0].url")?.ToString() ?? "https://via.placeholder.com/120"),
                        Index = (int)(video.SelectToken("navigationEndpoint.watchEndpoint.index") ?? 0)
                    };

                    // Parse duration
                    var durationText = video.SelectToken("lengthText.simpleText")?.ToString();
                    if (!string.IsNullOrWhiteSpace(durationText))
                    {
                        item.Duration = ParseDuration(durationText);
                    }

                    if (string.IsNullOrWhiteSpace(item.Id) || !encounteredIds.Add(item.Id))
                        continue;

                    newItems++;
                    results.Add(item);
                    lastVideoId = item.Id;
                    lastVideoIndex = item.Index;
                }

                return newItems;
            }
            catch (Exception ex)
            {
                _errorHandler?.HandleError(
                    ex,
                    context: "parsing YouTube playlist videos",
                    showUserMessage: false
                );
                return 0;
            }
        }

        private uint ParseCount(string countText)
        {
            if (string.IsNullOrWhiteSpace(countText))
                return 0;

            var match = Regex.Match(countText, @"\d+");
            return match.Success && uint.TryParse(match.Value, out var count) ? count : 0;
        }

        private System.TimeSpan ParseDuration(string durationText)
        {
            var formats = new[] { @"m\:s", @"h\:m\:s", @"mm\:ss", @"hh\:mm\:ss" };
            foreach (var format in formats)
            {
                if (System.TimeSpan.TryParseExact(durationText, format, null, out var result))
                {
                    return result;
                }
            }
            return System.TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Represents a YouTube item (playlist or video)
    /// </summary>
    public class YouTubeItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public Uri ThumbnailUrl { get; set; }
        public System.TimeSpan Duration { get; set; }
        public int Index { get; set; }
        public uint Count { get; set; }

        /// <summary>
        /// YouTube channel ID (for playlists)
        /// </summary>
        public string ChannelId { get; set; }

        /// <summary>
        /// YouTube channel name (for playlists)
        /// </summary>
        public string ChannelName { get; set; }
    }
}

