# Implementation Plan

## Overview

PuckDrop is built incrementally, bottom-up. Each phase produces a working, buildable solution and is committed independently.

### Architecture

```
PuckDrop.Domain (zero dependencies)
├── Entities/          # Business objects with behaviour
└── Enums/             # PollStatus

PuckDrop.Application (→ Domain)
├── Models/            # PollWithQuestions, AnswerSubmission, QuestionScore, OptionDefinition
├── Repositories/      # Interface contracts (ports)
├── Services/          # Use case orchestration
└── Services/Abstractions/  # IUserProfileService

PuckDrop.Infrastructure (→ Application → Domain)
├── DynamoDb/          # Repository implementations (adapters)
└── Identity/          # DefaultUserProfileService

PuckDrop.Api (→ Application, Infrastructure)
├── Controllers/       # Thin HTTP handlers
├── Contracts/         # Request/response DTOs
├── Filters/           # DomainExceptionFilter
└── Mappings/          # Per-type ToResponse() extension methods
```

Dependencies only flow inward. Domain is pure entities and business rules with zero outward-facing contracts.

---

## Phase 1 — Domain Layer ✅

**Status:** Complete

### What was built

| File | Purpose |
|------|---------|
| `Enums/PollStatus.cs` | Draft, Open, Closed, Scored lifecycle states |
| `Entities/Season.cs` | Season entity with `DeriveSeasonId()` and `CreateForDate()` helpers |
| `Entities/GameDayPoll.cs` | Poll entity with status machine (`Publish`, `Close`, `MarkScored`) and `IsAcceptingAnswers` guard |
| `Entities/Question.cs` | Question with `SetCorrectOption()` and private setter enforcement |
| `Entities/Option.cs` | Simple value entity (OptionId, QuestionId, Text, SortOrder) |
| `Entities/UserAnswer.cs` | User's pick with `Evaluate()` scoring method |
| `Entities/LeaderboardEntry.cs` | Aggregated score with `AddPollResults()` and computed `Accuracy` |

### Design decisions

- Rich domain model — behaviour on entities, not in services
- Private setters on guarded properties (`Status`, `CorrectOptionId`, `IsCorrect`)
- Zero dependencies — Domain references nothing
- `DateTime` stored as UTC throughout

---

## Phase 2 — Infrastructure (DynamoDB Repositories) ✅

**Status:** Complete

### What was built

| Component | Location | Purpose |
|-----------|----------|---------|
| `DynamoDbKeys` | `Infrastructure/DynamoDb/` | Key construction constants and helpers |
| DynamoDB item models | `Infrastructure/DynamoDb/Items/` | PK/SK/GSI shapes matching `dynamodb-design.md` |
| `DynamoDbMapper` | `Infrastructure/DynamoDb/Mappers/` | Bidirectional mapping between domain entities and DynamoDB items |
| `DynamoDbSeasonRepository` | `Infrastructure/DynamoDb/Repositories/` | Season CRUD + list (transact writes for dual items) |
| `DynamoDbPollRepository` | `Infrastructure/DynamoDb/Repositories/` | Poll CRUD, list by season, active polls (GSI2), full poll with questions/options (GSI1) |
| `DynamoDbUserAnswerRepository` | `Infrastructure/DynamoDb/Repositories/` | Submit/get answers, bulk results query via GSI1, batch score updates |
| `DynamoDbLeaderboardRepository` | `Infrastructure/DynamoDb/Repositories/` | Leaderboard read (inverted sort key), update (transact delete old SK + put new SK) |
| DI registration | `InfrastructureServiceCollectionExtensions.cs` | `IAmazonDynamoDB` singleton + scoped repositories + `IUserProfileService` |

### Design decisions

- Low-level `IAmazonDynamoDB` client for full single-table control (not DynamoDBContext)
- Transact writes for multi-item consistency (poll + season collection, leaderboard SK rotation)
- Inverted zero-padded score key for descending leaderboard sort
- `HydrateStatus` walks the domain state machine to reach persisted status (respects private setter)
- Batch writes in chunks of 25 for scoring (DynamoDB limit)
- Aspire provides `IAmazonDynamoDB` normally; fallback registration for non-Aspire environments

