# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

PuckDrop is an open-source game day poll app for friend groups (EIHL ice hockey): admins create
multiple-choice predictions per game day, friends submit picks before puck drop, admins score
correct answers post-game, and a season leaderboard tracks results. Stack: Blazor WebAssembly
frontend, ASP.NET Core on AWS Lambda backend, DynamoDB (single-table), Cognito/Keycloak auth,
AWS CDK + .NET Aspire for infra/orchestration.

## Commands

Requires .NET 10 SDK (see `global.json`) and the Aspire CLI.

```bash
aspire start                      # Run everything locally: Aspire dashboard, Lambda emulator,
                                   # API Gateway emulator, DynamoDB Local, Keycloak, Blazor WASM
dotnet build                      # Build the whole solution (PuckDrop.slnx)
dotnet build <path>.csproj        # Build a single project
dotnet test PuckDrop.slnx         # Run all tests (xUnit v3 on Microsoft.Testing.Platform,
                                   # per global.json's test runner setting — not VSTest)
```

`API/tests/` and `UI/tests/` hold four xUnit v3 test projects — `PuckDrop.Domain.Tests`,
`PuckDrop.Application.Tests`, `PuckDrop.Api.Tests` (all pure/fast, no external dependencies), and
`PuckDrop.Web.Tests` (unit tests plus bUnit component tests) — 139 tests total, covered in
`docs/implementation-plan.md`'s Phase 7.

A fifth project, `tests/PuckDrop.E2ETests/` (repo-root `tests/`, not under `API/`/`UI/` — it's
genuinely cross-cutting), supersedes what Phase 7 originally deferred (separate infrastructure and
API-pipeline test layers): it boots the real AppHost via `Aspire.Hosting.Testing` and drives a real
headless Chromium browser via Playwright against it — real DynamoDB Local, real Keycloak, the real
Lambda-hosted API, the real Blazor WASM app, nothing mocked. Needs Docker (same as `aspire start`)
plus a one-time `pwsh tests/PuckDrop.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium`
after first build, to fetch Playwright's browser binary. Meaningfully slower than the four fast
projects above (real container boots, real cold WASM loads, real AWS Lambda Service Emulator round
trips) — for routine local iteration, run the fast subset (`dotnet test` against each of the four
projects above individually — `dotnet test PuckDrop.slnx` runs everything, this project included,
so reach for that before a PR or whenever DynamoDB/API-contract/auth/UI logic actually changed, not
for every edit). One known, accepted source of flakiness: the AWS Lambda Service Emulator processes
one invocation at a time, so a slow run can occasionally time out a step even with the retry
helpers this suite already has in code — no further fix available (checked: no concurrency option
on `lambda-test-tool` or in `Aspire.Hosting.AWS`).

Carries `Microsoft.Testing.Extensions.Retry` for exactly that residual flakiness — it reruns a
failed test in a whole fresh process (a full AppHost reboot for this project, so keep the retry
count low). Confirmed for real: a deliberately-broken assertion genuinely got retried and still
correctly failed at the end (proving the mechanism itself works), but two separate live runs with
it enabled both happened to pass outright with no retry triggered — the flakiness is real but
infrequent enough that it wasn't caught in the act. Run the suite as:
`dotnet test tests/PuckDrop.E2ETests/PuckDrop.E2ETests.csproj --retry-failed-tests 1
--retry-failed-tests-delay 5s`.

There is no linter/formatter config (`.editorconfig`) and no CI workflow in this repo currently.

## Architecture

### API: layered, Clean-Architecture-style, one project per layer

- **PuckDrop.Domain** — entities (`Season`, `GameDayPoll`, `Question`, `Option`, `UserAnswer`,
  `LeaderboardEntry`) and enums (`PollStatus`). No dependencies on other layers.
- **PuckDrop.Application** — use-case services (`SeasonService`, `PollService`, `AnswerService`,
  `ScoringService`, `ResultsService`, `LeaderboardService`), repository *interfaces*
  (`ISeasonRepository`, `IPollRepository`, `IUserAnswerRepository`, `ILeaderboardRepository`),
  and the `IUserProfileService` abstraction. Depends only on Domain.
