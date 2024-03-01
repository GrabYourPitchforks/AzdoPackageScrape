using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AzdoPackageScrape;
using AzdoPackageScrape.DevOps;

namespace ConsoleAppSearchAzDO
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // replace me if you want to be able to cancel any of these operations
            CancellationToken cancellationToken = CancellationToken.None;

            AzdoConnection connection;

            while (true)
            {
                Console.Write("Please provide AzDO URL: ");
                string uriStringOrOrgName = Console.ReadLine();

                try
                {
                    connection = new AzdoConnection(uriStringOrOrgName);
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"{e.GetType()}: {e.Message}");
                }
            }

            Console.WriteLine($"Authenticating to {connection.BaseUri}...");
            Console.WriteLine("(Check that the auth dialog isn't hidden under the console window.)");
            var authenticatedIdentity = await connection.AuthenticateAsync(cancellationToken);
            Console.WriteLine($"Authenticated as {authenticatedIdentity.DisplayName} <{authenticatedIdentity.Properties["Account"]}>.");
            Console.WriteLine();

            string packageName;
            bool performWildcardMatch = false;

            while (true)
            {
                Console.WriteLine("Enter package name to search for.");
                Console.WriteLine("Type \".*\" at the end to perform a wildcard search.");
                Console.Write("Package: ");
                packageName = Console.ReadLine()?.Trim();

                if (!string.IsNullOrEmpty(packageName))
                {
                    if (packageName.EndsWith(".*", StringComparison.Ordinal))
                    {
                        packageName = packageName[..^2];
                        performWildcardMatch = true;
                    }

                    // really naive search, but it's good enough for our usage
                    if (Regex.IsMatch(packageName, @"^[a-zA-Z0-9_-]+(\.[a-zA-Z0-9_-]+)*$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant))
                    {
                        break;
                    }
                }

                Console.WriteLine("Bad package name."); // and try again
            }

            Console.WriteLine();
            Console.WriteLine($"Searching {connection.BaseUri} for {packageName}{((performWildcardMatch) ? ".*" : "")}...");

            // Issue search request to backend

            QueryParameters query = new QueryParameters(connection, packageName, performWildcardMatch, 1000);
            int matchCount = await AzdoCodeSearchPipeline.GetMatchCountAsync(query, cancellationToken);
            Console.WriteLine($"Found {matchCount} match(es).");

            if (matchCount <= 0) { return; }

            int numRecordsToRetrieve;

            while (true)
            {
                numRecordsToRetrieve = Math.Min(1000, matchCount);
                Console.Write($"Enter number of records to retrieve [default = {numRecordsToRetrieve}]: ");
                string recordCountStr = Console.ReadLine();
                if (!string.IsNullOrEmpty(recordCountStr))
                {
                    if (!int.TryParse(recordCountStr, out numRecordsToRetrieve) || (numRecordsToRetrieve is <= 0 or > 10000))
                    {
                        Console.WriteLine("Invalid number.");
                        continue;
                    }
                }
                break;
            }

            query = query with { MaxRecordsToReturn = numRecordsToRetrieve };
            int numRecordsToRetrieveStringLength = numRecordsToRetrieve.ToString().Length;

            List<AzdoPackageLookupResult> allRecordsList = new List<AzdoPackageLookupResult>();
            var allRecordsAsyncEnum = AzdoCodeSearchPipeline.GetResultsAsync(query, cancellationToken);
            await foreach (var record in allRecordsAsyncEnum)
            {
                if (record is null) { continue; }
                allRecordsList.Add(record);
                Console.WriteLine($"Processing {allRecordsList.Count.ToString().PadLeft(numRecordsToRetrieveStringLength, ' ')} of {numRecordsToRetrieve} [{100 * (double)allRecordsList.Count / (double)numRecordsToRetrieve,5:F1}%]");
            }

            if (allRecordsList.Count < numRecordsToRetrieve)
            {
                Console.WriteLine($"{numRecordsToRetrieve - allRecordsList.Count} record(s) could not be processed.");
            }

            Console.WriteLine();
            Console.WriteLine("Writing to disk...");

            // results-<org>-<package>-packages_all.csv ; contains all package references
            // results-<org>-<package>-packages_stats.csv ; packages grouped by hit count
            // results-<org>-<package>-assemblies_all.csv ; contains all assembly references
            // results-<org>-<package>-assemblies_stats.csv ; assemblies grouped by hit count

            string outputFilenameBase = $"results-{connection.OrgName}-{packageName}".ToLowerInvariant();

            // Full list of packages and assemblies
            using (var pkgWriter = File.CreateText($"{outputFilenameBase}-packages_all.csv"))
            using (var asmWriter = File.CreateText($"{outputFilenameBase}-assemblies_all.csv"))
            {
                // Headers
                pkgWriter.WriteLine("Url,Organization,Project,Repository,Path,Branch,PackageName,PackageVersion");
                asmWriter.WriteLine("Url,Organization,Project,Repository,Path,Branch,AssemblyName,AssemblyVersion");

                // Individual rows
                foreach (var record in allRecordsList)
                {
                    var fileRef = record.FileReference;
                    string baseInfo = $"{CsvEscape(fileRef.ToFriendlyUrl())},{CsvEscape(fileRef.Organization)},{CsvEscape(fileRef.Project)},{CsvEscape(fileRef.Repository)},{CsvEscape(fileRef.Path)},{CsvEscape(fileRef.Branch)},";
                    foreach (var pkgRef in record.PackageReferences)
                    {
                        pkgWriter.WriteLine($"{baseInfo}{CsvEscape(pkgRef.PackageName)},{CsvEscape(pkgRef.PackageVersion)}");
                    }
                    foreach (var asmRef in record.AssemblyReferences)
                    {
                        asmWriter.WriteLine($"{baseInfo}{CsvEscape(asmRef.AssemblyName)},{CsvEscape(asmRef.AssemblyVersion)}");
                    }
                }
            }

            // Stats of packages
            using (var pkgWriter = File.CreateText($"{outputFilenameBase}-packages_stats.csv"))
            {
                pkgWriter.WriteLine("Organization,PackageName,PackageVersion,HitCount");
                foreach (var record in from pkgRef in allRecordsList.SelectMany(o => o.PackageReferences)
                                       group pkgRef by (pkgRef.PackageName, pkgRef.PackageVersion) into g
                                       orderby g.Key.PackageName ascending, g.Key.PackageVersion ascending
                                       select (Key: g.Key, HitCount: g.Count()))
                {
                    pkgWriter.WriteLine($"{CsvEscape(connection.OrgName)},{CsvEscape(record.Key.PackageName)},{CsvEscape(record.Key.PackageVersion)},{record.HitCount}");
                }
            }

            // Stats of assemblies
            using (var asmWriter = File.CreateText($"{outputFilenameBase}-assemblies_stats.csv"))
            {
                asmWriter.WriteLine("Organization,AssemblyName,AssemblyVersion,HitCount");
                foreach (var record in from asmRef in allRecordsList.SelectMany(o => o.AssemblyReferences)
                                       group asmRef by (asmRef.AssemblyName, asmRef.AssemblyVersion) into g
                                       orderby g.Key.AssemblyName ascending, g.Key.AssemblyVersion ascending
                                       select (Key: g.Key, HitCount: g.Count()))
                {
                    asmWriter.WriteLine($"{CsvEscape(connection.OrgName)},{CsvEscape(record.Key.AssemblyName)},{CsvEscape(record.Key.AssemblyVersion)},{record.HitCount}");
                }
            }

            Console.WriteLine($"Files written to {outputFilenameBase}-*.csv.");
        }
    }
}
