using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

using static AzdoPackageScrape.SearchPlugins.ExtractorUtil;

namespace AzdoPackageScrape.SearchPlugins
{
    // <packages>
    //   <package id="System.Foo" version="1.2.3" targetFramework="..." />
    // </packages>
    public class PackagesConfigPackageReferenceExtractor : IPackageReferenceExtractor
    {
        public IEnumerable<PackageReference> ExtractPackageReferences(XDocument document, string targetPackageName, bool useWildcardSuffix)
            => from package in document.Descendants("packages").Elements("package")
               let packageName = package.Attribute("id")?.Value?.Trim()
               where PackageOrAssemblyNameMatches(packageName, targetPackageName, useWildcardSuffix)
               let packageVersion = TryParsePackageVersion(package.Attribute("version")?.Value?.Trim())
               where packageVersion is not null
               select new PackageReference(packageName, packageVersion);

        public string GetSearchClause(string basePackageName, bool useWildcardSuffix)
        {
            StringBuilder builder = new StringBuilder();

            // exact match
            builder.Append($"""
                            file:packages.config AND ("<package id=\"{basePackageName}\""
                            """);

            if (useWildcardSuffix)
            {
                // wildcard match
                builder.Append(' ');
                builder.Append($"""
                                OR "<package id=\"{basePackageName}."
                                """);
            }

            builder.Append(')');
            return builder.ToString();
        }
    }
}