- **PuckDrop.Infrastructure** — DynamoDB repository implementations, `DynamoDbItem` models per
  entity, and `DynamoDbMapper` (domain entity ↔ DynamoDB item). Implements the Application
  interfaces.
- **PuckDrop.Api** — ASP.NET Core controllers, request/response contracts (`ApiContracts.cs`),
  mapping extensions (contract ↔ domain, one file per entity under `Mappings/`), auth setup, and
  the Lambda entry point (`LambdaEntryPoint.cs`, via `Amazon.Lambda.AspNetCoreServer`). References
  Application and Infrastructure and wires DI via `ApiServiceCollectionExtensions.AddApis`/`UseApis`.

Dependency direction: Api → Infrastructure → Application → Domain. Repository interfaces live in
Application; only Infrastructure knows about DynamoDB.

### DynamoDB: single-table design

Everything lives in one `PuckDrop` table (`PK`/`SK`, plus GSI1 and GSI2) — see
`docs/dynamodb-design.md` for the full access-pattern-to-key mapping and
`API/src/PuckDrop.Infrastructure/DynamoDb/DynamoDbKeys.cs` for the canonical key-building
functions (e.g. `POLL#{pollId}`, `SEASON#{seasonId}#STATUS#{status}`, inverted zero-padded score
keys for leaderboard sort order). Item shape and key layout must stay in sync between that doc,
`DynamoDbKeys.cs`, and `DynamoDb/Items/*Item.cs` — when changing an access pattern, update all
three plus `DynamoDbMapper.cs`.

### Auth: dual-provider, resolved at startup, normalized to provider-agnostic claims

`ApiServiceCollectionExtensions.AddApis` picks an auth scheme by inspecting config at startup, in
this order:
1. **Cognito** (`Cognito` section) if `CognitoSettings` is bound and `UserPoolId` isn't a
   placeholder — JWT bearer against the Cognito issuer.
2. **Keycloak** (`Keycloak` section) if `KeycloakSettings` is bound and `Realm` isn't a
   placeholder — JWT bearer against the Keycloak realm issuer.
3. **Neither resolves** — throws `InvalidOperationException` at startup rather than coming up
   with broken/no auth. (There used to be a third, DEBUG-only `DevAuthenticationHandler` fallback
   that auto-authenticated every request as an admin; removed deliberately — a shipped
   auto-admin-bypass wasn't worth carrying forward, even DEBUG-gated.)

Only one of Cognito/Keycloak should be configured at a time — whichever resolves first wins.
Each provider branch also registers an `IClaimsTransformation`
(`CognitoClaimsTransformation` / `KeycloakClaimsTransformation`, in `Api/Auth/`) that normalizes
that provider's own admin signal (Cognito's `cognito:groups` claim, Keycloak's
`realm_access.roles`) into a standard `ClaimTypes.Role` "admin" claim. There is a single, shared
`AdminPolicy` (`policy.RequireRole("admin")`) used by every controller, so switching Cognito ↔
Keycloak is purely a config change, never a code change. The Blazor UI mirrors this with
`PuckDropClaimsPrincipalFactory` (`UI/src/PuckDrop.Web/Auth/`), which normalizes the same two
claim shapes client-side.

