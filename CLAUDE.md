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
`PuckDrop.Web.Tests` (unit tests plus bUnit component tests) — 317 tests total, covered in
`docs/implementation-plan.md`'s Phase 7.

A fifth project, `tests/PuckDrop.E2ETests/` (repo-root `tests/`, not under `API/`/`UI/` — it's
genuinely cross-cutting), supersedes what Phase 7 originally deferred (separate infrastructure and
API-pipeline test layers): it boots the real AppHost via `Aspire.Hosting.Testing` and drives a real
headless Chromium browser via Playwright against it — real DynamoDB Local, real Keycloak, the real
Lambda-hosted API, the real Blazor WASM app, nothing mocked. Needs a container runtime (same as `aspire start` - Docker or
Podman), the `Amazon.Lambda.TestTool` global tool (`dotnet tool install --global
Amazon.Lambda.TestTool`), which `AddAWSLambdaFunction` shells out to and which also provides the
API Gateway emulator, plus a one-time
`pwsh tests/PuckDrop.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium` after first build
to fetch Playwright's browser binary. All three are easy to forget because a dev machine acquires
them once: a fresh CI runner has none of them, and missing the Lambda tool shows up as `api-gateway`
never becoming healthy rather than as anything mentioning Lambda. Meaningfully slower than the four fast
projects above (real container boots, real cold WASM loads, real AWS Lambda Service Emulator round
trips) — for routine local iteration, run the fast subset (`dotnet test` against each of the four
projects above individually — `dotnet test PuckDrop.slnx` runs everything, this project included,
so reach for that before a PR or whenever DynamoDB/API-contract/auth/UI logic actually changed, not
for every edit). `Browser/ScoringCorrectionsTests` is the one that covers re-scoring, voiding/restoring a game day
and point deductions. It matters at this layer specifically: season totals are folded from facts
sharing a `U#{userId}#` sort-key prefix, so a wrong key or a missing attribute only shows up against
DynamoDB itself, and the undo paths rely on conditional deletes, which is DynamoDB-side behaviour.
Its assertions are relative to the friend's total when it starts, because the leaderboard is per
season and other tests in the same run add to it.

One known, accepted source of flakiness: the AWS Lambda Service Emulator processes
one invocation at a time, so a slow run can occasionally time out a step even with the retry
helpers this suite already has in code — no further fix available (checked: no concurrency option
on `lambda-test-tool` or in `Aspire.Hosting.AWS`).

Carries `Microsoft.Testing.Extensions.Retry` for exactly that residual flakiness — it reruns a
failed test in a whole fresh process (a full AppHost reboot for this project, so keep the retry
count low). Confirmed for real, twice over: a deliberately-broken assertion got retried and still
correctly failed at the end (proving the mechanism works), and a full-suite run has since been
caught in the act — `PollLifecycleTests` and `ScoringCorrectionsTests` both failed on try 1 with
`App still showing "Couldn't reach the server" after 4 attempts` and passed on try 2, for a green
`12 (+2 retried)`. So the retry is load-bearing on a full run, not just insurance: expect the two
poll-scoring tests to be the ones that need it, and treat a run that needs *no* retry as luck
rather than the norm. Run the suite as:
`dotnet test tests/PuckDrop.E2ETests/PuckDrop.E2ETests.csproj --retry-failed-tests 1
--retry-failed-tests-delay 5s`.

There is no linter/formatter config (`.editorconfig`) and no CI workflow in this repo currently.

## Architecture

### API: layered, Clean-Architecture-style, one project per layer

- **PuckDrop.Domain** — entities (`Season`, `GameDayPoll`, `Question`, `Option`, `UserAnswer`,
  `PollScore`, `PollVoid`, `PointAdjustment`, `LeaderboardEntry`), enums (`PollStatus`), and
  `Standings/SeasonStandings` — the pure fold that derives a season's standings from its scoring
  facts. No dependencies on other layers.
- **PuckDrop.Application** — use-case services (`SeasonService`, `PollService`, `AnswerService`,
  `ScoringService`, `ResultsService`, `LeaderboardService`, `PollVoidService`,
  `PointAdjustmentService`), repository *interfaces*
  (`ISeasonRepository`, `IPollRepository`, `IUserAnswerRepository`, `ILeaderboardRepository`),
  and the `IUserProfileService` abstraction (display-name resolution, implemented in PuckDrop.Api
  as `Auth/UserProfileService`). Depends only on Domain.
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
functions (e.g. `POLL#{pollId}`, `SEASON#{seasonId}#STATUS#{status}`, `U#{userId}#POLL#{pollId}`).
Item shape and key layout must stay in sync between that doc, `DynamoDbKeys.cs`, and
`DynamoDb/Items/*Item.cs` — when changing an access pattern, update all three plus
`DynamoDbMapper.cs`.

