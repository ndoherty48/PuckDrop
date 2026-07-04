# Implementation Plan

## Overview

PuckDrop is built incrementally, bottom-up. Each phase produces a working, buildable solution and is committed independently.

---

## Phase 1 — Domain Layer ✅

**Status:** Complete  
**Commit:** `feat: add domain entities and business rules`

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
- Zero infrastructure dependencies in Domain layer
- `DateTime` stored as UTC throughout

---

## Phase 2 — Infrastructure (DynamoDB Repositories) ✅

**Status:** Complete  
**Commit:** `feat: add DynamoDB infrastructure layer`

### What was built

| Component | Location | Purpose |
|-----------|----------|---------|
| Repository interfaces | `PuckDrop.Domain/Repositories/` | Contracts for data access (no DynamoDB knowledge) |
| `DynamoDbKeys` | `PuckDrop.Infrastructure/DynamoDb/` | Key construction constants and helpers |
| DynamoDB item models | `PuckDrop.Infrastructure/DynamoDb/Items/` | PK/SK/GSI shapes matching `dynamodb-design.md` |
| `DynamoDbMapper` | `PuckDrop.Infrastructure/DynamoDb/Mappers/` | Bidirectional mapping between domain entities and DynamoDB items |
| `DynamoDbSeasonRepository` | `PuckDrop.Infrastructure/DynamoDb/Repositories/` | Season CRUD + list (transact writes for dual items) |
| `DynamoDbPollRepository` | `PuckDrop.Infrastructure/DynamoDb/Repositories/` | Poll CRUD, list by season, active polls (GSI2), full poll with questions/options (GSI1) |
| `DynamoDbUserAnswerRepository` | `PuckDrop.Infrastructure/DynamoDb/Repositories/` | Submit/get answers, bulk results query via GSI1, batch score updates |
| `DynamoDbLeaderboardRepository` | `PuckDrop.Infrastructure/DynamoDb/Repositories/` | Leaderboard read (inverted sort key), update (transact delete old SK + put new SK) |
| DI registration | `InfrastructureServiceCollectionExtensions.cs` | `IAmazonDynamoDB` fallback + all 4 repository singletons |

### Design decisions

- Low-level `IAmazonDynamoDB` client for full single-table control (not DynamoDBContext)
- Transact writes for multi-item consistency (poll + season collection, leaderboard SK rotation)
- Inverted zero-padded score key for descending leaderboard sort
- `HydrateStatus` walks the domain state machine to reach persisted status (respects private setter)
- Batch writes in chunks of 25 for scoring (DynamoDB limit)
- Aspire provides `IAmazonDynamoDB` normally; fallback registration for non-Aspire environments

### Single-table design

All entities stored in one `PuckDrop` table with composite keys. See [DynamoDB Design](dynamodb-design.md) for full key schema, GSI definitions, and access patterns.

---

## Phase 3 — Application Layer (Use Cases)

**Status:** Not started

### Planned

| Service | Operations |
|---------|-----------|
| `SeasonService` | Get current season, list seasons |
| `PollService` | Create poll, publish, close, get poll with user's answers, list polls |
| `AnswerService` | Submit/update answers (with deadline validation) |
| `ScoringService` | Score a poll — evaluate all answers, update leaderboard |
| `LeaderboardService` | Get season leaderboard |

### Design approach

- Thin service classes orchestrating domain entities + repositories
- No business logic in services — delegate to entity methods
- Services validate preconditions, load aggregates, call domain methods, persist

---

## Phase 4 — API Controllers

**Status:** Not started

### Planned

| Controller | Endpoints |
|-----------|-----------|
| `SeasonsController` | `GET /seasons`, `GET /seasons/current` |
| `PollsController` | `GET /polls`, `GET /polls/active`, `GET /polls/{id}`, `POST /polls`, `PUT /polls/{id}`, `POST /polls/{id}/publish`, `POST /polls/{id}/close` |
| `QuestionsController` | `POST /polls/{id}/questions`, `PUT /polls/{id}/questions/{qid}`, `DELETE /polls/{id}/questions/{qid}` |
| `AnswersController` | `GET /polls/{id}/answers`, `PUT /polls/{id}/answers` |
| `ScoringController` | `POST /polls/{id}/score` |
| `ResultsController` | `GET /polls/{id}/results` |
| `LeaderboardController` | `GET /leaderboard` |

### Design approach

- Minimal controllers — validate input, call application service, return response
- Consistent error responses (`error` code + `message`)
- Route prefix `/puckdrop` applied via middleware (already configured in `LambdaEntryPoint`)

---

## Phase 5 — Authentication & Authorisation

**Status:** Not started

### Planned

- Cognito JWT validation via API Gateway authoriser
- User ID extracted from `sub` claim
- Admin role from Cognito group / custom claim
- `[Authorize]` attributes on controllers
- Custom `AdminOnly` policy

---

## Phase 6 — Blazor WASM Frontend

**Status:** Not started

### Planned (priority order)

1. Poll submission flow (core UX)
2. Results page
3. Leaderboard
4. Home page (dashboard)
5. Admin: create/edit/score polls
6. History page

---

## Phase 7 — Integration & Testing

**Status:** Not started

### Planned

- Unit tests for domain entities
- Integration tests against DynamoDB Local
- API endpoint tests via WebApplicationFactory
- Blazor component tests (bUnit)
