# Publishing to NuGet

The bindings are published to nuget.org as `EdiFabric.Native.X12`: one package with the managed
assembly for .NET 8+ and the ediFabric Native library for every supported platform, plus a
`.snupkg` symbols package.

| Package path | Platform |
| --- | --- |
| `lib/net8.0/EdiFabric.Native.X12.dll` | the bindings |
| `runtimes/win-x64/native/edifabric-x12-tools.dll` | Windows x64 |
| `runtimes/linux-x64/native/edifabric-x12-tools.so` | Linux x64, glibc 2.34+ |
| `runtimes/linux-arm64/native/edifabric-x12-tools.so` | Linux arm64, glibc 2.34+ |
| `runtimes/osx-arm64/native/edifabric-x12-tools.dylib` | macOS 12+ on Apple silicon |

.NET copies the matching `runtimes` library into the consumer's build output, and the
bindings' `DllImportResolver` loads it from there. The libraries come from the public downloads at
`https://www.edifabric.com/download/`, so publish a release only after the native build for that
version is uploaded there.

| File | Purpose |
| --- | --- |
| `EdiFabric.Native.X12/EdiFabric.Native.X12.csproj` | version, package metadata, and the four `runtimes` entries; `dotnet pack` fails if a library is missing |
| `scripts/fetch-natives.ps1`, `.sh` | download the four libraries into `natives/<rid>/` |
| `scripts/smoke-test/` | a console app that installs the package from `dist/` and loads the bundled library |
| `scripts/release.ps1`, `.sh` | fetch, pack, smoke-test, and push from your machine |
| `.github/workflows/publish-nuget.yml` | the same pipeline in GitHub Actions, with a smoke test on every platform |

## One-time setup

1. Sign in to [nuget.org](https://www.nuget.org) with the account that owns the existing EdiFabric
   packages (`EdiFabric`, `EdiFabric.Api`). The `EdiFabric` prefix is reserved for it, so only
   that account (or an organization it belongs to) can push `EdiFabric.Native.X12`.
2. Add a trusted publishing policy: click the account name, **Trusted Publishing > Create**, with
   package owner `EdiFabric`, repository owner `EdiFabric`, repository
   `edifabric-csharp-bindings`, workflow file `publish-nuget.yml` (the file name only), and
   environment `nuget`. The policy covers every package the owner has, including ones not yet
   pushed, so the first release needs no API key.
3. GitHub: create the environment `nuget` (**Settings > Environments**), optionally with required
   reviewers, and add the environment secret `NUGET_USER` with the nuget.org profile name of the
   account from step 1 (the user name, not the email address).
4. For pushing from your machine only: create an API key (**API Keys > Create**, scope **Push new
   packages and package versions**, glob `EdiFabric.Native.X12`) and set it in `NUGET_API_KEY`.

## Release

1. Upload the new native build to `https://www.edifabric.com/download/`.
2. Set `<Version>` in `EdiFabric.Native.X12/EdiFabric.Native.X12.csproj` to the native library
   version, for example `1.1.1`, and commit.
3. Validate the package:
   - GitHub: **Actions > Publish to NuGet > Run workflow** with **publish** cleared
   - or locally: `./scripts/release.ps1 -DryRun` (Windows) or `scripts/release.sh --dry-run`
4. Create a GitHub release with the tag `v<version>`, for example `v1.1.1`. Publishing the release
   runs the workflow and pushes the package; the workflow fails if the tag and the version differ.
   Locally, the equivalent is `./scripts/release.ps1` or `scripts/release.sh`.
5. nuget.org validates and indexes the package, which usually takes 5 to 15 minutes; until then
   it shows on the package page as not yet listed. Then `dotnet add package EdiFabric.Native.X12`
   works everywhere.

nuget.org never accepts the same version twice, even after you delete it, and the push uses
`--skip-duplicate`, so rerunning a release only pushes what is missing. A bad version can be
unlisted (**Manage package > Listing**), which hides it from search and new installs but keeps it
available to projects that already reference it.
