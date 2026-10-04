using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TarkovServerReporter.Tests
{
    internal static class OperationSearchTests
    {
        private static int _assertions;

        private sealed class FakeTransport : IOperationSearchTransport
        {
            public Dictionary<string, string> Documents = Fixture();
            public readonly List<string> Requests = new List<string>();
            public string FailAt;
            public Task<string> GetAsync(string endpoint, CancellationToken cancellationToken)
            {
                Requests.Add(endpoint);
                if (endpoint == FailAt) throw new IOException("Simulated network outage");
                return Task.FromResult(Documents[endpoint]);
            }
        }

        private static int Main(string[] args)
        {
            string root = Path.Combine(Path.GetTempPath(), "TsgOperationSearchTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var transport = new FakeTransport();
                var service = new OperationSearchService(root, transport);
                Check(!service.HasData && service.ShouldRefresh, "First start needs data");
                var privateMemos = new[] { new OperationSearchMemo { Id = "private-one", Title = "개인 비밀", Text = "전달 금지 소중한 초안", MapId = "customs" } };
                Check(service.Search("소중한", null, privateMemos, 10).Single().Kind == "memo", "Private memo search works without network");
                Check(transport.Requests.Count == 0, "Search does not access transport");
                Check(service.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult(), "Valid static documents install");
                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel();
                    Check(!service.RefreshAsync(cancelled.Token).GetAwaiter().GetResult(), "Pre-cancelled refresh is contained");
                }
                Check(service.HasData && !service.ShouldRefresh, "Cache freshness suppresses repeat download");
                Check(transport.Requests.Count == 12, "Exactly twelve fixed public requests");
                Check(transport.Requests.All(request => !request.Contains("소중한") && !request.Contains("?")), "Network endpoint privacy");
                var korean = service.Search("볼트", "bigmap", privateMemos, 10).Single(row => row.Kind == "item");
                Check(korean.EnglishTitle == "Bolts" && korean.KoreanNameAvailable, "Korean and English names paired");
                Check(service.Search("BOLTS", null, null, 10).Single(row => row.Kind == "item").Id == korean.Id, "English case-insensitive lookup");
                Check(korean.Summary.Contains("부족") && korean.Summary.Contains("확보 ×3 (FIR)") && korean.Summary.Contains("제출 ×3 (FIR)"), "Quest reverse usage keeps objective roles");
                Check(!korean.Summary.Contains("×6"), "Find and give are never summed");
                Check(korean.Summary.Contains("작업대 Lv.2 ×4 (FIR)"), "Hideout station level quantity and FIR retained");
                Check(!korean.Summary.Contains("reward-only"), "Task rewards are never requirements");
                var fallback = service.Search("English Only", null, null, 10).Single(row => row.Kind == "item");
                Check(fallback.Title == "English Only" && !fallback.KoreanNameAvailable, "Missing Korean falls back to English honestly");
                Check(service.Search("Upper case", null, null, 10).Any(row => row.Id == "caseItem"), "Case-sensitive locale keys are not merged");
                Check(service.Search("tagilla", null, null, 10).Any(row => row.Kind == "boss"), "Bosses share search");
                Check(service.Search("customs", null, null, 10).Any(row => row.Kind == "map"), "Maps share search");
                var exit = service.Search("Gate", null, null, 10).Single(row => row.Kind == "exit");
                Check(exit.Summary.Contains("추가 조건 확인 필요") && !exit.Summary.Contains("조건 없음"), "Absent exit conditions stay unknown");
                Check(service.GetExitSummary("bigmap").Contains("일반 탈출구 정보"), "Log map alias resolves");
                Check(service.GetExitSummary("unknown-map").StartsWith("맵 미확인", StringComparison.Ordinal), "Unknown maps are never guessed");
                Check(service.Search("탈출구", null, null, 10).Count == 1, "Category keyword search");
                Check(service.Search("볼트", null, null, 0).Count == 1, "Result limit bounded");
                korean.Title = "mutated result";
                korean.MapIds[0] = "bad-map";
                Check(service.Search("볼트", null, null, 10).First().Title == "볼트", "Search returns detached cache rows");
                string cachePath = Path.Combine(root, "operation-search-v1.json");
                Check(!File.ReadAllText(cachePath).Contains("소중한") && !File.ReadAllText(cachePath).Contains("private-one"), "Private memo content never enters cache");
                string original = File.ReadAllText(cachePath);
                transport.FailAt = "maps_ko";
                Check(!service.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult(), "Late network failure contained");
                Check(File.ReadAllText(cachePath) == original && service.Search("볼트", null, null, 10).Count == 1, "Last good disk and memory cache survive late failure");
                Check(service.StatusText.Contains("마지막 정상"), "Stale state visible");
                transport.FailAt = null;
                transport.Documents["maps"] = "{\"data\":{\"maps\":{},\"mobs\":{}}}";
                Check(!service.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult(), "Empty dataset cannot replace cache");
                Check(File.ReadAllText(cachePath) == original, "Incomplete response preserves good disk cache");
                transport.Documents = Fixture();
                transport.Documents["tasks"] = "{ broken";
                Check(!service.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult(), "Malformed JSON cannot break notes");
                transport.Documents = Fixture();
                Check(service.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult(), "Valid refresh recovers after failure");
                Check(File.Exists(cachePath + ".bak"), "Prior cache retained as backup");
                File.WriteAllText(cachePath, "damaged");
                var recovered = new OperationSearchService(root, transport);
                Check(recovered.HasData && recovered.Search("볼트", null, null, 10).Count == 1, "Corrupt active cache recovers backup");
                Check(recovered.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult(), "Refresh repairs corrupt active cache");
                File.WriteAllText(cachePath, "damaged again");
                Check(new OperationSearchService(root, transport).HasData, "Recovery does not overwrite good backup with corrupt file");
                Check(new OperationSearchService(Path.Combine(root, "empty"), new FakeTransport { FailAt = "items" }).Search("소중한", null, privateMemos, 10).Count == 1,
                    "Offline first-start memo search remains available");
                if (args.Length > 0) VerifyLiveDocuments(args[0], Path.Combine(root, "live"));
                if (args.Length > 1 && args[1] == "--network")
                {
                    var network = new OperationSearchService(Path.Combine(root, "network"));
                    Check(network.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult(), "Real product HTTP transport refreshes live endpoints");
                    Console.WriteLine("NETWORK: " + network.ResultCount + " indexed records; " + network.StatusText);
                }
                Console.WriteLine("OperationSearchTests: " + _assertions + " assertions passed.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { Directory.Delete(root, true); }
        }

        private static void VerifyLiveDocuments(string documents, string cacheDirectory)
        {
            var service = new OperationSearchService(cacheDirectory, new FakeTransport());
            var timer = Stopwatch.StartNew();
            service.ImportJsonFiles(documents);
            long importMilliseconds = timer.ElapsedMilliseconds;
            var salewa = service.Search("Salewa", null, null, 10).First(row => row.Id == "544fb45d4bdc2dee738b4568");
            var bolts = service.Search("볼트", null, null, 20).First(row => row.Id == "57347c5b245977448d35f6e1");
            var flash = service.Search("플래시", null, null, 20).First(row => row.Id == "590c621186f774138d11ea29");
            Check(service.ResultCount > 5300, "Live dataset contains expected broad records");
            Check(salewa.KoreanNameAvailable && bolts.KoreanNameAvailable && flash.KoreanNameAvailable, "Representative live Korean names present");
            Check(salewa.Summary.Contains("×3") && salewa.Summary.Contains("FIR"), "Live Salewa quest usage");
            Check(bolts.Summary.Contains("Lv."), "Live bolts hideout reverse usage");
            Check(!bolts.Summary.Contains("판매 ×50") && !bolts.Summary.Contains("판매 ×75"), "Generic sell-any-item tasks do not masquerade as stock requirements");
            Check(salewa.Summary.Contains("대체 가능 대상 중 합계"), "Alternative quest item quantities are not per-item requirements");
            Check(service.GetExitSummary("bigmap").Contains("추가 조건 확인 필요"), "Live Customs exits retain unknown conditions");
            Check(service.GetExitSummary("Terminal").Contains("탈출구 정보가 없습니다"), "Live empty exits disclosed");
            var cliff = service.Search("Cliff Descent", "reserve", null, 10).First(row => row.Kind == "exit" && row.ConditionSourceUrl == "https://tarkov.help/en/article/cliff-descent");
            Check(cliff.Summary.Contains("Paracord") && cliff.Summary.Contains("방탄 리그 가능"), "Verified cliff conditions supplement missing JSON fields");
            Check(cliff.ConditionSourceUrl == "https://tarkov.help/en/article/cliff-descent" && cliff.ConditionCheckedOn == "2026-09-13", "Supplement records independent source and review date");
            Check(cliff.ToNoteText().Contains(cliff.ConditionSourceUrl), "Copied supplement preserves attribution");
            Check(service.GetExitSummary("interchange").Contains("배낭 해제"), "Verified no-backpack extract supplement");
            Check(service.GetExitSummary("reserve").Contains("PMC와 플레이어 Scav"), "Verified co-op extract supplement");
            var train = service.Search("Armored Train", "reserve", null, 10).First(row => row.Kind == "exit" && row.MapId == cliff.MapId);
            Check(!train.Summary.Contains("함께 탈출") && String.IsNullOrEmpty(train.ConditionSourceUrl), "Shared faction alone never becomes co-op");
            timer.Restart();
            for (int index = 0; index < 50; index++) service.Search(index % 2 == 0 ? "볼트" : "customs", "bigmap", null, 8);
            long searchMilliseconds = timer.ElapsedMilliseconds;
            Console.WriteLine("LIVE: results=" + service.ResultCount + ", importMs=" + importMilliseconds + ", 50SearchesMs=" + searchMilliseconds);
            Console.WriteLine("SALEWA: " + salewa.Title + " | " + salewa.Summary.Replace('\n', '|'));
            Console.WriteLine("BOLTS: " + bolts.Title + " | " + bolts.Summary.Replace('\n', '|'));
            Console.WriteLine("FLASH: " + flash.Title + " | " + flash.Summary.Replace('\n', '|'));
        }

        private static Dictionary<string, string> Fixture()
        {
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            data["items"] = "{\"data\":{\"items\":{\"bolts\":{\"id\":\"bolts\",\"name\":\"bolts Name\",\"shortName\":\"bolts ShortName\",\"normalizedName\":\"bolts\"},\"fallback\":{\"id\":\"fallback\",\"name\":\"fallback Name\",\"normalizedName\":\"english-only\"},\"caseItem\":{\"id\":\"caseItem\",\"name\":\"Case\",\"normalizedName\":\"case-item\"}}}}";
            data["items_en"] = "{\"data\":{\"bolts Name\":\"Bolts\",\"bolts ShortName\":\"Bolts\",\"fallback Name\":\"English Only\",\"Case\":\"Upper case\",\"case\":\"Lower case\"}}";
            data["items_ko"] = "{\"data\":{\"bolts Name\":\"볼트\",\"bolts ShortName\":\"볼트\"}}";
            data["tasks"] = "{\"data\":{\"tasks\":{\"task1\":{\"id\":\"task1\",\"name\":\"task1 Name\",\"map\":\"customs\",\"objectives\":[{\"type\":\"findItem\",\"items\":[\"bolts\"],\"count\":3,\"foundInRaid\":true},{\"type\":\"giveItem\",\"items\":[\"bolts\"],\"count\":3,\"foundInRaid\":true}],\"finishRewards\":{\"items\":[{\"item\":\"reward-only\",\"count\":20}]}}}}}";
            data["tasks_en"] = "{\"data\":{\"task1 Name\":\"Shortage\"}}";
            data["tasks_ko"] = "{\"data\":{\"task1 Name\":\"부족\"}}";
            data["hideout"] = "{\"data\":{\"workbench\":{\"id\":\"workbench\",\"name\":\"workbench Name\",\"levels\":[{\"level\":2,\"itemRequirements\":[{\"item\":\"bolts\",\"count\":4,\"attributes\":{\"foundInRaid\":true}}]}]}}}";
            data["hideout_en"] = "{\"data\":{\"workbench Name\":\"Workbench\"}}";
            data["hideout_ko"] = "{\"data\":{\"workbench Name\":\"작업대\"}}";
            data["maps"] = "{\"data\":{\"maps\":{\"customs\":{\"id\":\"customs\",\"name\":\"customs Name\",\"nameId\":\"bigmap\",\"normalizedName\":\"customs\",\"players\":\"10-12\",\"raidDuration\":40,\"bosses\":[{\"mob\":\"bossTagilla\"}],\"hazards\":[{\"hazardType\":\"minefield\"}],\"extracts\":[{\"id\":\"gate\",\"name\":\"gate Name\",\"faction\":\"pmc\",\"switch\":false,\"switches\":[]}]}},\"mobs\":{\"bossTagilla\":{\"id\":\"bossTagilla\",\"name\":\"bossTagilla\",\"normalizedName\":\"tagilla\"}}}}";
            data["maps_en"] = "{\"data\":{\"customs Name\":\"Customs\",\"bossTagilla\":\"Tagilla\",\"gate Name\":\"Gate\"}}";
            data["maps_ko"] = "{\"data\":{\"customs Name\":\"세관\",\"bossTagilla\":\"타길라\",\"gate Name\":\"문\"}}";
            return data;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + name);
            _assertions++;
        }
    }
}
