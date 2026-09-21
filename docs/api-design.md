# API Design

## Overview

RESTful API served via AWS Lambda behind API Gateway (HTTP API v2). All endpoints prefixed with `/puckdrop`.

## Authentication

- Cognito (production) or Keycloak (local dev) issues JWTs - whichever is configured, see
  `ApiServiceCollectionExtensions.AddApis`
- API Gateway validates the JWT (authorizer) in production; in-app JWT bearer validation always
  runs too, so the API is self-protecting even without the gateway-level authorizer
- Lambda reads claims from the validated token (`sub` = userId; each provider's own admin signal
  is normalized into a standard `admin` role claim - see `CognitoClaimsTransformation`/
  `KeycloakClaimsTransformation`)
- No anonymous access except health check and `GET /auth-config` (serves the OIDC config the
  Blazor UI needs before it has a token to authenticate with - see "Auth" below)

## Roles

| Role | Permissions |
|------|-------------|
| admin | All user permissions + create/edit polls, mark answers |
| user | View polls, submit/edit own answers, view leaderboard |

## Endpoints

### Health

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/health` | None | Health check |

### Auth

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/auth-config` | None | OIDC `Authority`/`ClientId`/`ResponseType` for whichever provider is active, so the Blazor WASM app can fetch its login config at boot instead of it being baked into `wwwroot/appsettings.json` at build time (there's no way to inject CDK-provisioned values into a static WASM bundle the way the Lambda's own env vars are injected at publish time). Deliberately unauthenticated - the UI has no token yet when it asks how to get one - and only ever returns values that are meant to be public (an issuer URL and a public client ID, no secret). In production this needs its own API Gateway route with no JWT authorizer, since the catch-all Lambda route otherwise requires a token for every path under `/puckdrop/` - see `DeploymentStack.cs`. |

### Seasons

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/seasons` | User | List all seasons |
| GET | `/seasons/current` | User | Get current active season |

### Polls

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/polls?seasonId={id}&status={status}` | User | List polls (filterable) |
| GET | `/polls/active` | User | Get currently open poll(s) |
| GET | `/polls/{pollId}` | User | Get poll with questions, options, and current user's answers |
| POST | `/polls` | Admin | Create a new poll |
| PUT | `/polls/{pollId}` | Admin | Update poll (title, deadline) |
| POST | `/polls/{pollId}/publish` | Admin | Move from Draft → Open |
| POST | `/polls/{pollId}/close` | Admin | Manually close voting |

### Questions (nested under polls)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/polls/{pollId}/questions` | Admin | Add a question with options |
| PUT | `/polls/{pollId}/questions/{questionId}` | Admin | Edit question/options |
| DELETE | `/polls/{pollId}/questions/{questionId}` | Admin | Remove a question |

### Scoring

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/polls/{pollId}/score` | Admin | Mark correct answers for all questions |

**Request body:**
```json
{
  "answers": [
    { "questionId": "...", "correctOptionId": "..." }
  ]
}
```

This triggers scoring: evaluates all UserAnswers, sets IsCorrect, and records what each player
earned for this poll.

**Re-scoring is the same call.** A poll already `Scored` can be scored again — to fix a wrong correct
option, finish a partly scored poll, or retry a pass that failed halfway. Every write uses a
deterministic key, so it overwrites rather than double-counting. Only questions included in the
request are graded; omitted ones are left ungraded and don't count towards anyone's answered total.

### Voided picks

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/polls/{pollId}/voids` | Admin | Void one player's picks for this game day |
| DELETE | `/polls/{pollId}/voids/{userId}` | Admin | Restore previously voided picks |

**Request body (POST):**
```json
{ "userId": "...", "reason": "Picked after puck drop" }
```

A void takes that game day's points *and* answered count off the player's season total. The reason is
required and shown to every player on the leaderboard. Voting must be closed first (`Closed` or
`Scored`), and the player must actually have made picks — otherwise `400`. Restoring is a conditional
delete: undoing a void that doesn't exist is `404`, not a silent success. A void survives re-scoring,
so restoring after a re-score reveals the *new* score.

### Point adjustments

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/leaderboard/adjustments` | Admin | Apply a signed points change |
| DELETE | `/leaderboard/adjustments/{adjustmentId}?userId={id}&seasonId={id}` | Admin | Remove an adjustment |

**Request body (POST):**
```json
{ "userId": "...", "points": -5, "reason": "Picked after puck drop", "seasonId": null }
```

`points` is signed and non-zero (max ±1000); negative deducts. `seasonId` defaults to the current
season. The player must already have a leaderboard standing — names are only ever captured from a
player's own answers, so there is nobody to name an adjustment after otherwise (`404`). A deduction
may take a total below zero.

`userId` and `seasonId` are required on DELETE as well as the id: the adjustment is keyed on all
three and there is no index on the id alone. Removing one restores the total exactly.

### User Answers

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/polls/{pollId}/answers` | User | Get current user's answers for a poll |
| PUT | `/polls/{pollId}/answers` | User | Submit or update answers |

**Request body:**
```json
{
  "answers": [
    { "questionId": "...", "selectedOptionId": "..." }
  ]
}
```

**Validation:**
- 400 if poll status is not Open
- 400 if current time > poll deadline
- 400 if any questionId doesn't belong to the poll
- 400 if any selectedOptionId doesn't belong to the question

### Results

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/polls/{pollId}/results` | User | All users' answers + scores for a scored poll |

**Response:**
```json
{
  "pollId": "...",
  "status": "Scored",
  "questions": [
    {
      "questionId": "...",
      "text": "Who scores first?",
      "correctOptionId": "...",
      "options": [...]
    }
  ],
  "userResults": [
    {
      "userId": "...",
      "displayName": "Nick",
      "answers": [
        { "questionId": "...", "selectedOptionId": "...", "isCorrect": true }
      ],
      "points": 1
    }
  ]
}
```

### Leaderboard

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/leaderboard?seasonId={id}` | User | Season leaderboard (defaults to current season) |

**Response:**
```json
{
  "seasonId": "2025-26",
  "entries": [
    {
      "userId": "...", "displayName": "Nick", "rank": 1,
      "totalPoints": 12, "totalAnswered": 15,
      "earnedPoints": 12, "adjustmentPoints": 0,
      "adjustments": [], "voids": []
    },
    {
      "userId": "...", "displayName": "Dave", "rank": 2,
      "totalPoints": 5, "totalAnswered": 11,
      "earnedPoints": 10, "adjustmentPoints": -5,
      "adjustments": [{ "adjustmentId": "...", "points": -5, "reason": "Picked after puck drop" }],
      "voids": [{ "pollId": "...", "pollTitle": "Giants vs Steelers", "reason": "No-show" }]
    }
  ]
}
```

`totalPoints` is `earnedPoints + adjustmentPoints` and **may be negative**. `adjustments` and `voids`
are public: every player sees why a penalty was applied. Who applied it stays internal. Accuracy is
computed from `earnedPoints`, not `totalPoints` — a sanction is not a wrong answer.

There is no stored total: the whole season is folded from its scoring facts in one query on each
read. See `docs/dynamodb-design.md`.

## Error Responses

All errors follow a consistent shape:

```json
{
  "error": "POLL_CLOSED",
  "message": "Voting has closed for this poll."
}
```

| Code | HTTP Status | When |
|------|-------------|------|
| POLL_CLOSED | 400 | Submitting answers after deadline |
| POLL_NOT_FOUND | 404 | Invalid pollId |
| FORBIDDEN | 403 | User attempting admin action |
| VALIDATION_ERROR | 400 | Invalid request body |
