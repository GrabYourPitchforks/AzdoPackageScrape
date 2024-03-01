using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

using static AzdoPackageScrape.SearchPlugins.ExtractorUtil;

namespace AzdoPackageScrape.SearchPlugins
{
    // <runtime>
    //   <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
    //     <dependentAssembly>
    //       <assemblyIdentity name="System.Foo" publicKeyToken="..." culture="..." />
    //       <bindingRedirect oldVersion="0.0.0.0-1.2.3.0" newVersion="1.2.3.0" />
    //     </dependentAssembly>
    //   </assemblyBinding>
    // </runtime>
    public class BindingRedirectAssemblyReferenceExtractor : IAssemblyReferenceExtractor
    {
        private static readonly XNamespace _baseNamespace = XNamespace.Get("urn:schemas-microsoft-com:asm.v1");
        private static readonly XName _assemblyBindingElementName = _baseNamespace.GetName("assemblyBinding");
        private static readonly XName _dependentAssemblyElementName = _baseNamespace.GetName("dependentAssembly");
        private static readonly XName _assemblyIdentityElementName = _baseNamespace.GetName("assemblyIdentity");
        private static readonly XName _bindingRedirectElementName = _baseNamespace.GetName("bindingRedirect");

        public IEnumerable<AssemblyReference> ExtractAssemblyReferences(XDocument document, string targetAssemblyName, bool useWildcardSuffix)
            => from dependentAssembly in document.Descendants(_assemblyBindingElementName).Elements(_dependentAssemblyElementName)
               let assemblyName = dependentAssembly.Element(_assemblyIdentityElementName)?.Attribute("name")?.Value?.Trim()
               where PackageOrAssemblyNameMatches(assemblyName, targetAssemblyName, useWildcardSuffix)
               let assemblyVersion = TryParseVersion(dependentAssembly.Element(_bindingRedirectElementName)?.Attribute("newVersion")?.Value?.Trim())
               where assemblyVersion is not null
               select new AssemblyReference(assemblyName, assemblyVersion);

        public string GetSearchClause(string basePackageName, bool useWildcardSuffix)
        {
            StringBuilder builder = new StringBuilder();

            // exact match
            builder.Append($"""
                            ext:config AND ("<assemblyIdentity name=\"{basePackageName}\""
                            """);

            if (useWildcardSuffix)
            {
                // wildcard match
                builder.Append(' ');
                builder.Append($"""
                                OR "<assemblyIdentity name=\"{basePackageName}."
                                """);
            }

            builder.Append(')');
            return builder.ToString();
        }

        private static Version TryParseVersion(string versionString)
        {
            return Version.TryParse(versionString, out var version) ? version : null;
        }
    }
}
