using System;
using System.IO;
using TarkovServerReporter;

internal static class DbIpLiteActualDatabaseTests
{
    private static int Main(string[] args)
    {
        if (args.Length != 1 || !Directory.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: DbIpLiteActualDatabaseTests.exe <service-store-directory>");
            return 2;
        }

        try
        {
            string databasePath = Path.Combine(args[0], "dbip-city-lite.mmdb");
            using (var reader = new DbIpLiteMmdbReader(databasePath))
            {
                Assert(reader.DatabaseType.IndexOf("City", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Not a city MMDB: " + reader.DatabaseType);
                Assert(reader.BuildUtc.Year == 2026 && reader.BuildUtc.Month == 8,
                    "Unexpected build epoch: " + reader.BuildUtc.ToString("o"));
                Console.WriteLine("DatabaseType=" + reader.DatabaseType);
                Console.WriteLine("BuildUtc=" + reader.BuildUtc.ToString("o"));
            }

            using (var service = new DbIpLiteGeoService(args[0], new NoNetworkDownloadClient()))
            {
                Assert(service.HasUsableDatabase, "Service rejected the official DB-IP database.");
                PrintAndAssert(service, "8.8.8.8");
                PrintAndAssert(service, "1.1.1.1");
                PrintAndAssert(service, "209.58.188.216");
            }
            Console.WriteLine("Official DB-IP City Lite local lookup passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void PrintAndAssert(DbIpLiteGeoService service, string ip)
    {
        GeoInfo geo = service.Lookup(ip);
        Assert(geo.Success, ip + " lookup failed: " + geo.ErrorMessage);
        Assert(!string.IsNullOrWhiteSpace(geo.CountryCode), ip + " country code missing");
        Assert(!string.IsNullOrWhiteSpace(geo.Country), ip + " localized country missing");
        Console.WriteLine(ip + "=" + geo.City + "|" + geo.Region + "|"
            + geo.Country + "|" + geo.CountryCode);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class NoNetworkDownloadClient : IDbIpLiteDownloadClient
    {
        public System.Threading.Tasks.Task DownloadAsync(
            Uri uri, Stream destination, long maximumBytes,
            System.Threading.CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Network must not be used by the local lookup test.");
        }
    }
}
