using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;
using AzdoPackageScrape.SearchPlugins;

/*
 * The pipeline for this is as follows:
 * 
 * Query execution (running in a loop)
 * `-> Result processing (once per query execution)
 *     `-> File fetching (potentially many per result processing)
 *         `-> File processing (once per file fetch)
 *             `-> Write data into sink block (one per file fetch)
 *                 `-> Main thread is reading from this block
 * 
 * The JSON response format from the search service is:
 * {
 *   "count": 123,
 *   "results": [
 *     {
 *       "path": "/path/to/file",
 *       "project": {
 *         "name": "ProjectName"
 *       },
 *       "repository": {
 *         "name": "RepoName",
 *         "type": "git"
 *       },
 *       "versions": [
 *         {
 *           "branchName": "BranchName"
 *         }
 *       ]
 *     }
 *   ]
 */

namespace AzdoPackageScrape.DevOps
{
    internal static class AzdoCodeSearchPipeline
    {
        private static readonly ISearchPlugin[] _searchPlugins =
        [
            new BindingRedirectAssemblyReferenceExtractor(),
            new CentralPackageManagementPackageReferenceExtractor(),
            new PackagesConfigPackageReferenceExtractor(),
            new SdkProjPackageReferenceExtractor(),
        ];

        public static async Task<int> GetMatchCountAsync(QueryParameters query, CancellationToken cancellationToken)
        {
            Debug.Assert(query.MaxRecordsToReturn is > 0 and < 10000, "Caller should've validated this argument.");

            string queryText = GetQueryTerms(query.PackageName, query.UseWildcardSuffixMatching);

            // Ideally we'd pass "$top: 0" to fetch only the count and none of the results,
            // but AzDO requires us to specify a minimum value of 1. We'll ignore the records
            // returned by this query and focus only on the count field.

            var response = await query.Connection.SearchCodeAsync(queryText, skip: 0, top: 1, cancellationToken);
            int count = response.RootElement.GetProperty("count").GetInt32();

            if (count < 0)
            {
                throw new Exception("Invalid count returned.");
            }

            return count;
        }

        public static IAsyncEnumerable<AzdoPackageLookupResult> GetResultsAsync(QueryParameters query, CancellationToken cancellationToken)
        {
            Debug.Assert(query.MaxRecordsToReturn is > 0 and < 10000, "Caller should've validated this argument.");

            Pipeline pipeline = new Pipeline(query, cancellationToken);
            PreprocessRecords().ContinueWith(_ => pipeline.Complete());
            return pipeline.GetAsyncEnumerableResults();

            async Task PreprocessRecords()
            {
                // We can only ask for 1000 results from a single query,
                // so if we're asked for more than this, we'll need to run
                // the queries in 1000-result chunks.

                const int MaxAzdoRecordsPerQuery = 1000;
                int recordsRemainingToReturn = query.MaxRecordsToReturn;
                int skip = 0;

                string queryText = GetQueryTerms(query.PackageName, query.UseWildcardSuffixMatching);

                while (recordsRemainingToReturn > 0)
                {
                    int top = Math.Min(MaxAzdoRecordsPerQuery, recordsRemainingToReturn);
                    var document = await query.Connection.SearchCodeAsync(queryText, skip, top, cancellationToken);
                    var records = document.RootElement.GetProperty("results").Deserialize<Models.ResultRecord[]>();

                    if (records.Length == 0)
                    {
                        break; // we're out of records!
                    }

                    foreach (var record in records)
                    {
                        // we only care about git repositories
                        if (record.repository.type == "git" && record.versions.Any())
                        {
                            pipeline.Post(record);
                        }
                    }

                    recordsRemainingToReturn -= records.Length;
                    skip += records.Length;
                }
            }
        }

        private static string GetQueryTerms(string packageName, bool useWildcardSuffix)
        {
            return string.Join(" OR ", _searchPlugins.Select(p => $"({p.GetSearchClause(packageName, useWildcardSuffix)})"));
        }

        private static class Models
        {
            public record class ProjectRecord(string name);
            public record class RepositoryRecord(string name, string type);
            public record class VersionRecord(string branchName);
            public record class ResultRecord(string path, ProjectRecord project, RepositoryRecord repository, VersionRecord[] versions);
        }

        private sealed class Pipeline
        {
            private readonly ITargetBlock<Models.ResultRecord> _sourceBlock;
            private readonly IReceivableSourceBlock<AzdoPackageLookupResult> _sinkBlock;

            private readonly QueryParameters _query;
            private CancellationToken _cancellationToken; // non-primitive struct; don't make readonly

