namespace AzdoPackageScrape.SearchPlugins
{
    public interface ISearchPlugin
    {
        string GetSearchClause(string basePackageName, bool useWildcardSuffix);
    }
}
