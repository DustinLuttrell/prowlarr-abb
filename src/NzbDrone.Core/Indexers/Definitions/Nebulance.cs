using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Indexers.Settings;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Definitions
{
    public class Nebulance : TorrentIndexerBase<NebulanceSettings>
    {
        public override string Name => "Nebulance";
        public override string[] IndexerUrls => new[] { "https://nebulance.io/" };
        public override string Description => "Nebulance (NBL) is a ratioless Private Torrent Tracker for TV";
        public override string Language => "en-US";
        public override Encoding Encoding => Encoding.UTF8;
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;
        public override bool SupportsRedirect => true;
        public override bool SupportsPagination => true;
        public override IndexerCapabilities Capabilities => SetCapabilities();

        public Nebulance(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new NebulanceRequestGenerator(Settings);
        }

        public override IParseIndexerResponse GetParser()
        {
            return new NebulanceParser(Settings);
        }

        protected override bool CheckIfLoginNeeded(HttpResponse httpResponse)
        {
            return false;
        }

        protected override Task<HttpRequest> GetDownloadRequest(Uri link)
        {
            // Avoid using cookies to prevent redirects to login page
            var requestBuilder = new HttpRequestBuilder(link.AbsoluteUri)
            {
                AllowAutoRedirect = FollowRedirect
            };

            var request = requestBuilder.Build();

            return Task.FromResult(request);
        }

        protected override IList<ReleaseInfo> CleanupReleases(IEnumerable<ReleaseInfo> releases, SearchCriteriaBase searchCriteria)
        {
            var cleanReleases = base.CleanupReleases(releases, searchCriteria);

            return FilterReleasesByQuery(cleanReleases, searchCriteria).ToList();
        }

        protected override IEnumerable<ReleaseInfo> FilterReleasesByQuery(IEnumerable<ReleaseInfo> releases, SearchCriteriaBase searchCriteria)
        {
            if (!searchCriteria.IsRssSearch &&
                searchCriteria.IsIdSearch &&
                searchCriteria is TvSearchCriteria tvSearchCriteria &&
                tvSearchCriteria.EpisodeSearchString.IsNotNullOrWhiteSpace())
            {
                releases = releases.Where(r => r.Title.IsNotNullOrWhiteSpace() && r.Title.ContainsIgnoreCase(tvSearchCriteria.EpisodeSearchString)).ToList();
            }

            return releases;
        }

        private IndexerCapabilities SetCapabilities()
        {
            var caps = new IndexerCapabilities
            {
                TvSearchParams = new List<TvSearchParam>
                {
                    TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep, TvSearchParam.ImdbId, TvSearchParam.TvMazeId
                },
                SupportsRawSearch = true
            };

            caps.Categories.AddCategoryMapping(1, NewznabStandardCategory.TV);
            caps.Categories.AddCategoryMapping(2, NewznabStandardCategory.TVSD);
            caps.Categories.AddCategoryMapping(3, NewznabStandardCategory.TVHD);
            caps.Categories.AddCategoryMapping(4, NewznabStandardCategory.TVUHD);

            return caps;
        }
    }

    public class NebulanceRequestGenerator : IIndexerRequestGenerator
    {
        private readonly NebulanceSettings _settings;

        public NebulanceRequestGenerator(NebulanceSettings settings)
        {
            _settings = settings;
        }

        public IndexerPageableRequestChain GetSearchRequests(MovieSearchCriteria searchCriteria)
        {
            return new IndexerPageableRequestChain();
        }

        public IndexerPageableRequestChain GetSearchRequests(MusicSearchCriteria searchCriteria)
        {
            return new IndexerPageableRequestChain();
        }

        public IndexerPageableRequestChain GetSearchRequests(TvSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var queryParams = new NebulanceQuery
            {
                Age = ">0"
            };

            if (searchCriteria.SanitizedTvSearchString.IsNotNullOrWhiteSpace())
            {
                queryParams.Name = "%" + Regex.Replace(searchCriteria.SanitizedTvSearchString, "[\\W]+", "%").Trim() + "%";
            }

            if (searchCriteria.TvMazeId.HasValue)
            {
                queryParams.Tvmaze = searchCriteria.TvMazeId.Value;

                if (searchCriteria.EpisodeSearchString.IsNotNullOrWhiteSpace())
                {
                    queryParams.Name = "%" + Regex.Replace(searchCriteria.EpisodeSearchString, "[\\W]+", "%").Trim() + "%";
                }
            }
            else if (searchCriteria.ImdbId.IsNotNullOrWhiteSpace() && int.TryParse(searchCriteria.ImdbId, out var intImdb))
            {
                queryParams.Imdb = intImdb;

                if (searchCriteria.EpisodeSearchString.IsNotNullOrWhiteSpace())
                {
                    queryParams.Name = "%" + Regex.Replace(searchCriteria.EpisodeSearchString, "[\\W]+", "%").Trim() + "%";
                }
            }

            pageableRequests.Add(GetPagedRequests(queryParams, searchCriteria.Limit, searchCriteria.Offset));

            if (queryParams.Name.IsNotNullOrWhiteSpace() && (queryParams.Tvmaze is > 0 || queryParams.Imdb is > 0))
            {
                queryParams = queryParams.Clone();
                queryParams.Name = null;

                pageableRequests.Add(GetPagedRequests(queryParams, searchCriteria.Limit, searchCriteria.Offset));
            }

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            return new IndexerPageableRequestChain();
        }

        public IndexerPageableRequestChain GetSearchRequests(BasicSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var queryParams = new NebulanceQuery
            {
                Age = ">0"
            };

            if (searchCriteria.SanitizedSearchTerm.IsNotNullOrWhiteSpace())
            {
                queryParams.Name = "%" + Regex.Replace(searchCriteria.SanitizedSearchTerm, "[\\W]+", "%").Trim() + "%";
            }

            pageableRequests.Add(GetPagedRequests(queryParams, searchCriteria.Limit, searchCriteria.Offset));

            return pageableRequests;
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(NebulanceQuery parameters, int? results, int? offset)
        {
            var apiUrl = _settings.BaseUrl + "api.php";

            var builder = new JsonRpcRequestBuilder(apiUrl)
                .Call("getTorrents", _settings.ApiKey, parameters, results ?? 100, offset ?? 0);

            builder.SuppressHttpError = true;

            yield return new IndexerRequest(builder.Build());
        }

        public Func<IDictionary<string, string>> GetCookies { get; set; }
        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class NebulanceParser : IParseIndexerResponse
    {
        private readonly NebulanceSettings _settings;

        public NebulanceParser(NebulanceSettings settings)
        {
            _settings = settings;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<ReleaseInfo>();

            if (indexerResponse.HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new IndexerException(indexerResponse, "Unexpected response status '{0}' code from indexer request", indexerResponse.HttpResponse.StatusCode);
            }

            JsonRpcResponse<NebulanceTorrents> jsonResponse;

            try
            {
                jsonResponse = STJson.Deserialize<JsonRpcResponse<NebulanceTorrents>>(indexerResponse.HttpResponse.Content);
            }
            catch (Exception ex)
            {
                STJson.TryDeserialize<JsonRpcResponse<string>>(indexerResponse.HttpResponse.Content, out var response);

                throw new IndexerException(indexerResponse, "Unexpected response from indexer request: {0}", ex, response?.Result ?? ex.Message);
            }

            if (jsonResponse.Error != null || jsonResponse.Result == null)
            {
                throw new IndexerException(indexerResponse, "Indexer API call returned an error [{0}]", jsonResponse.Error);
            }

            if (jsonResponse.Result.Items.Count == 0)
            {
                return torrentInfos;
            }

            var rows = jsonResponse.Result.Items;

            foreach (var row in rows)
            {
                var details = _settings.BaseUrl + "torrents.php?id=" + row.TorrentId;

                var title = row.ReleaseTitle.IsNotNullOrWhiteSpace() ? row.ReleaseTitle : row.GroupName;

                var release = new TorrentInfo
                {
                    Title = title,
                    Guid = details,
                    InfoUrl = details,
                    PosterUrl = row.Banner,
                    DownloadUrl = row.Download,
                    Categories = new List<IndexerCategory> { TvCategoryFromQualityParser.ParseTvShowQuality(row.ReleaseTitle) },
                    Size = ParseUtil.CoerceLong(row.Size),
                    Files = row.FileList.Length,
                    PublishDate = DateTime.Parse(row.PublishDateUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                    Grabs = ParseUtil.CoerceInt(row.Snatch),
                    Seeders = ParseUtil.CoerceInt(row.Seed),
                    Peers = ParseUtil.CoerceInt(row.Seed) + ParseUtil.CoerceInt(row.Leech),
                    Scene = row.Tags?.ContainsIgnoreCase("scene"),
                    MinimumRatio = 0, // ratioless
                    MinimumSeedTime = row.Category.ToLower() == "season" ? 432000 : 86400, // 120 hours for seasons and 24 hours for episodes
                    DownloadVolumeFactor = 0, // ratioless tracker
                    UploadVolumeFactor = 1
                };

                if (row.TvMazeId.IsNotNullOrWhiteSpace())
                {
                    release.TvMazeId = ParseUtil.CoerceInt(row.TvMazeId);
                }

                torrentInfos.Add(release);
            }

            return torrentInfos.ToArray();
        }

        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class NebulanceSettings : NoAuthTorrentBaseSettings
    {
        public NebulanceSettings()
        {
            ApiKey = "";
        }

        [FieldDefinition(4, Label = "ApiKey", HelpText = "IndexerNebulanceSettingsApiKeyHelpText")]
        public string ApiKey { get; set; }
    }

    public class NebulanceQuery
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Id { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Time { get; set; }
        [JsonProperty(PropertyName="age", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Age { get; set; }
        [JsonProperty(PropertyName="tvmaze", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? Tvmaze { get; set; }
        [JsonProperty(PropertyName="imdb", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? Imdb { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Hash { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string[] Tags { get; set; }
        [JsonProperty(PropertyName="name", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Name { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Category { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Series { get; set; }

        public NebulanceQuery Clone()
        {
            return MemberwiseClone() as NebulanceQuery;
        }
    }

    public class NebulanceTorrent
    {
        [JsonPropertyName("rls_name")]
        public string ReleaseTitle { get; set; }
        [JsonPropertyName("cat")]
        public string Category { get; set; }
        public string Size { get; set; }
        public string Seed { get; set; }
        public string Leech { get; set; }
        public string Snatch { get; set; }
        public string Download { get; set; }
        [JsonPropertyName("file_list")]
        public string[] FileList { get; set; }
        [JsonPropertyName("group_name")]
        public string GroupName { get; set; }
        [JsonPropertyName("series_banner")]
        public string Banner { get; set; }
        [JsonPropertyName("group_id")]
        public string TorrentId { get; set; }
        [JsonPropertyName("series_id")]
        public string TvMazeId { get; set; }
        [JsonPropertyName("rls_utc")]
        public string PublishDateUtc { get; set; }
        public IEnumerable<string> Tags { get; set; }
    }

    public class NebulanceTorrents
    {
        public List<NebulanceTorrent> Items { get; set; }
        public int Results { get; set; }
    }
}
