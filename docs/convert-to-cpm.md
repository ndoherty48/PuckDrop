# NuGet Central Package Management (CPM) Conversion Report

## 1. Conversion Overview

| Item | Detail |
|------|--------|
| Scope | WKPA repository (WKPA.slnx) |
| Projects converted | 4 of 7 (3 projects have no package references) |
| Packages centralized | 18 |
| Projects skipped | 0 |
| Packages skipped | 0 |
| MSBuild properties inlined/removed | 0 (none existed) |
| VersionOverride usage | 0 |

**Projects with packages converted:**
- `API/src/WKPA.Api/WKPA.Api.csproj` (1 package)
- `API/src/WKPA.Application/WKPA.Application.csproj` (2 packages)
- `Infrastructure/WKPA.AppHost/WKPA.AppHost.csproj` (4 packages)
- `Infrastructure/WKPA.ServiceDefaults/WKPA.ServiceDefaults.csproj` (9 packages)
- `UI/src/WKPA.Web/WKPA.Web.csproj` (2 packages)

**Additional fixes applied during conversion:**
- Fixed malformed XML in `WKPA.slnx` (`</Infrastructure>` → `</Folder>`)
- Fixed incorrect relative project reference paths in `WKPA.AppHost.csproj` (`..` → `../..`)

## 2. Version Conflict Resolutions

No version conflicts were found. All 18 packages had consistent versions across projects — each package is referenced by exactly one project.

## 3. Package Comparison — Baseline vs. Result

The conversion is **fully version-neutral**. `baseline-packages.json` and `after-cpm-packages.json` are byte-for-byte identical.

### Changes table

No changes. All packages resolve to the same versions as baseline.

### Unchanged table

| Package | Version | Project |
|---------|---------|---------|
| Amazon.Lambda.AspNetCoreServer | 9.2.1 | WKPA.Api |
| Aspire.Hosting.AWS | 13.2.0 | WKPA.AppHost |
| Aspire.Hosting.Blazor | 13.4.2-preview.1.26303.6 | WKPA.AppHost |
| Aspire.Hosting.Browsers | 13.4.2-preview.1.26303.6 | WKPA.AppHost |
| Aspire.Hosting.DevTunnels | 13.4.2 | WKPA.AppHost |
| Microsoft.AspNetCore.Components.WebAssembly | 10.0.5 | WKPA.Web |
| Microsoft.AspNetCore.Components.WebAssembly.DevServer | 10.0.5 | WKPA.Web |
| Microsoft.Extensions.Configuration | 10.0.8 | WKPA.Application |
| Microsoft.Extensions.DependencyInjection | 10.0.8 | WKPA.Application |
| Microsoft.Extensions.Http.Resilience | 10.5.0 | WKPA.ServiceDefaults |
| Microsoft.Extensions.ServiceDiscovery | 10.5.0 | WKPA.ServiceDefaults |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.15.3 | WKPA.ServiceDefaults |
| OpenTelemetry.Extensions.Hosting | 1.15.3 | WKPA.ServiceDefaults |
| OpenTelemetry.Instrumentation.AspNetCore | 1.15.2 | WKPA.ServiceDefaults |
| OpenTelemetry.Instrumentation.AWS | 1.15.0 | WKPA.ServiceDefaults |
| OpenTelemetry.Instrumentation.AWSLambda | 1.15.0 | WKPA.ServiceDefaults |
| OpenTelemetry.Instrumentation.Http | 1.15.1 | WKPA.ServiceDefaults |
| OpenTelemetry.Instrumentation.Runtime | 1.15.1 | WKPA.ServiceDefaults |

## 4. Risk Assessment

**[Low risk]** — Conversion is version-neutral; all packages resolve to the same versions as baseline. The build and restore succeeded with zero errors. The only warning (ASPIRE004) is pre-existing and unrelated to the CPM conversion. Recommend running `dotnet test` as a final check once tests are added.

## 5. Follow-up Items

1. **Run tests:** No test projects exist yet. When tests are added, run `dotnet test` to validate runtime behavior.
2. **Pre-existing warning:** The ASPIRE004 warning indicates `WKPA.Api` is referenced by the Aspire AppHost but is not an executable. Consider setting `IsAspireProjectResource="false"` on that project reference or changing the project's OutputType.
3. **Aspire.Hosting.Blazor/Browsers preview packages:** These use a preview version (`13.4.2-preview.1.26303.6`). Consider upgrading to stable releases when available.

## 6. Artifacts and How to Use Them

| Artifact | Purpose |
|----------|---------|
| `convert-to-cpm.md` | This report file. Suitable for use as a pull request description or team review artifact. |
| `Directory.Packages.props` | The central package version file. All future version changes should be made here. |

**Recommended next steps:**
- Run `dotnet test` to validate runtime behavior (once test projects exist)
- To add a new package: add a `<PackageVersion>` entry in `Directory.Packages.props` and a `<PackageReference Include="..."/>` (without Version) in the project
- To update a package version: change only the `Version` attribute in `Directory.Packages.props`
