using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.IO;
using SKYPE4COMLib;
using System.Collections;
using System.Xml;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Google.Apis.Services;
using SkypeBot.plugins.config.youtube;
using System.Threading;
using log4net;


namespace SkypeBot.plugins {
    public class YouTubePlugin : Plugin {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private Random random;
        private YouTubeService youtubeService;
        private Queue<String> randomCache;

        public String name() { return "YouTube Plugin"; }

        public String help() { return "!youtube [query]"; }

        public String description() { return "Gives title and rating information on posted YouTube links.\n" +
                                             "Also lets people search for YouTube videos.\n" +
                                             "Also gives random YouTube links."; }

        public bool canConfig() { return true; }
        public void openConfig() {
            YoutubePluginConfigForm ycf = new YoutubePluginConfigForm();
            ycf.Visible = true;
        }

        public YouTubePlugin() {
            random = new Random();
            youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = "REPLACE_ME",
                ApplicationName = "Dynamic Skype Bot"
            });
            randomCache = new Queue<string>();
        }

        public void load() {
            log.Info("Plugin successfully loaded.");
            if (randomCache.Count < PluginSettings.Default.YoutubeCacheSize) {
                generateRandomVideos(false);
            }
        }

        public void unload() {
            log.Info("Plugin successfully unloaded.");
        }

        public void Skype_MessageStatus(IChatMessage message, TChatMessageStatus status) {
            Match output = Regex.Match(message.Body, @"(?:youtube\.\w{2,3}\S+v=|youtu\.be/)([\w-]+)", RegexOptions.IgnoreCase);
            // Use non-breaking space as a marker for when to not show info.
            if (output.Success && !message.Body.Contains(" ")) {
                String youtubeId = output.Groups[1].Value;
                log.Info("Sending request to YouTube...");

                VideosResource.ListRequest request = youtubeService.Videos.List("snippet,contentDetails,statistics");
                request.Id = youtubeId;

                VideoListResponse response = request.Execute();
                Video vid = response.Items[0];
                String title = vid.Snippet.Title;
                String user = vid.Snippet.ChannelTitle;
                String rating = vid.Statistics.LikeCount + "👍 " + vid.Statistics.DislikeCount + "👎";
                TimeSpan duration = XmlConvert.ToTimeSpan(vid.ContentDetails.Duration);

                message.Chat.SendMessage(String.Format(@"YouTube: ""{0}"" (uploaded by: {1}) (duration: {2}) (rating: {3})", title, user, duration, rating));
                return;
            }
            
            output = Regex.Match(message.Body, @"^!youtube (.+)", RegexOptions.IgnoreCase);
            if (output.Success) {
                String query = output.Groups[1].Value;

                SearchResource.ListRequest request = youtubeService.Search.List("snippet");
                request.Q = query;
                request.Type = "video";
                request.SafeSearch = SearchResource.ListRequest.SafeSearchEnum.None;
                request.MaxResults = 10;

                SearchListResponse response = request.Execute();
                int count = response.Items.Count;

                string url;
                if (count > 0) {
                    SearchResult result = response.Items[random.Next(count)];
                    url = "https://youtu.be/" + result.Id.VideoId;
                } else {
                    url = "No matches found.";
                }

                message.Chat.SendMessage(String.Format(@"YouTube search for ""{0}"": {1}", query, url));
                return;
            }

            output = Regex.Match(message.Body, @"^!youtube", RegexOptions.IgnoreCase);
            if (output.Success) {
                log.Debug("Got a request for a random video.");

                String url = randomCache.Count > 0 ? randomCache.Dequeue() : generateRandomVideos(true);

                message.Chat.SendMessage(String.Format(@"Random YouTube video: {0}", url));

                generateRandomVideos(false);
                return;
            }
        }

        public String generateRandomVideos(bool onlyOne) {
            if (onlyOne) {
                log.Debug("Cache is empty; generating video...");
            } else {
                log.Debug(String.Format("Cache currently contains {0} items; refilling to {1}...", randomCache.Count, PluginSettings.Default.YoutubeCacheSize));
            }
            while (randomCache.Count < PluginSettings.Default.YoutubeCacheSize) {
                try {
                    log.Debug("Generating a random video...");
                    VideosResource.ListRequest request = youtubeService.Videos.List("snippet");
                    request.Chart = VideosResource.ListRequest.ChartEnum.MostPopular;
                    request.MaxResults = 40;

                    log.Debug("Fetching list of most popular videos...");
                    
                    VideoListResponse response = request.Execute();
                    int count = response.Items.Count;

                    Video first = response.Items[random.Next(count)];
                    String id = first.Id;
                    log.Debug("Picked \"" + first.Snippet.Title + "\" as my starting point.");
                    for (int i = 0; i < PluginSettings.Default.YoutubeIterations; i++) {
                        SearchResource.ListRequest relatedRequest = youtubeService.Search.List("snippet");
                        relatedRequest.RelatedToVideoId = id;
                        relatedRequest.Type = "video";
                        relatedRequest.SafeSearch = SearchResource.ListRequest.SafeSearchEnum.None;
                        relatedRequest.MaxResults = 20;

                        SearchListResponse relatedResponse = relatedRequest.Execute();
                        count = relatedResponse.Items.Count;
                        SearchResult result = relatedResponse.Items[random.Next(count)];
                        id = result.Id.VideoId;
                        log.Debug("Next link: " + result.Snippet.Title);
                    }

                    log.Debug("Found my random video!");
                    String url = "https://youtu.be/" + id;

                    if (onlyOne) {
                        return url;
                    }

                    log.Debug("Adding to cache...");
                    randomCache.Enqueue(url);
                } catch (Exception e) {
                    log.Error("Failed in generating a video.", e);
                }
            }

            return null;
        }
    }
}
