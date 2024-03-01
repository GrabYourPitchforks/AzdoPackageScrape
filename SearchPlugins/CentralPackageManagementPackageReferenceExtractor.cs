using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

using static AzdoPackageScrape.SearchPlugins.ExtractorUtil;

namespace AzdoPackageScrape.SearchPlugins
{
    // https://learn.microsoft.com/nuget/consume-packages/Central-Package-Management
    // <PackageVersion Include="System.Foo" Version="1.2.3" />
    public class CentralPackageManagementPackageReferenceExtractor : IPackageReferenceExtractor
    {
        public IEnumerable<PackageReference> ExtractPackageReferences(XDocument document, string targetPackageName, bool useWildcardSuffix)
            => from pvElement in document.Descendants("PackageVersion")
               let packageName = pvElement.Attribute("Include")?.Value?.Trim()
               where PackageOrAssemblyNameMatches(packageName, targetPackageName, useWildcardSuffix)
               let packageVersion = TryParsePackageVersion(pvElement.Attribute("Version")?.Value?.Trim())
               where packageVersion is not null
               select new PackageReference(packageName, packageVersion);

        public string GetSearchClause(string basePackageName, bool useWildcardSuffix)
        {
            StringBuilder builder = new StringBuilder();

            // exact match
            builder.Append($"""
                            file:Directory.Packages.props AND ("<PackageVersion Include=\"{basePackageName}\""
                            """);

            if (useWildcardSuffix)
            {
                // wildcard match
                builder.Append(' ');
                builder.Append($"""
                                OR "<PackageVersion Include=\"{basePackageName}."
                                """);
            }

            builder.Append(')');
            return builder.ToString();
        }
    }
}
