using System.Collections.Generic;
using System.Xml.Linq;

namespace AzdoPackageScrape.SearchPlugins
{
    public interface IPackageReferenceExtractor : ISearchPlugin
    {
        IEnumerable<PackageReference> ExtractPackageReferences(XDocument document, string targetPackageName, bool useWildcardSuffix);
    }
}
