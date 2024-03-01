using System;
using Pitchfork.SemanticVersioning;

namespace AzdoPackageScrape.SearchPlugins
{
    internal static class ExtractorUtil
    {
        // candidateName := name of assembly or package to check for match
        // targetName := name of assembly or package user specified to search for
        // useWildcardSuffix := whether to allow sub-packages or sub-assemblies to match
        public static bool PackageOrAssemblyNameMatches(string candidateName, string targetName, bool useWildcardSuffix)
        {
            if (!string.IsNullOrEmpty(candidateName)
                && candidateName.StartsWith(targetName, StringComparison.OrdinalIgnoreCase))
            {
                if (candidateName.Length == targetName.Length)
                {
                    return true; // exact match
                }

                if (useWildcardSuffix)
                {
                    if (candidateName.Length > targetName.Length
                        && candidateName[targetName.Length] == '.')
                    {
                        return true; // wildcard suffix match
                    }
                }
            }

            return false; // no match
        }

        // used for weeding out <package version="$(MsBuildVariable)" /> and other things we can't understand
        public static SemanticVersion TryParsePackageVersion(string versionString)
        {
            return SemanticVersion.TryParse(versionString, out var version) ? version : null;
        }
    }
}