            public Pipeline(QueryParameters query, CancellationToken cancellationToken)
            {
                _query = query;
                _cancellationToken = cancellationToken;

                // We'll reuse these for all the different blocks.

                var defaultDataflowBlockOptions = new DataflowBlockOptions()
                {
                    CancellationToken = _cancellationToken,
                    EnsureOrdered = false,
                };

                var defaultDataflowLinkOptions = new DataflowLinkOptions()
                {
                    PropagateCompletion = true
                };

                var defaultExecutionDataflowBlockOptions = new ExecutionDataflowBlockOptions()
                {
                    CancellationToken = _cancellationToken,
                    EnsureOrdered = false,
                    MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded
                };

                var networkExecutionDataflowBlockOptions = new ExecutionDataflowBlockOptions()
                {
                    CancellationToken = defaultDataflowBlockOptions.CancellationToken,
                    EnsureOrdered = defaultDataflowBlockOptions.EnsureOrdered,
                    MaxDegreeOfParallelism = 8 // don't flood the network
                };

                // Source block is where records are posted.

                var resultRecordBufferBlock = new BufferBlock<Models.ResultRecord>();
                _sourceBlock = resultRecordBufferBlock;

                // Records are then filtered and then fetched from AzDO.

                var fetchFileTextFromAzdoTransformBlock = new TransformBlock<Models.ResultRecord, AzdoGitFileContents<string>>(
                    FetchRawFileTextFromAzdoAsync,
                    networkExecutionDataflowBlockOptions);

                resultRecordBufferBlock.LinkTo(
                    fetchFileTextFromAzdoTransformBlock,
                    defaultDataflowLinkOptions);

                // Then converted to XML.

                var convertRawContentsToXmlBlock = new TransformBlock<AzdoGitFileContents<string>, AzdoGitFileContents<XDocument>>(
                    ConvertRawAzdoResultBlobToXml,
                    defaultExecutionDataflowBlockOptions);

                fetchFileTextFromAzdoTransformBlock.LinkTo(
                    convertRawContentsToXmlBlock,
                    defaultDataflowLinkOptions);

                // And then the assembly & package versions are extracted from
                // the XML documents.

                var extractPackageVersionsBlock = new TransformBlock<AzdoGitFileContents<XDocument>, AzdoPackageLookupResult>(
                    ExtractPackageVersionsFromXml,
                    defaultExecutionDataflowBlockOptions);
                _sinkBlock = extractPackageVersionsBlock;

                convertRawContentsToXmlBlock.LinkTo(
                    extractPackageVersionsBlock,
                    defaultDataflowLinkOptions);
            }

            public void Complete()
            {
                _sourceBlock.Complete();
            }

            public void Post(Models.ResultRecord record)
            {
                Debug.Assert(record.repository.type == "git", "Caller should only pass us git repositories.");
                Debug.Assert(record.versions.Any(), "Caller should have confirmed a branch exists for this file.");
                _sourceBlock.Post(record);
            }

            public IAsyncEnumerable<AzdoPackageLookupResult> GetAsyncEnumerableResults()
            {
                return _sinkBlock.ReceiveAllAsync(_cancellationToken);
            }

            private async Task<AzdoGitFileContents<string>> FetchRawFileTextFromAzdoAsync(Models.ResultRecord resultRecord)
            {
                try
                {
                    // 20-second network timeout regardless of provided CancellationToken
                    var newCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
                    newCts.CancelAfter(TimeSpan.FromSeconds(20));

                    var fileReference = new AzdoGitFileReference(
                        Organization: _query.Connection.OrgName,
                        Project: resultRecord.project.name,
                        Repository: resultRecord.repository.name,
                        Path: resultRecord.path,
                        Branch: resultRecord.versions.First().branchName);

                    return new AzdoGitFileContents<string>(
                        fileReference,
                        await _query.Connection.FetchGitFileContentsAsTextAsync(fileReference, newCts.Token));
                }
                catch
                {
                    return null;
                }
            }

            private static AzdoGitFileContents<XDocument> ConvertRawAzdoResultBlobToXml(AzdoGitFileContents<string> fileContents)
            {
                if (fileContents is null) { return null; }
                try
                {
                    return fileContents.Transform(XDocument.Parse);
                }
                catch
                {
                    return null;
                }
            }

            private AzdoPackageLookupResult ExtractPackageVersionsFromXml(AzdoGitFileContents<XDocument> fileXml)
            {
                if (fileXml is null) { return null; }

                try
                {
                    return new AzdoPackageLookupResult(
                        fileXml.FileReference,
                        _searchPlugins
                            .OfType<IAssemblyReferenceExtractor>()
                            .SelectMany(plugin => plugin.ExtractAssemblyReferences(fileXml.Contents, _query.PackageName, _query.UseWildcardSuffixMatching))
                            .ToImmutableArray(),
                        _searchPlugins
                            .OfType<IPackageReferenceExtractor>()
                            .SelectMany(plugin => plugin.ExtractPackageReferences(fileXml.Contents, _query.PackageName, _query.UseWildcardSuffixMatching))
                            .ToImmutableArray());
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
