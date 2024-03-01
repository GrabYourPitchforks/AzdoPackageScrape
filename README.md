# AzdoPackageScrape

Searches an entire Azure DevOps organization to discover versions of packages or assemblies used by projects within that org.

Usage: `dotnet run -c Release` from the command line.

When the command runs, it will prompt you for the path to your AzDO organization. For example, for the organization _contoso_, you could type `https://dev.azure.com/contoso/` or equivalent. Or you can specify just the org name: `contoso`.

If you put a path after the org name (e.g., `https://dev.azure.com/contoso/my-project/`), it will get stripped off. The entire org will be searched.

After typing the org name and authenticating to AzDO, you will be asked for the name of the package to search for. To specify an exact package name, like for package _Contoso.Foo_, specify `Contoso.Foo`. You can also specify `Contoso.Foo.*`, which will match _Contoso.Foo_, _Contoso.Foo.Bar_, etc.

Results will be saved to cwd as a series of `*.csv` files.

The `*-assemblies_all.csv` and `*-packages_all.csv` files will contain the full dump of matching assemblies and packages which were found in the org's code base, including the full paths of the files which contained the hits. The `*-assemblies_stats.csv` and `*-packages_stats.csv` files will contain only aggregate stats, such as the total count of each package version, without an exhaustive enumeration of each path that contained the reference.

Due to AzDO search limitations, this app will process an upper bound of only 10,000 matches.

### File search information

This app will search four places for package registrations.

* `packages.config`, used for netfx-style applications.
* `*.config`, used for netfx-style assembly binding redirects.
* `*.*proj`, used for SDK-style package registrations.
* `Directory.Packages.props`, used for central package management versioning.

This tool is likely to undercount, as it discards matches where exact version information cannot be extracted, e.g. `<package id="..." version="$(ExternallyProvidedProperty)" />`.
