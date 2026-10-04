using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TarkovServerReporter
{
    internal sealed class OperationSearchMemo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public string MapId { get; set; }
    }

    internal sealed class OperationSearchResult
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Title { get; set; }
        public string EnglishTitle { get; set; }
        public string Summary { get; set; }
        public string SourceUrl { get; set; }
        public string ConditionSourceUrl { get; set; }
        public string ConditionCheckedOn { get; set; }
        public string MapId { get; set; }
        public string[] MapIds { get; set; }
        public string[] Aliases { get; set; }
        public bool KoreanNameAvailable { get; set; }

        public string ToNoteText()
        {
            if (Kind == "memo") return Summary ?? String.Empty;
            string text = (Title ?? EnglishTitle ?? String.Empty) + Environment.NewLine + (Summary ?? String.Empty);
            if (!String.IsNullOrEmpty(ConditionSourceUrl)) text += Environment.NewLine + "조건 보충: " + ConditionSourceUrl + " (" + ConditionCheckedOn + ")";
            return text;
        }

        public string KindLabel
        {
            get
            {
                switch (Kind)
                {
                    case "item": return "아이템";
                    case "map": return "맵";
                    case "boss": return "보스·특수 적";
                    case "exit": return "탈출구";
                    case "memo": return "내 메모";
                    default: return "정보";
                }
            }
        }

        public override string ToString() { return "[" + KindLabel + "] " + Title; }
    }

    internal sealed class OperationSearchCache
    {
        public int SchemaVersion { get; set; }
        public string UpdatedUtc { get; set; }
        public List<OperationSearchResult> Results { get; set; }
        public int TaskCount { get; set; }
        public int HideoutCount { get; set; }
    }

    // Only static public endpoints enter this interface. Search terms and user notes never do.
    internal interface IOperationSearchTransport
    {
        Task<string> GetAsync(string endpoint, CancellationToken cancellationToken);
    }

    internal sealed class OperationSearchHttpTransport : IOperationSearchTransport
    {
        public async Task<string> GetAsync(string endpoint, CancellationToken cancellationToken)
        {
            if (!Regex.IsMatch(endpoint ?? String.Empty, "^(items|tasks|hideout|maps)(_en|_ko)?$"))
                throw new ArgumentException("Unknown static data endpoint.", "endpoint");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(35) })
            {
                // ResponseHeadersRead ends HttpClient's own timeout at the headers.
                // Keep a deadline over the body stream as well.
                timeout.CancelAfter(TimeSpan.FromSeconds(35));
                cancellationToken = timeout.Token;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("TarkovServerGuard/0.8.8");
                using (var response = await client.GetAsync("https://json.tarkov.dev/regular/" + endpoint,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > OperationSearchService.MaximumDocumentBytes)
                        throw new InvalidDataException("Search response is too large.");
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[32768];
                        int count;
                        while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            if (output.Length + count > OperationSearchService.MaximumDocumentBytes)
                                throw new InvalidDataException("Search response is too large.");
                            output.Write(buffer, 0, count);
                        }
                        return Encoding.UTF8.GetString(output.ToArray());
                    }
                }
            }
        }
    }

    internal sealed class OperationSearchService
    {
        internal const int MaximumDocumentBytes = 32 * 1024 * 1024;
        public const string Attribution = "tarkov.dev 커뮤니티 데이터 · regular · 실제 레이드 상태와 다를 수 있음";
        private readonly string _cachePath;
        private readonly IOperationSearchTransport _transport;
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);
        private OperationSearchCache _cache;
        private SearchEntry[] _index = new SearchEntry[0];
        private bool _loadedBackup;
        private string _statusText = "검색 데이터를 준비 중입니다. 메모는 계속 작성할 수 있습니다.";
        private DateTime _lastAttemptUtc;

        private sealed class SearchEntry
        {
            public OperationSearchResult Row;
            public string Title;
            public string KoreanTitle;
            public string EnglishTitle;
            public string SearchText;
        }

        public OperationSearchService(string cacheDirectory) : this(cacheDirectory, new OperationSearchHttpTransport()) { }

        internal OperationSearchService(string cacheDirectory, IOperationSearchTransport transport)
        {
            if (String.IsNullOrWhiteSpace(cacheDirectory)) throw new ArgumentException("Cache directory is required.", "cacheDirectory");
            if (transport == null) throw new ArgumentNullException("transport");
            _cachePath = Path.Combine(cacheDirectory, "operation-search-v1.json");
            _transport = transport;
            LoadCache();
        }

        public bool HasData { get { lock (_gate) return _cache != null; } }
        public string StatusText { get { lock (_gate) return _statusText; } }
        public DateTime? CacheUpdatedUtc
        {
            get { lock (_gate) { DateTime value; return _cache != null && DateTime.TryParse(_cache.UpdatedUtc,
                CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value)
                ? (DateTime?)value : null; } }
        }
        public int ResultCount { get { lock (_gate) return _cache == null ? 0 : _cache.Results.Count; } }

        // Call in the background. All twelve documents must validate before replacing the cache.
        public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || !await _refreshGate.WaitAsync(0).ConfigureAwait(false)) return false;
            try
            {
                lock (_gate) _lastAttemptUtc = DateTime.UtcNow;
                var documents = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string name in new[] { "items", "tasks", "hideout", "maps" })
                {
                    foreach (string suffix in new[] { String.Empty, "_en", "_ko" })
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        documents[name + suffix] = await _transport.GetAsync(name + suffix, cancellationToken).ConfigureAwait(false);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                OperationSearchCache next = OperationSearchParser.Parse(documents, DateTime.UtcNow);
                cancellationToken.ThrowIfCancellationRequested();
                InstallCache(next);
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is HttpRequestException || ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException ||
                    ex is ArgumentException || ex is InvalidOperationException || ex is OperationCanceledException || ex is FormatException)) throw;
                lock (_gate) _statusText = _cache == null ? "검색 데이터를 받지 못했습니다. 내 메모 검색과 작성은 사용할 수 있습니다."
                    : "검색 갱신 실패 · 마지막 정상 데이터 사용 중 (" + CacheDate(_cache) + ")";
                return false;
            }
            finally { _refreshGate.Release(); }
        }

        public bool ShouldRefresh
        {
            get { lock (_gate) { DateTime? updated = CacheUpdatedUtc; return DateTime.UtcNow - _lastAttemptUtc > TimeSpan.FromMinutes(10)
                && (!updated.HasValue || DateTime.UtcNow - updated.Value > TimeSpan.FromHours(24)); } }
        }

        // Local inspection/bootstrap helper. These are public API responses, never private notes.
        public void ImportJsonFiles(string directory)
        {
            var documents = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in new[] { "items", "tasks", "hideout", "maps" })
                foreach (string suffix in new[] { String.Empty, "_en", "_ko" })
                {
                    string path = Path.Combine(directory, name + suffix + ".json");
                    if (new FileInfo(path).Length > MaximumDocumentBytes) throw new InvalidDataException("Search response is too large.");
                    documents[name + suffix] = File.ReadAllText(path, Encoding.UTF8);
                }
            InstallCache(OperationSearchParser.Parse(documents, DateTime.UtcNow));
        }

        public List<OperationSearchResult> Search(string query, string currentMap, IEnumerable<OperationSearchMemo> privateMemos, int limit)
        {
            string normalized = Normalize(query);
            if (normalized.Length == 0) return new List<OperationSearchResult>();
            if (normalized.Length > 160) normalized = normalized.Substring(0, 160);
            string[] words = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var entries = new List<SearchEntry>();
            lock (_gate) entries.AddRange(_index);
            if (privateMemos != null)
                foreach (OperationSearchMemo memo in privateMemos)
                {
                    if (memo == null || String.IsNullOrWhiteSpace(memo.Text)) continue;
                    entries.Add(Index(new OperationSearchResult { Id = memo.Id, Kind = "memo", Title = String.IsNullOrWhiteSpace(memo.Title)
                        ? "내 메모" : memo.Title, Summary = memo.Text, MapId = memo.MapId, MapIds = new[] { memo.MapId ?? String.Empty },
                        EnglishTitle = String.Empty, Aliases = new string[0], SourceUrl = String.Empty, KoreanNameAvailable = true }));
                }
            string mapId = ResolveMapId(currentMap, entries.Select(entry => entry.Row));
            return entries.Select(entry => new { Row = entry.Row, Score = Score(entry, words, normalized, mapId) })
                .Where(match => match.Score > 0).OrderByDescending(match => match.Score)
                .ThenBy(match => match.Row.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(Math.Max(1, Math.Min(50, limit))).Select(match => Clone(match.Row)).ToList();
        }

        public string GetExitSummary(string currentMap)
        {
            List<OperationSearchResult> rows;
            lock (_gate) rows = _cache == null ? new List<OperationSearchResult>() : _cache.Results;
            string mapId = ResolveMapId(currentMap, rows);
            if (String.IsNullOrEmpty(mapId)) return "맵 미확인 · 검색에서 맵을 선택하면 탈출구 정보를 볼 수 있습니다.";
            OperationSearchResult map = rows.FirstOrDefault(row => row.Kind == "map" && row.Id == mapId);
            List<OperationSearchResult> exits = rows.Where(row => row.Kind == "exit" && row.MapId == mapId).ToList();
            if (exits.Count == 0) return map.Title + " · 제공 데이터에 탈출구 정보가 없습니다.";
            var builder = new StringBuilder(map.Title + " · 일반 탈출구 정보\n");
            foreach (OperationSearchResult exit in exits)
                builder.Append(exit.Title).Append(" — ").AppendLine(exit.Summary);
            builder.Append("현재 활성 여부 미판정 · 추가 조건은 게임에서 확인 · tarkov.dev");
            return builder.ToString();
        }

        private void InstallCache(OperationSearchCache next)
        {
            ValidateCache(next);
            lock (_gate)
            {
                if (_cache != null && (next.Results.Count(row => row.Kind == "item") < _cache.Results.Count(row => row.Kind == "item") / 2 ||
                    next.TaskCount < _cache.TaskCount / 2 || next.HideoutCount < _cache.HideoutCount / 2 ||
                    next.Results.Count(row => row.Kind == "map") < _cache.Results.Count(row => row.Kind == "map") / 2))
                    throw new InvalidDataException("The search dataset unexpectedly shrank; keeping the prior cache.");
            }
            string directory = Path.GetDirectoryName(_cachePath);
            Directory.CreateDirectory(directory);
            string temporary = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(Serializer().Serialize(next));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(_cachePath)) File.Replace(temporary, _cachePath, _loadedBackup ? null : _cachePath + ".bak", true);
                else File.Move(temporary, _cachePath);
                SearchEntry[] index = next.Results.Select(Index).ToArray();
                lock (_gate) { _cache = next; _index = index; _loadedBackup = false; _statusText = "tarkov.dev / regular · 받은 시각 " + CacheDate(next); }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private void LoadCache()
        {
            foreach (string path in new[] { _cachePath, _cachePath + ".bak" })
            {
                try
                {
                    if (!File.Exists(path) || new FileInfo(path).Length > MaximumDocumentBytes) continue;
                    var cache = Serializer().Deserialize<OperationSearchCache>(File.ReadAllText(path, Encoding.UTF8));
                    ValidateCache(cache);
                    _cache = cache;
                    _index = cache.Results.Select(Index).ToArray();
                    _loadedBackup = path.EndsWith(".bak", StringComparison.Ordinal);
                    _statusText = "tarkov.dev / regular · 받은 시각 " + CacheDate(cache);
                    return;
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                        ex is InvalidOperationException || ex is FormatException)) throw;
                }
            }
        }

        private static void ValidateCache(OperationSearchCache cache)
        {
            DateTime updated;
            if (cache == null || cache.SchemaVersion != 1 || cache.Results == null || cache.Results.Count > 30000 ||
                !DateTime.TryParse(cache.UpdatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out updated) ||
                cache.TaskCount < 1 || cache.HideoutCount < 1 ||
                !cache.Results.Any(row => row != null && row.Kind == "item") || !cache.Results.Any(row => row != null && row.Kind == "map"))
                throw new InvalidDataException("Invalid operation-search cache.");
            foreach (OperationSearchResult row in cache.Results)
                if (row == null || String.IsNullOrWhiteSpace(row.Id) || String.IsNullOrWhiteSpace(row.Title) || row.Summary == null ||
                    row.Aliases == null || row.MapIds == null || row.Kind == "memo")
                    throw new InvalidDataException("Invalid operation-search entry.");
        }

        private static string CacheDate(OperationSearchCache cache)
        {
            DateTime value;
            return DateTime.TryParse(cache.UpdatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out value)
                ? value.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) : "시간 미확인";
        }

        private static string ResolveMapId(string currentMap, IEnumerable<OperationSearchResult> rows)
        {
            string wanted = Normalize(currentMap);
            if (wanted.Length == 0) return String.Empty;
            OperationSearchResult match = rows.FirstOrDefault(row => row.Kind == "map" &&
                new[] { row.Id, row.Title, row.EnglishTitle }.Concat(row.Aliases ?? new string[0]).Any(alias => Normalize(alias) == wanted));
            return match == null ? String.Empty : match.Id;
        }

        private static SearchEntry Index(OperationSearchResult row)
        {
            string title = Normalize(row.Title + " " + row.EnglishTitle + " " + String.Join(" ", row.Aliases ?? new string[0]));
            return new SearchEntry { Row = row, Title = title, KoreanTitle = Normalize(row.Title), EnglishTitle = Normalize(row.EnglishTitle),
                SearchText = title + " " + Normalize(row.KindLabel + " " + row.Kind + " " + row.Summary) };
        }

        private static int Score(SearchEntry entry, string[] words, string query, string mapId)
        {
            OperationSearchResult row = entry.Row;
            if (!words.All(word => entry.SearchText.IndexOf(word, StringComparison.Ordinal) >= 0)) return 0;
            int score = 10;
            if (entry.KoreanTitle == query || entry.EnglishTitle == query) score += 100;
            if (entry.Title.StartsWith(query, StringComparison.Ordinal)) score += 30;
            if (words.All(word => entry.Title.IndexOf(word, StringComparison.Ordinal) >= 0)) score += 30;
            if (row.Kind == "memo") score += 4;
            if (!String.IsNullOrEmpty(mapId) && (row.MapId == mapId || (row.MapIds ?? new string[0]).Contains(mapId))) score += 15;
            return score;
        }

        private static OperationSearchResult Clone(OperationSearchResult row)
        {
            return new OperationSearchResult { Id = row.Id, Kind = row.Kind, Title = row.Title, EnglishTitle = row.EnglishTitle,
                Summary = row.Summary, SourceUrl = row.SourceUrl, MapId = row.MapId, MapIds = (string[])row.MapIds.Clone(),
                ConditionSourceUrl = row.ConditionSourceUrl, ConditionCheckedOn = row.ConditionCheckedOn,
                Aliases = (string[])row.Aliases.Clone(), KoreanNameAvailable = row.KoreanNameAvailable };
        }

        private static string Normalize(string value)
        {
            return Regex.Replace((value ?? String.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant(), "[\\s_\\-]+", " ").Trim();
        }

        internal static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaximumDocumentBytes, RecursionLimit = 128 };
        }
    }

    internal static class OperationSearchParser
    {
        private sealed class Locale
        {
            public Dictionary<string, object> En;
            public Dictionary<string, object> Ko;
            public string Get(string key, bool korean)
            {
                string value = Text(korean ? Ko : En, key);
                if (String.IsNullOrWhiteSpace(value)) value = Text(En, key);
                return String.IsNullOrWhiteSpace(value) ? String.Empty : Clean(value);
            }
            public bool HasKorean(string key) { return !String.IsNullOrWhiteSpace(Text(Ko, key)); }
        }

        public static OperationSearchCache Parse(IDictionary<string, string> documents, DateTime updatedUtc)
        {
            var roots = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            var locales = new Dictionary<string, Locale>(StringComparer.Ordinal);
            foreach (string name in new[] { "items", "tasks", "hideout", "maps" })
            {
                roots[name] = Document(documents[name]);
                locales[name] = new Locale { En = Document(documents[name + "_en"]), Ko = Document(documents[name + "_ko"]) };
                if (locales[name].En.Count == 0 || locales[name].Ko.Count == 0) throw new InvalidDataException("Empty translation document.");
            }
            Dictionary<string, object> items = Required(roots["items"], "items");
            Dictionary<string, object> tasks = Required(roots["tasks"], "tasks");
            Dictionary<string, object> stations = roots["hideout"];
            Dictionary<string, object> maps = Required(roots["maps"], "maps");
            Dictionary<string, object> mobs = Required(roots["maps"], "mobs");
            if (items.Count == 0 || tasks.Count == 0 || stations.Count == 0 || maps.Count == 0)
                throw new InvalidDataException("Incomplete search dataset.");
            var result = new List<OperationSearchResult>();
            var byItem = new Dictionary<string, OperationSearchResult>(StringComparer.Ordinal);
            var taskUses = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var hideoutUses = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var taskMaps = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var pair in items)
            {
                var entry = Create(Map(pair.Value), pair.Key, "item", locales["items"]);
                entry.SourceUrl = "https://tarkov.dev/item/" + Text(Map(pair.Value), "normalizedName");
                byItem.Add(pair.Key, entry);
                result.Add(entry);
            }
            foreach (var pair in tasks)
            {
                var task = Map(pair.Value);
                var useByItem = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                string taskName = Name(task, locales["tasks"], true);
                foreach (object rawObjective in Array(task, "objectives"))
                {
                    var objective = Map(rawObjective);
                    string type = Text(objective, "type");
                    // Requirements only: rewards and keys that merely unlock a route are not counted.
                    if (!new[] { "findItem", "giveItem", "plantItem", "mark", "useItem", "sellItem", "buildWeapon" }.Contains(type)) continue;
                    var objectiveItems = Array(objective, "items").Select(Reference).ToList();
                    string single = Reference(Value(objective, "item"));
                    if (single.Length > 0) objectiveItems.Add(single);
                    // "Sell any items" exposes thousands of eligible IDs. It does not mean
                    // the player must retain the listed quantity of every eligible item.
                    if (type == "sellItem" && objectiveItems.Distinct(StringComparer.Ordinal).Count() > 1) continue;
                    bool alternatives = objectiveItems.Distinct(StringComparer.Ordinal).Count() > 1;
                    foreach (string itemId in objectiveItems.Distinct(StringComparer.Ordinal))
                    {
                        if (!byItem.ContainsKey(itemId)) continue;
                        string count = Number(objective, "count");
                        // Keep each objective's quantity; finding and handing in the same objects are not summed.
                        string usage = ObjectiveLabel(type) + (count.Length > 0 ? " ×" + count : String.Empty)
                            + (alternatives ? " (대체 가능 대상 중 합계)" : String.Empty)
                            + (Flag(objective, "foundInRaid") ? " (FIR)" : String.Empty)
                            + (Flag(objective, "optional") ? " [선택]" : String.Empty);
                        Add(useByItem, itemId, usage);
                        HashSet<string> mapSet;
                        if (!taskMaps.TryGetValue(itemId, out mapSet)) { mapSet = new HashSet<string>(StringComparer.Ordinal); taskMaps[itemId] = mapSet; }
                        string taskMap = Reference(Value(task, "map"));
                        if (maps.ContainsKey(taskMap)) mapSet.Add(taskMap);
                        foreach (string objectiveMap in Array(objective, "maps").Select(Reference))
                            if (maps.ContainsKey(objectiveMap)) mapSet.Add(objectiveMap);
                    }
                }
                foreach (var use in useByItem)
                    Add(taskUses, use.Key, taskName + " — " + String.Join(" / ", use.Value.Distinct().ToArray()));
            }
            foreach (var pair in stations)
            {
                var station = Map(pair.Value);
                if (Text(station, "id").Length == 0 || Array(station, "levels").Length == 0)
                    throw new InvalidDataException("Invalid hideout station.");
                foreach (object rawLevel in Array(station, "levels"))
                {
                    var level = Map(rawLevel);
                    foreach (object rawRequirement in Array(level, "itemRequirements"))
                    {
                        var requirement = Map(rawRequirement);
                        string itemId = Reference(Value(requirement, "item"));
                        if (!byItem.ContainsKey(itemId)) continue;
                        Add(hideoutUses, itemId, Name(station, locales["hideout"], true) + " Lv." + Number(level, "level")
                            + " ×" + Number(requirement, "count") + (Flag(Map(Value(requirement, "attributes")), "foundInRaid") ? " (FIR)" : String.Empty));
                    }
                }
            }
            foreach (var pair in byItem)
            {
                List<string> uses;
                var lines = new List<string>();
                if (taskUses.TryGetValue(pair.Key, out uses)) lines.Add("퀘스트\n" + String.Join("\n", uses.Distinct().ToArray()));
                else lines.Add("퀘스트: 제공 데이터에서 요구 항목을 찾지 못했습니다.");
                if (hideoutUses.TryGetValue(pair.Key, out uses)) lines.Add("은신처\n" + String.Join("\n", uses.Distinct().ToArray()));
                else lines.Add("은신처: 제공 데이터에서 요구 항목을 찾지 못했습니다.");
                pair.Value.Summary = String.Join("\n", lines.ToArray());
                HashSet<string> related;
                if (taskMaps.TryGetValue(pair.Key, out related)) pair.Value.MapIds = related.ToArray();
            }
            var bossMaps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var pair in maps)
            {
                var map = Map(pair.Value);
                var entry = Create(map, pair.Key, "map", locales["maps"]);
                entry.MapId = pair.Key;
                entry.MapIds = new[] { pair.Key };
                entry.Aliases = entry.Aliases.Concat(new[] { Text(map, "nameId") }).ToArray();
                entry.SourceUrl = "https://tarkov.dev/map/" + Text(map, "normalizedName");
                var lines = new List<string>();
                string players = Text(map, "players");
                if (players.Length > 0) lines.Add("인원 " + players + " · 일반 제한 " + Number(map, "raidDuration") + "분");
                string minimum = Number(map, "minPlayerLevel");
                string maximum = Number(map, "maxPlayerLevel");
                if (minimum.Length > 0 && minimum != "0") lines.Add("입장 최소 레벨 " + minimum);
                if (maximum.Length > 0 && maximum != "0") lines.Add("입장 최대 레벨 " + maximum);
                var mapBosses = new List<string>();
                foreach (object rawBoss in Array(map, "bosses"))
                {
                    var boss = Map(rawBoss);
                    string mobId = Reference(Value(boss, "mob"));
                    object mob;
                    if (!mobs.TryGetValue(mobId, out mob)) continue;
                    mapBosses.Add(Name(Map(mob), locales["maps"], true));
                    Add(bossMaps, mobId, pair.Key);
                }
                if (mapBosses.Count > 0) lines.Add("보스·특수 적 데이터: " + String.Join(", ", mapBosses.Distinct().ToArray()));
                var hazards = Array(map, "hazards").Select(raw => HazardLabel(Text(Map(raw), "hazardType"))).Where(text => text.Length > 0).Distinct().ToArray();
                if (hazards.Length > 0) lines.Add("주의 구역 데이터: " + String.Join(", ", hazards) + " · 위치는 게임에서 확인");
                lines.Add("표시되지 않은 위험·조건이 있을 수 있습니다. 실제 출현·활성 여부는 판정하지 않습니다.");
                entry.Summary = String.Join("\n", lines.ToArray());
                result.Add(entry);
                foreach (object rawExit in Array(map, "extracts"))
                {
                    var exit = Map(rawExit);
                    string faction = Text(exit, "faction");
                    var exitEntry = Create(exit, pair.Key + ":" + Text(exit, "id") + ":" + faction, "exit", locales["maps"]);
                    exitEntry.MapId = pair.Key;
                    exitEntry.MapIds = new[] { pair.Key };
                    exitEntry.Aliases = exitEntry.Aliases.Concat(new[] { entry.Title, entry.EnglishTitle, Text(map, "nameId"), "탈출 조건", "extract" }).ToArray();
                    exitEntry.SourceUrl = entry.SourceUrl;
                    var conditions = new List<string>();
                    conditions.Add(faction == "shared" ? "PMC / Scav" : faction.Length == 0 ? "진영 미확인" : faction.ToUpperInvariant());
                    var transfer = Map(Value(exit, "transferItem"));
                    string transferId = Reference(Value(transfer, "item"));
                    if (transferId.Length > 0)
                        conditions.Add("제출 항목: " + (byItem.ContainsKey(transferId) ? byItem[transferId].Title : "이름 미확인") + " ×" + Number(transfer, "count"));
                    if (Array(exit, "switches").Length > 0 || Text(exit, "switch").Length > 0 && Text(exit, "switch") != "False" && Text(exit, "switch") != "false")
                        conditions.Add("스위치/이벤트 연결 있음 (상세 확인)");
                    string supplement = ExitSupplement(Text(map, "normalizedName"), Text(exit, "name"), exitEntry);
                    if (supplement.Length > 0) conditions.Add(supplement + " [보충 2026-09-13]");
                    conditions.Add("추가 조건 확인 필요");
                    exitEntry.Summary = String.Join(" · ", conditions.ToArray());
                    result.Add(exitEntry);
                }
            }
            foreach (var pair in bossMaps)
            {
                var boss = Create(Map(mobs[pair.Key]), pair.Key, "boss", locales["maps"]);
                boss.MapIds = pair.Value.Distinct().ToArray();
                boss.SourceUrl = "https://tarkov.dev/boss/" + Text(Map(mobs[pair.Key]), "normalizedName");
                boss.Summary = "출현 맵 데이터: " + String.Join(", ", boss.MapIds.Select(id => Name(Map(maps[id]), locales["maps"], true)).ToArray())
                    + "\n실제 출현 여부·위치는 판정하지 않습니다.";
                boss.Aliases = boss.Aliases.Concat(boss.MapIds.Select(id => Name(Map(maps[id]), locales["maps"], false))).ToArray();
                result.Add(boss);
            }
            return new OperationSearchCache { SchemaVersion = 1, UpdatedUtc = updatedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Results = result, TaskCount = tasks.Count, HideoutCount = stations.Count };
        }

        private static OperationSearchResult Create(Dictionary<string, object> value, string id, string kind, Locale locale)
        {
            string key = Text(value, "name");
            return new OperationSearchResult { Id = id, Kind = kind, Title = Name(value, locale, true), EnglishTitle = Name(value, locale, false),
                Summary = String.Empty, SourceUrl = String.Empty, MapId = String.Empty, MapIds = new string[0],
                KoreanNameAvailable = locale.HasKorean(key), Aliases = new[] { Text(value, "normalizedName").Replace('-', ' '),
                    locale.Get(Text(value, "shortName"), true), locale.Get(Text(value, "shortName"), false) } };
        }

        private static string Name(Dictionary<string, object> value, Locale locale, bool korean)
        {
            string name = locale.Get(Text(value, "name"), korean);
            if (name.Length > 0) return name;
            string normalized = Text(value, "normalizedName");
            if (normalized.Length > 0) return normalized.Replace('-', ' ');
            string original = Text(value, "name");
            return original.Length > 0 && !Regex.IsMatch(original, "^[a-f0-9]{24}( |$)") ? original : "이름 미확인";
        }

        private static Dictionary<string, object> Document(string json)
        {
            if (String.IsNullOrWhiteSpace(json) || json.Length > OperationSearchService.MaximumDocumentBytes)
                throw new InvalidDataException("Invalid search document.");
            var envelope = OperationSearchService.Serializer().DeserializeObject(json) as Dictionary<string, object>;
            return Required(envelope, "data");
        }

        private static Dictionary<string, object> Required(Dictionary<string, object> value, string key)
        {
            var result = Value(value, key) as Dictionary<string, object>;
            if (result == null) throw new InvalidDataException("Missing search data: " + key);
            return result;
        }
        private static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(StringComparer.Ordinal); }
        private static object Value(Dictionary<string, object> value, string key) { object result; return value != null && value.TryGetValue(key, out result) ? result : null; }
        private static string Text(Dictionary<string, object> value, string key) { object result = Value(value, key); return result == null ? String.Empty : Convert.ToString(result, CultureInfo.InvariantCulture).Trim(); }
        private static string Reference(object value) { return value is string ? (string)value : Text(Map(value), "id"); }
        private static object[] Array(Dictionary<string, object> value, string key)
        {
            object result = Value(value, key);
            var array = result as object[];
            if (array != null) return array;
            var list = result as ArrayList;
            return list == null ? new object[0] : list.ToArray();
        }
        private static bool Flag(Dictionary<string, object> value, string key) { return Object.Equals(Value(value, key), true); }
        private static string Number(Dictionary<string, object> value, string key)
        {
            decimal number;
            return Decimal.TryParse(Text(value, key), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                ? number.ToString("0.##", CultureInfo.InvariantCulture) : String.Empty;
        }
        private static void Add(Dictionary<string, List<string>> target, string key, string value)
        {
            List<string> list;
            if (!target.TryGetValue(key, out list)) { list = new List<string>(); target.Add(key, list); }
            if (!list.Contains(value)) list.Add(value);
        }
        private static string Clean(string value) { return Regex.Replace(value ?? String.Empty, "<[^>]+>", String.Empty).Trim(); }

        // Small, separately attributed facts verified against the linked source on this date.
        // Exact map + raw exit names are intentional: "shared" is not evidence of co-op,
        // and these conditions must never leak onto another exit with a similar label.
        private static string ExitSupplement(string mapName, string exitName, OperationSearchResult entry)
        {
            string text = String.Empty;
            string source = String.Empty;
            if (mapName == "reserve")
            {
                source = "https://tarkov.help/en/map/reserve/home";
                switch (exitName)
                {
                    case "Alpinist":
                        text = "Red Rebel 근접 무기 장착 + Paracord 소지 · 방탄복 해제 (방탄 리그 가능)";
                        source = "https://tarkov.help/en/article/cliff-descent";
                        break;
                    case "EXFIL_vent": text = "배낭 해제"; break;
                    case "EXFIL_Bunker": text = "탈출 스위치 작동 필요"; break;
                    case "EXFIL_ScavCooperation":
                        text = "PMC와 플레이어 Scav가 함께 탈출";
                        source = "https://tarkov.help/en/article/scav-lands";
                        break;
                }
            }
            else if (mapName == "interchange")
            {
                source = "https://tarkov.help/en/map/interchange/home";
                switch (exitName)
                {
                    case "Hole Exfill": text = "배낭 해제"; break;
                    case "Interchange Cooperation": text = "PMC와 플레이어 Scav가 함께 탈출"; break;
                    case "PP Exfil": text = "차량이 있어야 이용 가능 · 일회성 차량 탈출 · 실제 요금은 현장 확인"; break;
                }
            }
            if (text.Length > 0)
            {
                entry.ConditionSourceUrl = source;
                entry.ConditionCheckedOn = "2026-09-13";
            }
            return text;
        }
        private static string ObjectiveLabel(string type)
        {
            switch (type)
            {
                case "findItem": return "확보";
                case "giveItem": return "제출";
                case "plantItem": return "설치";
                case "mark": return "표식";
                case "useItem": return "사용";
                case "sellItem": return "판매";
                case "buildWeapon": return "개조 (세부 조건 확인)";
                default: return "요구";
            }
        }
        private static string HazardLabel(string value)
        {
            switch (value)
            {
                case "minefield": return "지뢰 구역";
                case "sniper": return "저격 위험 구역";
                case "claymore": return "클레이모어";
                default: return value;
            }
        }
    }
}
