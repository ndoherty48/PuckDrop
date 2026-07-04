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
| `AdminOnly` policy | `ApiServiceCollectionExtensions` | Requires `cognito:groups` contains "admin" |
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
- Admin policy checks `cognito:groups` claim value (Cognito group membership)

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

**Status:** Not started

### Planned

- Unit tests for domain entities (status transitions, scoring, season derivation)
- Unit tests for application services (with mocked repositories)
- Integration tests against DynamoDB Local
- API endpoint tests via WebApplicationFactory
- Blazor component tests (bUnit)

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
