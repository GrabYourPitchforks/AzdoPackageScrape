using System.Collections.Generic;
using System.Xml.Linq;

namespace AzdoPackageScrape.SearchPlugins
{
    public interface IAssemblyReferenceExtractor : ISearchPlugin
    {
        IEnumerable<AssemblyReference> ExtractAssemblyReferences(XDocument document, string targetAssemblyName, bool useWildcardSuffix);
    }
}
