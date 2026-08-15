// SPDX-License-Identifier: MPL-2.0
// Copyright 2026 Spirit-Schema

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using TarkovServerReporter;

// Reproducible compatibility harness for the Apache-2.0 MaxMind-DB test-data files.
// Usage: OfficialMmdbCompatibilityTests.exe <path-to-MaxMind-DB-test-data>
internal static class OfficialMmdbCompatibilityTests
{
    private static int Main(string[] args)
    {
        if (args.Length != 1 || !Directory.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: OfficialMmdbCompatibilityTests.exe <MaxMind-DB test-data directory>");
            return 2;
        }

        string[] searchable =
        {
            "ipv4-24.mmdb", "ipv4-28.mmdb", "ipv4-32.mmdb",
            "ipv6-24.mmdb", "ipv6-28.mmdb", "ipv6-32.mmdb",
            "mixed-24.mmdb", "mixed-28.mmdb", "mixed-32.mmdb"
        };
        try
        {
            foreach (string name in searchable)
            {
                Console.WriteLine("Checking " + name);
                using (var reader = new DbIpLiteMmdbReader(Path.Combine(args[0], name)))
                {
                    Assert(reader.DatabaseType.Length > 0, name + " database_type missing");
                    IPAddress address = name.StartsWith("ipv4-", StringComparison.Ordinal)
                        ? IPAddress.Parse("1.1.1.1")
                        : IPAddress.Parse("::1:ffff:ffff");
                    IDictionary<string, object> record = reader.Lookup(address);
                    Assert(record != null, name + " representative lookup failed");
                    Assert(GetText(record, "ip") == address.ToString(),
                        name + " representative record mismatch");
                    if (!name.StartsWith("ipv4-", StringComparison.Ordinal))
                    {
                        IDictionary<string, object> second = reader.Lookup(IPAddress.Parse("::2:0:1"));
                        Assert(GetText(second, "ip") == "::2:0:0",
                            name + " IPv6 network record mismatch");
                    }
                    if (name.StartsWith("mixed-", StringComparison.Ordinal))
                    {
                        IDictionary<string, object> ipv4 = reader.Lookup(IPAddress.Parse("1.1.1.1"));
                        Assert(GetText(ipv4, "ip") == "::1.1.1.1",
                            name + " IPv4-in-IPv6 record mismatch");
                    }
                }
            }

            using (var metadata = new DbIpLiteMmdbReader(
                Path.Combine(args[0], "metadata-pointers.mmdb")))
                Assert(metadata.DatabaseType.Length > 0, "metadata pointer decoding failed");

            using (var decoder = new DbIpLiteMmdbReader(Path.Combine(args[0], "decoder.mmdb")))
                Assert(decoder.Lookup(IPAddress.Parse("::1.1.1.0")) != null,
                    "decoder representative lookup failed");

            Console.WriteLine("All official MMDB compatibility samples passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string GetText(IDictionary<string, object> map, string key)
    {
        object value;
        return map != null && map.TryGetValue(key, out value) ? value as string : null;
    }
}
