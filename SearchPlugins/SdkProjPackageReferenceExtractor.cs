using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

using static AzdoPackageScrape.SearchPlugins.ExtractorUtil;

namespace AzdoPackageScrape.SearchPlugins
{
    // <PackageReference Include="System.Foo" Version="1.2.3" />
    public class SdkProjPackageReferenceExtractor : IPackageReferenceExtractor
    {
        private static readonly string[] _matchingTargetFrameworks = new[]
        {
            "netcoreapp2", // matches ".0", ".1", ".2", ...
            "netcoreapp3",
            "net5",
            "net462",
            "net47",
            "net471",
            "net472",
            "net48",
            "net481",
        };

        public IEnumerable<PackageReference> ExtractPackageReferences(XDocument document, string targetPackageName, bool useWildcardSuffix)
            => from packageReference in document.Descendants("PackageReference")
               let packageName = packageReference.Attribute("Include")?.Value?.Trim()
               where PackageOrAssemblyNameMatches(packageName, targetPackageName, useWildcardSuffix)
               let packageVersion = TryParsePackageVersion(packageReference.Attribute("Version")?.Value?.Trim())
               where packageVersion is not null
               select new PackageReference(packageName, packageVersion);

        public string GetSearchClause(string basePackageName, bool useWildcardSuffix)
        {
            StringBuilder builder = new StringBuilder();

            // exact match
            builder.Append($"""
                            ext:*proj AND ("<PackageReference Include=\"{basePackageName}\""
                            """);

            if (useWildcardSuffix)
            {
                // wildcard match
                builder.Append(' ');
                builder.Append($"""
                                OR "<PackageReference Include=\"{basePackageName}."
                                """);
            }

            // If you *don't* want to limit results to the matching frameworks
            // listed in the _matchingTargetFrameworks field at the top of this
            // file, comment out the three lines below.

            builder.Append(") AND (");
            builder.Append(string.Join(" OR ", _matchingTargetFrameworks.Select(tf => $"\"<TargetFramework>{tf}\"")));
            builder.Append(')');

            return builder.ToString();
        }
    }
}
