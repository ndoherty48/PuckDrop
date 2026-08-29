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
`PuckDrop.Web.Tests` (unit tests plus bUnit component tests) — 132 tests total, covered in
`docs/implementation-plan.md`'s Phase 7. Infrastructure integration tests (needs DynamoDB Local
via Docker) and API full-HTTP-pipeline tests are deliberate follow-ups, not yet built — also
detailed in Phase 7.

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
3. **`DevAuthenticationHandler`** (DEBUG builds only) if neither is configured — auto-authenticates
   every request as an admin `dev-user`. Never active in Release builds.

Only one of Cognito/Keycloak should be configured at a time — whichever resolves first wins.
Each provider branch also registers an `IClaimsTransformation`
(`CognitoClaimsTransformation` / `KeycloakClaimsTransformation`, in `Api/Auth/`) that normalizes
that provider's own admin signal (Cognito's `cognito:groups` claim, Keycloak's
`realm_access.roles`) into a standard `ClaimTypes.Role` "admin" claim — `DevAuthenticationHandler`
emits it directly. There is a single, shared `AdminPolicy` (`policy.RequireRole("admin")`) used by
every controller, so switching Cognito ↔ Keycloak is purely a config change, never a code change.
The Blazor UI mirrors this with `PuckDropClaimsPrincipalFactory` (`UI/src/PuckDrop.Web/Auth/`),
which normalizes the same two claim shapes client-side.

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
default). Provider-agnostic OIDC auth (config in `wwwroot/appsettings.json`'s `Oidc` section,
pointed at whichever of Cognito/Keycloak the API is currently using) is wired up in `Program.cs` —
see "Auth" above. Pages are split into
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