Each branch also registers an `AuthDiscoveryOptions` (`Api/Auth/`) with the active provider's
`Authority`/`ClientId`/`ResponseType`, served unauthenticated via `GET /auth-config`
(`AuthConfigController`) so the Blazor UI can fetch its OIDC config at boot instead of it being
baked into `wwwroot/appsettings.json` at build time — there's no way to inject CDK-provisioned
values into a static WASM bundle the way the Lambda's env vars are injected at publish time (see
`AppHost.cs`'s `ConstructFunctionCallback`). Cognito's `AuthDiscoveryOptions.ClientId` reuses
`CognitoSettings.ClientId` (one `UserPoolClient` serves both the API's audience validation and
the browser's login); Keycloak's uses the separate `KeycloakSettings.UiClientId`
("PuckDrop-UI"), since Keycloak's own `ClientId` ("PuckDrop-API") is audience-validation-only and
isn't the client the browser logs in with. In production this endpoint needs its own API Gateway
route with `AuthorizationType = "NONE"` (`DeploymentStack.cs`), since the catch-all Lambda route
otherwise requires a JWT for every path under `/puckdrop/`.

Cognito is the production IdP (provisioned by CDK in `DeploymentStack`); Keycloak is used for
local dev via Aspire (`AddKeycloak` + realm import from
`Infrastructure/PuckDrop.AppHost/Keycloak/PuckDrop-realm.json`).

### Infrastructure orchestration (Aspire) vs. deployment (CDK)

- **`Infrastructure/PuckDrop.AppHost/AppHost.cs`** is the Aspire app model: it wires DynamoDB
  Local, Keycloak (dev-only IdP), the Lambda function (via `AddAWSLambdaFunction`), the API
  Gateway HTTP API emulator, and the Blazor WASM app together for `aspire start`. It also declares
  the AWS CDK environment used for real deployment.
- **`Infrastructure/PuckDrop.AppHost/AWS/DeploymentStack.cs`** is the CDK stack used when
  publishing: creates the DynamoDB table + GSIs, the Cognito user pool/client/admin group, and an
  HTTP API Gateway with a JWT authorizer wired to the Lambda. The `ConstructFunctionCallback` in
  `AppHost.cs` (`PublishAsLambdaFunction`) is where CDK-provisioned values (user pool ID, table
  grants, API Gateway route) get pushed into the Lambda's environment/permissions at publish time
  — this is the seam between the two systems.
- `PuckDrop.ServiceDefaults` / `PuckDrop.ClientServiceDefaults` hold shared OpenTelemetry/service
  discovery wiring for the server and Blazor WASM client respectively, added via
  `AddLambdaServiceDefaults()` / `AddBlazorClientServiceDefaults()`.

### UI

Blazor WebAssembly app (`UI/src/PuckDrop.Web`). `PuckDropApiClient` (in `Services/`) is the sole
HTTP client to the API, pointed at the API Gateway emulator's base URL (resolved from Aspire
service discovery config, falling back to `ApiClientSettings:BaseUrl`, then a hardcoded local
default). Provider-agnostic OIDC auth is wired up in `Program.cs`, which fetches its config from
the API's `GET /auth-config` at boot rather than a static file — see "Auth" above. If that fetch
fails, `Program.cs` registers an `AuthConfigLoadResult` that `App.razor` checks before rendering
its normal `<Router>`/`<CascadingAuthenticationState>` tree, showing a clear error instead of a
blank page. Pages are split into
top-level (`Home`, `Poll`, `Leaderboard`, `History`, `Results`) and `Pages/Admin/*`
(`CreatePoll`, `EditPoll`, `Polls`, `ScorePoll`) for the create/score workflow.

### Package versions

Centrally managed via `Directory.Packages.props` (`ManagePackageVersionsCentrally`) — add new
package versions there, not in individual `.csproj` files. `Directory.Build.props` sets the
shared `TargetFramework` (`net10.0`), nullable, and implicit usings for every project.

## Design docs

`docs/` has more detail than is summarized above when you need it:
- `docs/domain-model.md` — entity field reference and `PollStatus` transitions
- `docs/dynamodb-design.md` — full access pattern → key design table
- `docs/api-design.md` — API endpoint design
- `docs/ui-pages.md` — page-by-page UI spec
- `docs/implementation-plan.md` — build sequencing
- `docs/multi-tenancy.md` — not planned; design sketch for adding multi-group/league support
  if it's ever needed
- `docs/load-stress-smoke-testing.md` — planned, not started; load/stress/smoke testing against a
  real deployed environment (never the local Aspire stack)