---

## Phase 3 — Application Layer (Use Cases) ✅

**Status:** Complete

### What was built

| Component | Purpose |
|-----------|---------|
| `Repositories/` | Interface contracts: `IPollRepository`, `ISeasonRepository`, `IUserAnswerRepository`, `ILeaderboardRepository` |
| `Services/Abstractions/IUserProfileService` | Display name resolution (implemented by Infrastructure) |
| `Models/ApplicationModels.cs` | `PollWithQuestions`, `AnswerSubmission`, `QuestionScore`, `OptionDefinition` |
| `SeasonService` | `GetCurrentSeason`, `ListSeasons`, `EnsureSeasonExists` (auto-creates from game date) |
| `PollService` | `CreatePoll`, `UpdatePoll`, `PublishPoll`, `ClosePoll`, `GetPoll`, `GetPollWithQuestions`, `ListPolls`, `GetActivePolls`, `AddQuestion`, `DeleteQuestion` |
| `AnswerService` | `SubmitAnswers` (validates poll open, deadline, question/option IDs), `GetUserAnswers` |
| `ScoringService` | `ScorePoll` (evaluates answers, aggregates points, updates leaderboard, transitions status) |
| `LeaderboardService` | `GetLeaderboard` (defaults to current season, shared ranks for ties: 1,2,2,4) |
| `ResultsService` | `GetPollResults` (full scored poll with all users' answers) |

### Design decisions

- Thin orchestration services — business logic delegated to entity methods
- Scoped DI lifetime (one instance per HTTP request)
- `Guid.CreateVersion7()` for time-ordered unique IDs (native .NET 10)
- Named record types instead of tuples for all method parameters and return types
- `IUserProfileService` injected into `ScoringService` (no `Func` parameters)

---

## Phase 4 — API Controllers ✅

**Status:** Complete

### What was built

| Controller | Endpoints |
|-----------|-----------|
| `SeasonsController` | `GET /seasons`, `GET /seasons/current` |
| `PollsController` | `GET /polls`, `GET /polls/active`, `GET /polls/{id}`, `POST /polls`, `PUT /polls/{id}`, `POST /polls/{id}/publish`, `POST /polls/{id}/close` |
| `QuestionsController` | `POST /polls/{id}/questions`, `PUT /polls/{id}/questions/{qid}`, `DELETE /polls/{id}/questions/{qid}` |
| `AnswersController` | `GET /polls/{id}/answers`, `PUT /polls/{id}/answers` |
| `ScoringController` | `POST /polls/{id}/score` |
| `ResultsController` | `GET /polls/{id}/results` |
| `LeaderboardController` | `GET /leaderboard?seasonId=` |

Supporting infrastructure:
- `Contracts/ApiContracts.cs` — all request/response DTOs as records
- `Filters/DomainExceptionFilter.cs` — global exception-to-HTTP mapping
- `Mappings/` — per-type `ToResponse()` extension methods (7 files)

### Design decisions

- Thin controllers — extract input, call service, map response via extensions
- Primary constructors throughout (no field + constructor boilerplate)
- Global exception filter: `KeyNotFoundException` → 404, `InvalidOperationException` → 400, `ArgumentException` → 400
- No null-forgiving (`!`) operators — proper null handling everywhere
- No tuples — named records for all data passing
- Per-type mapping extension classes (not one monolithic mapper)
- `GetUserId()` placeholder extracts from JWT `sub` claim (wired up in Phase 5)

---

## Phase 5 — Authentication & Authorisation ✅

**Status:** Complete

### What was built

| Component | Location | Purpose |
|-----------|----------|---------|
| `CognitoSettings` | `Api/Auth/` | Configuration model (UserPoolId, ClientId, Region, AdminGroupName) |
| JWT middleware | `ApiServiceCollectionExtensions` | Bearer token validation against Cognito issuer |
| `AdminOnly` policy | `ApiServiceCollectionExtensions` | `RequireRole("admin")` — provider-agnostic, see note below |
| `ClaimsPrincipalExtensions` | `Api/Auth/` | `GetUserId()` and `GetDisplayName()` from JWT claims |
| `CognitoUserProfileService` | `Api/Auth/` | Implements `IUserProfileService` using HTTP context claims |
| `[Authorize]` attributes | All controllers | User-level auth on all endpoints |
| `[Authorize(Policy = "AdminOnly")]` | Poll CRUD, Questions, Scoring | Admin-only on write endpoints |

### Design decisions

- In-app JWT validation (works locally without API Gateway authorizer)
- Auth is optional (graceful if `Cognito` config section is missing — for local dev)
- `CognitoUserProfileService` lives in Api layer (depends on `IHttpContextAccessor`)
- Claims fallback chain: `name` → `cognito:username` → `email` → `sub`
- `UnauthorizedAccessException` thrown if `sub` claim is missing (shouldn't happen with valid JWT)
- Admin policy is provider-agnostic: `CognitoClaimsTransformation`/`KeycloakClaimsTransformation`
  (added later, see CLAUDE.md's Auth section) normalize each provider's own admin signal into a
  standard `ClaimTypes.Role` "admin" claim, so `AdminOnly` itself never checks a provider-specific
  claim

---

## Phase 6 — Blazor WASM Frontend ✅

**Status:** Complete

### What was built

| Page | Route | Purpose |
|------|-------|---------|
| Home | `/` | Dashboard with active poll CTA, quick leaderboard |
| Poll | `/poll/{id}` | View questions, submit/edit answers, deadline countdown |
| Results | `/results/{id}` | Per-question breakdown with correct/incorrect per user |
| Leaderboard | `/leaderboard` | Season standings table with rank, points, accuracy |
| History | `/history` | Past polls list with status badges and result links |
| Admin: Polls | `/admin/polls` | List/manage all polls with contextual action buttons |
| Admin: Create | `/admin/polls/create` | Create poll form (title, date, deadline) |
| Admin: Edit | `/admin/polls/{id}/edit` | Edit poll, add/remove questions with options |
| Admin: Score | `/admin/polls/{id}/score` | Mark correct answers per question |

Supporting infrastructure:
- `Services/PuckDropApiClient.cs` — typed HTTP client wrapping all API endpoints
- `Services/Models.cs` — client-side records matching API contracts
- `Layout/MainLayout.razor` — Bootstrap navbar, mobile-first, dark theme

### Design decisions

- Bootstrap 5 for styling (already in template, mobile-first)
- No client-side auth yet — API handles auth; UI will add Cognito OIDC later
- Graceful error handling with try/catch and user-friendly messages
- API base URL from Aspire environment variable (`ApiClientSettings__BaseUrl`)
- No state management library — simple per-page state
- Removed template pages (Counter, Weather) and sample-data

---

## Phase 7 — Integration & Testing

**Status:** Complete — fast/unit slice, bUnit component tests, and a full E2E suite all done

### What was built

| Project | Location | Tests | Covers |
|---------|----------|-------|--------|
| `PuckDrop.Domain.Tests` | `API/tests/` | 42 | Entity state machines (`GameDayPoll`), boundary conditions, season derivation math |
| `PuckDrop.Application.Tests` | `API/tests/` | 37 | All 6 services, with the 4 repository interfaces mocked via NSubstitute |
| `PuckDrop.Api.Tests` | `API/tests/` | 34 | `DomainExceptionFilter`, both IdP claims transformations, `ClaimsPrincipalExtensions`, response mappings, `AddApis`'s provider-resolution/`AuthDiscoveryOptions` behavior, `AuthConfigController` |
| `PuckDrop.Web.Tests` | `UI/tests/` | 26 | `PuckDropApiClient`'s response handling, `PuckDropClaimsPrincipalFactory`'s role normalization, `App.razor`'s auth-config-load-failure branch, plus bUnit component tests (below) |

xUnit v3 on Microsoft.Testing.Platform (matches `global.json`'s `test.runner` setting — no
`xunit.runner.visualstudio`/`Microsoft.NET.Test.Sdk` needed), NSubstitute for mocking, no
FluentAssertions (v8+ requires a paid commercial license outside qualifying non-commercial use;
xUnit v3's built-in `Assert` covers what's needed). All 139 tests build and pass together fast (a
couple of seconds total) — the separate, much slower `tests/PuckDrop.E2ETests/` project (9 tests)
is covered on its own below. `dotnet build`/`dotnet test PuckDrop.slnx` covers all five projects
together; see `CLAUDE.md` for running the fast subset on its own for routine iteration.

**bUnit component tests** (added to `PuckDrop.Web.Tests` rather than a separate project — the UI
test suite isn't large enough to warrant fragmenting it): `PollTests` (`AllQuestionsAnswered`/
`IsVotingClosed` gating the submit button, closed-voting badge, and disabled radio inputs),
`LeaderboardTests` (the current-user row highlight matching the raw `"sub"` claim — a real
regression fixed earlier in this project's history; deliberately reverted to the wrong
`ClaimTypes.NameIdentifier` check to confirm the test actually catches it, then restored),
`Admin/PollsTests` and `Admin/ScorePollTests` (`confirm()` JSInterop gating before Publish/Close/
Submit Scores actions fire, plus `ScorePoll`'s own answer-completeness gating and post-submit
navigation to the results page). `PuckDropApiClient` is faked via a new shared
`TestSupport/RoutingHttpMessageHandler` (routes canned responses by method + path/query, since a
single component often calls several endpoints in one render — the existing single-fixed-response
fake in `PuckDropApiClientTests` doesn't need to support that). Ground-truth for bUnit 2.9's API
(`BunitContext`/`.Render<T>()` replacing the now-obsolete `TestContext`/`.RenderComponent<T>()`,
`JSInterop.Setup<T>(...)`, `AddAuthorization()`/`.SetClaims(...)`) was confirmed against the
package's own XML docs rather than assumed, since it had moved since the last time this session
worked with an older bUnit API shape.

### Design decisions

- Domain and Application kept as separate test projects: Domain has zero dependencies (matches
  the layer's own architecture rule) and stays on the fastest possible feedback loop; only
  Application needs the NSubstitute dependency.
- Each phase's suite was checked for being non-vacuous by deliberately breaking one piece of the
  logic under test, confirming the corresponding test failed, then reverting.
- `CancellationToken` is threaded through explicitly via `TestContext.Current.CancellationToken`
  in every async call, rather than suppressing the `xUnit1051` analyzer warning — cheap to do
  correctly regardless of whether today's mocks happen to use it.

### E2E suite (`tests/PuckDrop.E2ETests/`)

Supersedes the two items this section originally deferred (a separate DynamoDB-only integration
project via Testcontainers, and a separate API full-HTTP-pipeline project) with one suite that's
purely browser-driven: `Aspire.Hosting.Testing` boots the real AppHost (real DynamoDB Local, real
Keycloak, the real Lambda-hosted API, the real Blazor WASM app), and Playwright drives a real
headless Chromium browser against it for everything, including setup — no mocking anywhere in this
layer. Rejected the raw-HTTP layer the original sketch of this had: DynamoDB round-trip precision
(the exact `DisplayName` bug this section used to cite as the motivating gap) is more faithfully
tested by submitting as a real user and viewing the real name on Results/Leaderboard than by a raw
JSON assertion — a UUID-instead-of-name is literally what that bug looked like to an actual user.

Repo-root `tests/` (not under `API/`/`UI/`) since the suite is genuinely cross-cutting. Lives in
its own collection fixture (`AppHostFixture`, built once per run): boots the AppHost, launches one
shared browser, and gives each test its own isolated `IBrowserContext`. Session reuse
(`LoginAndCaptureSessionAsync`/`NewAuthenticatedBrowserContextAsync`) avoids repeating the login UI
for every test that just needs to already be authenticated — carrying both cookies (so a stale
cached access token can still silently renew via Keycloak's own SSO cookie) and Blazor's
`sessionStorage`-only auth cache (which Playwright's built-in `StorageState` never covers).

Coverage: real login for both roles with role-gated nav; `AuthorizeRouteView`'s three branches
(authorized / authenticated-but-not-authorized / anonymous) on a direct admin-only navigation; the
full poll lifecycle end to end — create with two questions, publish (real JS `confirm()`), a
second real user votes, close, score with a deliberate correct/incorrect split, real display names
on Results (not UUIDs), Leaderboard points, then a second poll confirming points accumulate rather
than reset; the auth-config bootstrap failure mode live in a real browser (not just bUnit's
in-memory version); and logout, including the real cross-origin round trip to Keycloak's own
logout endpoint. Deliberately out of scope: the "voting closed" UI state (already covered by
bUnit's `PollTests` against mocked data) and Cognito (needs real AWS infra, out of reach locally —
this suite only exercises Keycloak's real flow).

**Known, accepted flakiness**: the AWS Lambda Service Emulator (`lambda-test-tool`, via
`Aspire.Hosting.AWS`) processes one invocation at a time — real, checked directly (no concurrency
flag on the CLI, no such setting anywhere in `Aspire.Hosting.AWS.dll`), not a guess. Under this
suite's own sequential real traffic, a step can occasionally take longer than Program.cs's
deliberate 10-second OIDC-bootstrap-fetch timeout (a correct production fail-fast, left untouched)
- `AppHostFixture.GotoWithBootstrapRetryAsync`/`ReloadOnBootstrapFailureAsync` absorb most of this
by reloading and retrying, but not unconditionally. Three separate AppHost bugs were found and
fixed along the way (all verified live via `aspire start` and a real browser, not just under the
test harness) — `dynamodb`/`api-gateway` given real health checks (`AddAWSDynamoDBLocal`/
`AddAWSAPIGatewayEmulator` register none of their own), and Keycloak's browser-facing OIDC
Authority fixed to resolve to a URL Keycloak itself would reliably accept
(`WithExternalHttpEndpoints()`) — all in `Infrastructure/PuckDrop.AppHost/AppHost.cs`; these fix
the same race for plain local `aspire start` too, not just this suite.

For the flakiness that's left after all of the above, the project carries
`Microsoft.Testing.Extensions.Retry` (a `PackageReference` self-registers it via an MSBuild hook -
no code needed) — see `CLAUDE.md` for the exact command. Confirmed for real: a deliberately-broken
assertion genuinely got retried across a fresh process each time and still correctly failed at the
end, proving the mechanism itself works; two separate live full runs with it enabled both happened
to pass outright with no retry triggered, so a transient failure actually being rescued by it
hasn't been directly observed yet, just the mechanism working correctly in isolation.

### Not yet done

- **CI** (`.github/workflows` running `dotnet test`) — explicit separate decision, not yet made;
  revisit now that a real test suite (including this E2E layer) exists to run. Would need to
  decide how to handle this suite's Docker + Playwright-browser prerequisites and its own
  flakiness in a CI environment specifically.

---

## Code Standards

Established through implementation and refactoring:

| Standard | Approach |
|----------|----------|
| Constructors | Primary constructors (no field + ctor boilerplate) |
| DI lifetimes | Singleton for `IAmazonDynamoDB`; Scoped for repositories and services |
| ID generation | `Guid.CreateVersion7().ToString("N")` |
| Null handling | Proper null checks; no null-forgiving `!` operators |
| Data passing | Named records; no value tuples in method signatures |
| Response mapping | Per-type static extension classes with `ToResponse()` |
| Error handling | Global `DomainExceptionFilter`; no try/catch in controllers |
| Architecture | Domain pure (zero deps); Application defines ports; Infrastructure implements |