### Leaderboard: derived, not accumulated

There is **no stored leaderboard total**. A season's standings are folded on read by
`SeasonStandings.Build` from three fact types, all in the `LEADERBOARD#{seasonId}` partition under a
shared `U#{userId}#` prefix (so one query covers a player, or the whole season):
`U#{userId}#POLL#{pollId}` (what they earned in a poll), `U#{userId}#VOID#{pollId}` (an admin voiding
that game day) and `U#{userId}#ADJ#{adjustmentId}` (a signed point adjustment).

This is load-bearing, not incidental. Because totals are derived, **re-scoring a poll is the same
operation as scoring it** (`GameDayPoll.MarkScored` permits `Scored → Scored`; the facts are simply
overwritten), and voiding or deducting is one write that can be undone by one conditional delete with
the total returning to exactly what it was. Nothing may incrementally adjust a total anywhere — if a
write path ever "just adds" the new poll's points, order-independence dies silently. There is no
`AddPollResults`, and the old inverted-score sort key (`SCORE#{999999 - points}#{userId}`) is gone: it
could not express a subtraction, and a negative total sorted *first* rather than last.

Voids are kept separate from score facts on purpose, so a re-score can't clobber one. Deductions may
take a total below zero, which is displayed and ranked as-is. Accuracy is always measured on earned
points, never the effective total.

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

Logout is the one place the UI can't stay fully provider-agnostic: Cognito's `/logout` ignores
the standard OIDC `id_token_hint`/`post_logout_redirect_uri` and requires `client_id` plus
`logout_uri` (otherwise it bounces to `/login`). The Cognito branch sets
`AuthDiscoveryOptions.UseCognitoLogout`, and `MainLayout.Logout` then adds those two parameters to
the logout request only, targeting `authentication/logged-out` — which must stay in the app
client's allowed sign-out URLs (`BlazorStaticSitePublishTarget.FixCognitoCallbackUrls`).

