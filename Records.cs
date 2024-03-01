using System;
using System.Collections.Immutable;
using AzdoPackageScrape.DevOps;
using Pitchfork.SemanticVersioning;

namespace AzdoPackageScrape
{
    public record class AssemblyReference(
        string AssemblyName,
        Version AssemblyVersion);

    public record class AzdoResult(Uri AzdoFilePath,
        ImmutableArray<AssemblyReference> AssemblyReferences,
        ImmutableArray<PackageReference> PackageReferences);

    public record class PackageReference(
        string PackageName,
        SemanticVersion PackageVersion);

    public record class AzdoGitFileReference(
        string Organization,
        string Project,
        string Repository,
        string Path,
        string Branch)
    {
        public Uri ToFriendlyUrl()
        {
            // hxxps://dev.azure.com/org/project/_git/repo?path=/path/to/file&version=GBbranch
            return new Uri($"https://dev.azure.com/{UrlEncode(Organization)}/{UrlEncode(Project)}/_git/{UrlEncode(Repository)}?path={QueryEncode(Path)}&version=GB{QueryEncode(Branch)}");
        }
    }

    public record class AzdoGitFileContents<TDocument>(
        AzdoGitFileReference FileReference,
        TDocument Contents)
    {
        public AzdoGitFileContents<TResultDocument> Transform<TResultDocument>(Converter<TDocument, TResultDocument> transform)
            => new AzdoGitFileContents<TResultDocument>(FileReference, transform(Contents));
    }

    public record class AzdoPackageLookupResult(
        AzdoGitFileReference FileReference,
        ImmutableArray<AssemblyReference> AssemblyReferences,
        ImmutableArray<PackageReference> PackageReferences)
        : AzdoGitFileContents<(ImmutableArray<AssemblyReference> AssemblyReferences, ImmutableArray<PackageReference> PackageReferences)>(
            FileReference,
            (AssemblyReferences, PackageReferences));

    internal record class QueryParameters(
        AzdoConnection Connection,
        string PackageName,
        bool UseWildcardSuffixMatching,
        int MaxRecordsToReturn);
}
