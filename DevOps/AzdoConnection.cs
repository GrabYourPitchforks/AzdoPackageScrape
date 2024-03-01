using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Identity;
using Microsoft.VisualStudio.Services.WebApi;

namespace AzdoPackageScrape.DevOps
{
    // Helper class for performing requests against an AzDO connection.
    internal class AzdoConnection
    {
        private readonly VssConnection _connection;

        public AzdoConnection(string orgNameOrUrl)
        {
            string orgName = ExtractOrgName(orgNameOrUrl?.Trim());
            if (orgName is null)
            {
                throw new ArgumentException("Invalid AzDO URI or organization name.");
            }

            OrgName = orgName;
            BaseUri = new Uri($"https://dev.azure.com/{orgName}/");

            // this is why we target netfx: to allow cred prompting
            _connection = new VssConnection(BaseUri,
                credentials: new VssClientCredentials(
                    new WindowsCredential(),
                    new VssFederatedCredential(useCache: true),
                    CredentialPromptType.PromptIfNeeded));

            static string ExtractOrgName(string uriStringOrOrgName)
            {
                if (string.IsNullOrEmpty(uriStringOrOrgName)) { return null; }

                // Did they pass the org name directly?

                if (Regex.IsMatch(uriStringOrOrgName, @"^[a-zA-Z0-9-]+$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant))
                {
                    return uriStringOrOrgName;
                }

                // Did they specify https://dev.azure.com/org/... or https://org.visualstudio.com/...?

                Match match1 = Regex.Match(uriStringOrOrgName, @"^https://(?<orgName>[a-zA-Z0-9-]+)\.visualstudio\.com(/.*)?$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant);
                if (match1.Success && match1.Groups["orgName"].Success)
                {
                    return match1.Groups["orgName"].Value;
                }

                Match match2 = Regex.Match(uriStringOrOrgName, @"^https://dev.azure.com/(?<orgName>[a-zA-Z0-9-]+)(/.*)?$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant);
                if (match2.Success && match2.Groups["orgName"].Success)
                {
                    return match2.Groups["orgName"].Value;
                }

                // Unknown.

                return null;
            }
        }

        public string OrgName { get; private set; }
        public Uri BaseUri { get; private set; }

        public async Task<Identity> AuthenticateAsync(CancellationToken cancellationToken)
        {
            // this is why we target netfx: to allow cred prompting
            await _connection.ConnectAsync(cancellationToken);
            return _connection.AuthorizedIdentity;
        }

        public async Task<string> FetchGitFileContentsAsTextAsync(AzdoGitFileReference fileReference, CancellationToken cancellationToken = default)
        {
            GitHttpClient client = await _connection.GetClientAsync<GitHttpClient>(cancellationToken);

            Stream fileStream = await client.GetItemContentAsync(
                project: fileReference.Project,
                repositoryId: fileReference.Repository,
                path: fileReference.Path,
                resolveLfs: true,
                versionDescriptor: new GitVersionDescriptor()
                {
                    VersionType = GitVersionType.Branch,
                    Version = fileReference.Branch
                },
                cancellationToken: cancellationToken);

            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        public async Task<JsonDocument> SearchCodeAsync(string searchText, int skip, int top, CancellationToken cancellationToken)
        {
            if (skip is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(skip));
            }

            if (top is < 1 or > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(top));
            }

            // REST API docs for how to perform a code search:
            // https://learn.microsoft.com/rest/api/azure/devops/search/code-search-results/fetch-code-search-results

            Uri fullTextSearchUri = new Uri($"https://almsearch.dev.azure.com/{UrlEncode(OrgName)}/_apis/search/codesearchresults?api-version=7.1-preview.1");

            var body = new Dictionary<string, object>
            {
                ["searchText"] = searchText,
                ["$skip"] = skip,
                ["$top"] = top,
            };

            var json = JsonSerializer.Serialize(body);
            var request = new HttpRequestMessage(HttpMethod.Post, fullTextSearchUri)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Post the search query to AzDO and read results

            using var client = new HttpClient(_connection.InnerHandler, disposeHandler: false);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(), cancellationToken: cancellationToken);
        }
    }
}