Display names are the other Cognito quirk: Cognito access tokens (all the API receives) carry no
`name`/`preferred_username`/`email` claims, so on answer submission `UserProfileService`
(`Api/Auth/`, behind Application's `IUserProfileService`) falls back to the OIDC `userInfo`
endpoint — found via the JWT handler's cached discovery document — using the caller's own access
token. Keycloak access tokens already carry `preferred_username`, so that path makes no call. The
UI's `PuckDropClaimsPrincipalFactory` applies the same `name` → `preferred_username` → `email`
precedence to the header name.

Cognito is the production IdP (provisioned by CDK in `DeploymentStack`); Keycloak is used for
local dev via Aspire (`AddKeycloak` + realm import from
`Infrastructure/PuckDrop.AppHost/Keycloak/PuckDrop-realm.json`).

The Cognito user pool construct is `PuckDropUserPoolV2` because several of its settings are
immutable once a pool exists — case sensitivity, the sign-in attributes, and which standard
attributes are required and mutable. CloudFormation reports `UsernameConfiguration` as "no
interruption" and `UpdateUserPool` has no parameter for it, so changing any of these in place
deploys green and silently does nothing; only a new pool (new construct ID) applies them. Both
pools carry `RemovalPolicy.RETAIN`, so V1 is orphaned rather than deleted. Consequences of a
swap: every `sub` changes, and `sub` is the key behind `UserAnswerPK`/`UserAnswerGSI1SK` and
every scoring fact under `UserFactSKPrefixFor` (`PollScoreSK`/`PollVoidSK`/`PointAdjustmentSK`),
so picks and standings both need remapping; and the domain prefix (globally unique across
all AWS accounts) gains a `v2-` segment because CloudFormation creates the new domain before
deleting the retained one — droppable in a later deploy once V1 is gone, and invisible to the UI,
which resolves login endpoints from OIDC discovery off the issuer rather than the domain.

Managed login branding is deliberately two-stage (`CreateManagedLoginBranding`). Cognito publishes
no schema for the branding settings document and silently drops keys it doesn't recognise, so the
only reliable source is a describe against a live style. Until
`AWS/Branding/branding-settings.json` exists the stack deploys `UseCognitoProvidedValues` — a
CloudFormation-created client with no style at all shows "Login pages unavailable" — and once it's
committed the stack switches to that document plus the logos in `AWS/Branding/` (settings, assets
and `UseCognitoProvidedValues` are mutually exclusive). The file is committed, so the fallback is
now only insurance. Refresh it with the describe below, then re-run `apply-design-tokens.py`,
which maps app.css's tokens onto the light-mode colours and is idempotent — the describe returns
Cognito's defaults for anything not overridden, so the two steps always go together:

```bash
aws cognito-idp describe-managed-login-branding-by-client --user-pool-id <pool> \
  --client-id <client> --return-merged-resources \
  --query 'ManagedLoginBranding.Settings' > Infrastructure/PuckDrop.AppHost/AWS/Branding/branding-settings.json
```

The logos are generated, not hand-drawn, and the generator is deliberately not committed (it's
the repo's only Python dependency and a once-in-a-logo-change tool). To redo them:
`puckdrop-lockup-{ink,frost}.svg` are `wwwroot/logo.svg`'s 32x32 mark scaled 1.25x at the left,
plus a "PuckDrop" wordmark whose letterforms are outlined from
`wwwroot/fonts/barlow-condensed-700.woff2` with fontTools (`SVGPathPen` through a `TransformPen`
per glyph, advancing by each glyph's width) at a 26px cap height on a baseline of y=34, starting
at x=52, in a 181x48 viewBox. Outlines rather than a `<text>` element because managed login has no
font asset category, so Barlow Condensed wouldn't load and the wordmark would fall back to a
generic sans. Keep to the sanitiser's allowlist: `<title>` is permitted, `role` and `aria-*` are
not. `ink` is for the white form card, `frost` for the
`--pd-boards` header; `ColorMode` on each asset is the browser's light/dark preference, not the
colour of the surface behind the logo. `categories.global.pageHeader` is enabled to give the frost
lockup its `--pd-boards` band — the schema has no text colour for the header, only a background and
a logo, so nothing else should land on that dark strip, but that's worth an eye on first deploy. Branding also requires the domain on
`ManagedLoginVersion.NEWER_MANAGED_LOGIN` — the CDK default is the classic hosted UI, which
ignores styles — and the Essentials feature plan or higher.

### Infrastructure orchestration (Aspire) vs. deployment (CDK)

- **`Infrastructure/PuckDrop.AppHost/AppHost.cs`** is the Aspire app model: it wires DynamoDB
  Local, Keycloak (dev-only IdP), the Lambda function (via `AddAWSLambdaFunction`), the API
  Gateway HTTP API emulator, and the Blazor WASM app together for `aspire start`. It also declares
  the AWS CDK environment used for real deployment.
- **`Infrastructure/PuckDrop.AppHost/AWS/DeploymentStack.cs`** is the CDK stack used when
  publishing: creates the DynamoDB table + GSIs, the Cognito user pool/client/admin group, and an
  HTTP API Gateway with a JWT authorizer wired to the Lambda. No VPC/ECS cluster — nothing in the
  stack needs one (confirmed: DynamoDB, Cognito, and API Gateway are all public managed APIs; the
  Lambda's `Vpc` property is nullable/opt-in and never set). The `ConstructFunctionCallback` in
  `AppHost.cs` (`PublishAsLambdaFunction`) is where CDK-provisioned values (user pool ID, table
  grants, API Gateway route) get pushed into the Lambda's environment/permissions at publish time
  — this is the seam between the two systems.
- **`Infrastructure/PuckDrop.AppHost/AWS/Deployment/`** publishes the Blazor WASM UI (`web` in
  `AppHost.cs`) as an S3 bucket fronted by CloudFront — `BlazorStaticSitePublishTarget`, a real
  `IAWSPublishTarget` registered via `builder.Services.AddTransient<IAWSPublishTarget,
  BlazorStaticSitePublishTarget>()` in `AppHost.cs` (the same DI mechanism every built-in AWS
  target uses — a genuine, consumer-usable extensibility point in `Aspire.Hosting.AWS`, not a
  workaround), invoked via `.PublishAsS3WithCloudFront()` on `web`. That extension adds a
  `build-web-static-site` pipeline step that runs `dotnet publish -c Release` into a temp folder,
  alongside the Lambda's `build-api` and before the CDK step. The target then builds the
  bucket/OAC/distribution/SPA-fallback/deployment CDK constructs, bakes in a `/puckdrop/*` CloudFront behavior routing to the real API Gateway (read
  directly off `DeploymentStack.HttpApi`, since that API Gateway isn't wrapped by any Aspire
  resource), and fixes up the Cognito `UserPoolClient`'s OAuth callback/logout URLs — created
  with a `localhost` placeholder in `DeploymentStack` before the CloudFront domain exists — via
  the CDK "escape hatch" (`Node.DefaultChild` cast to `CfnUserPoolClient`, whose properties are
  `CallbackUrLs`/`LogoutUrLs` — note the unusual JSII-codegen casing) once the distribution is
  built. Modeled closely on AWS's own unreleased `S3StaticWebsitePublishTarget` for JS apps
  (aws/integrations-on-dotnet-aspire-for-aws#203) for easy swap-out if that ships.
- `PuckDrop.ServiceDefaults` / `PuckDrop.ClientServiceDefaults` hold shared OpenTelemetry/service
  discovery wiring for the server and Blazor WASM client respectively, added via
  `AddLambdaServiceDefaults()` / `AddBlazorClientServiceDefaults()`.

### UI

Blazor WebAssembly app (`UI/src/PuckDrop.Web`). `PuckDropApiClient` (in `Services/`) is the sole
HTTP client to the API, pointed at the API Gateway emulator's base URL (resolved from Aspire
service discovery config, falling back to `ApiClientSettings:BaseUrl`, then
`HostEnvironment.BaseAddress` — same-origin, which is what makes the CloudFront `/puckdrop/*`
behavior above work for a real deployment — then a hardcoded local-dev default as a true last
resort). Provider-agnostic OIDC auth is wired up in `Program.cs`, which fetches its config from
the API's `GET /auth-config` at boot rather than a static file — see "Auth" above. If that fetch
fails, `Program.cs` registers an `AuthConfigLoadResult` that `App.razor` checks before rendering
its normal `<Router>`/`<CascadingAuthenticationState>` tree, showing a clear error instead of a
blank page.

Logins survive closing the site: `wwwroot/js/persist-login.js` (loaded between
`AuthenticationService.js` and `blazor.webassembly.js` in `index.html`) wraps Blazor's internal
`window.AuthenticationService.createUserManagerCore` so the oidc-client user - including its
refresh token - lives in `localStorage` under a `puckdrop.oidc.` prefix instead of Blazor's
default per-tab `sessionStorage`. On reopen, Blazor's startup silent sign-in renews via that
refresh token, for up to the app client's 30-day `RefreshTokenValidity` (rotation enabled, both in
`DeploymentStack`). Logout revokes the refresh token best-effort before the provider logout
redirect. If a Blazor upgrade removes the hook the script no-ops; `PersistentLoginTests` and
`LogoutTests` (E2E) guard both behaviours.

Pages are split into
top-level (`Home`, `Poll`, `Leaderboard`, `History`, `Results`) and `Pages/Admin/*`
(`CreatePoll`, `EditPoll`, `Polls`, `ScorePoll`) for the create/score workflow.

Styling is the app's own WCAG 2.2 AA design system in `wwwroot/css/app.css` - no Bootstrap. It
holds a small reset, colour/type/size tokens, `pd-`-prefixed shared components, and the styling for
Blazor's loading screen and error UI; page-only styles go in that page's `.razor.css`. Shared
components: `Components/Icon.razor` (decorative inline SVG icons), `Components/PollStatusBadge.razor`
and `Components/StatusScreen.razor` (no access, not found, sign-in outcomes, load failure). Helpers:
`Display/DisplayText.cs` (avatar initials, "In 5 days") and `Layout/NavSection.cs` (maps child
routes like `poll/{id}` to their nav tab for `aria-current`). Tests find elements by role, visible
text or ARIA state rather than CSS classes where they can.

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
- `docs/auth-security-testing.md` — planned, not started; closes a real gap (raw-API authorization
  enforcement, independent of the UI) rather than being exploratory — runs against the local
  Aspire stack, unlike the load/stress/smoke doc above
- `docs/ai-features.md` — future consideration; AI-generated post-poll recaps/season awards and
  admin-assist features (draft poll questions, auto-suggested scoring), ranked by fit and cost
- `docs/eihl-gamesheet-schema.md` — reference for `ai-features.md`'s auto-suggested-scoring idea;
  field-level schema of the official EIHL gamesheet data source, verified against the live site
