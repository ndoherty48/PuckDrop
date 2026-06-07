# Domain Model

## Entities

### Season

Represents an EIHL season running August → April.

| Field | Type | Notes |
|-------|------|-------|
| SeasonId | string | Format: `2025-26` |
| Name | string | e.g. "2025/26 Season" |
| StartDate | date | August 1st |
| EndDate | date | April 30th |

### GameDayPoll

A poll tied to a specific game day. Contains one or more questions.

| Field | Type | Notes |
|-------|------|-------|
| PollId | string (ULID) | Unique identifier |
| SeasonId | string | Which season this belongs to |
| GameDate | date | The game date |
| Title | string | e.g. "Belfast Giants vs Sheffield Steelers" |
| Deadline | datetime (UTC) | Voting closes at this time (typically 19:00 local) |
| Status | enum | Draft, Open, Closed, Scored |
| CreatedBy | string | Admin user ID |
| CreatedAt | datetime | |

**Status transitions:**
- Draft → Open (admin publishes)
- Open → Closed (deadline passes automatically, or admin manually closes)
- Closed → Scored (admin marks correct answers)
- Open → Scored (admin scores after deadline has passed — skips explicit close)

### Question

A multiple-choice question within a poll.

| Field | Type | Notes |
|-------|------|-------|
| QuestionId | string (ULID) | Unique identifier |
| PollId | string | Parent poll |
| Text | string | The question text |
| SortOrder | int | Display order within the poll |
| CorrectOptionId | string? | Set by admin post-game, null until scored |

### Option

A possible answer to a question.

| Field | Type | Notes |
|-------|------|-------|
| OptionId | string (ULID) | Unique identifier |
| QuestionId | string | Parent question |
| Text | string | The option text |
| SortOrder | int | Display order |

### UserAnswer

A user's selected answer for a question.

| Field | Type | Notes |
|-------|------|-------|
| UserId | string | Cognito user ID |
| QuestionId | string | Which question |
| PollId | string | Which poll (denormalised for queries) |
| SelectedOptionId | string | Their chosen option |
| SubmittedAt | datetime | Last submission/edit time |
| IsCorrect | bool? | Null until scored, then true/false |

### LeaderboardEntry

Aggregated score for a user within a season.

| Field | Type | Notes |
|-------|------|-------|
| UserId | string | Cognito user ID |
| SeasonId | string | Which season |
| DisplayName | string | Denormalised for fast reads |
| TotalPoints | int | Running total of correct answers |
| TotalAnswered | int | Total questions answered |
| LastUpdated | datetime | |

## Business Rules

1. Only admins can create/edit polls and mark correct answers.
2. Users can submit or edit answers only while `Status = Open` AND `now < Deadline`.
3. API rejects answer submissions after the deadline regardless of status.
4. When admin marks a correct answer, all UserAnswers for that question are evaluated and `IsCorrect` is set.
5. When all questions in a poll are scored, the poll status moves to `Scored` and LeaderboardEntry totals are updated.
6. A user can only select one option per question.
7. Seasons are not overlapping — a game date belongs to exactly one season.
8. Users cannot see other users' picks until the poll is Closed or Scored.
9. Tie-breaking: users with the same points share the same rank.
10. Late joiners start from zero — no backfilling of scores.
11. Multiple users can have the admin role (managed via Cognito group/claim).

## Design Decisions

| Decision | Choice | Notes |
|----------|--------|-------|
| Multi-tenancy (groups) | Single group for v1 | Can add GroupId later as a partition prefix |
| Notifications | Future enhancement | Web Push via service worker, not in v1 |
| Tie-breaking | Same rank | Can be changed later — just display logic. Other options: most games answered, first to reach score, accuracy % |
| Pick visibility | Hidden until closed | API won't return others' answers before deadline |
| Late joiners | Start from zero | No backfill |
| Admin model | Multiple admins | Role-based via Cognito |
| Display name | Cognito attribute | Read from JWT claims, denormalised to leaderboard entries |
| Season creation | Auto-created | System derives season from game date (Aug–Apr). Created on first poll if it doesn't exist |
