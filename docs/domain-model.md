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
- Scored → Scored (admin **re-scores**: fixes a wrong correct option, finishes a partly scored poll,
  or retries a pass that failed halfway). Safe to repeat, because a poll's scoring facts are
  overwritten rather than accumulated.

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
| DisplayName | string | Denormalised, captured at submission time from the submitter's own claims - this is the only point it can be resolved correctly, and seeds `LeaderboardEntry.DisplayName` on first scoring |
| QuestionId | string | Which question |
| PollId | string | Which poll (denormalised for queries) |
| SelectedOptionId | string | Their chosen option |
| SubmittedAt | datetime | Last submission/edit time |
| IsCorrect | bool? | Null until scored, then true/false |

### PollScore

What one player earned in one scored poll. Written when a poll is scored and rewritten on every
re-score, so it is always the current truth for that (player, poll) pair.

| Field | Type | Notes |
|-------|------|-------|
| SeasonId | string | Which season |
| PollId | string | Which poll |
| UserId | string | Cognito user ID |
| DisplayName | string | Captured from the player's own answers; refreshed each pass so a bad name self-heals |
| Points | int | One per correct answer |
| Answered | int | Graded answers only — a question with no correct option set is not one the player got wrong |
| ScoredAt | datetime | |

### PollVoid

An admin's decision to void one player's picks for one game day, taking that day's points *and*
answered count off their season total. Deliberately separate from `PollScore`: re-scoring rewrites
that, and must not be able to wipe out a void. A void may be recorded before the poll is scored.

| Field | Type | Notes |
|-------|------|-------|
| SeasonId | string | Which season |
| PollId | string | Which poll |
| UserId | string | Whose picks are voided |
| PollTitle | string | Denormalised, so the leaderboard can name the game day |
| Reason | string | Required, trimmed, max 200 — **shown publicly** |
| VoidedBy | string | Admin user ID |
| VoidedAt | datetime | |

### PointAdjustment

A manual points change on a player's season total.

| Field | Type | Notes |
|-------|------|-------|
| SeasonId | string | Which season |
| AdjustmentId | string | GUID v7 |
| UserId | string | Who is adjusted |
| DisplayName | string | Denormalised — no user directory exists to look a name up in |
| Points | int | Signed. Negative deducts, positive awards. Non-zero, max ±1000 |
| Reason | string | Required, trimmed, max 200 — **shown publicly** |
| CreatedBy | string | Admin user ID |
| CreatedAt | datetime | |

### LeaderboardEntry

A player's standing within a season. **Not stored** — folded on read from the three fact types above
by `SeasonStandings.Build`.

| Field | Type | Notes |
|-------|------|-------|
| UserId | string | Cognito user ID |
| SeasonId | string | Which season |
| DisplayName | string | From the most recently scored poll, so it self-heals |
| EarnedPoints | int | Correct answers from non-voided polls |
| AdjustmentPoints | int | Net of all adjustments; negative when deductions outweigh awards |
| TotalPoints | int | `EarnedPoints + AdjustmentPoints`. **Can be negative** |
| TotalAnswered | int | Graded questions answered, excluding voided polls |
| Adjustments | list | For public display, oldest first |
| Voids | list | For public display, oldest first |
| LastUpdated | datetime | The most recent contributing fact |

Accuracy is measured on `EarnedPoints`, never `TotalPoints`: an adjustment is a sanction, not a wrong
answer, and must not rewrite a hit rate or produce a negative percentage.

## Business Rules

1. Only admins can create/edit polls and mark correct answers.
2. Users can submit or edit answers only while `Status = Open` AND `now < Deadline`.
3. API rejects answer submissions after the deadline regardless of status.
4. When admin marks a correct answer, all UserAnswers for that question are evaluated and `IsCorrect` is set.
5. Scoring a poll records what each player earned as a fact; season totals are folded from those
   facts on read, never accumulated. Scoring the same poll again overwrites rather than double-counting.
6. Admins can void one player's picks for one game day (once voting has closed, and only if the
   player actually made picks), and can apply signed point adjustments to anyone who already has a
   standing. Both carry a required reason shown to every player, and both are undoable — the total
   then returns to exactly what it was.
7. A deduction may take a season total below zero. Negatives are displayed and ranked as-is, not clamped.
8. A user can only select one option per question.
9. Seasons are not overlapping — a game date belongs to exactly one season.
10. Users cannot see other users' picks until the poll is Closed or Scored.
11. Tie-breaking: users with the same points share the same rank.
12. Late joiners start from zero — no backfilling of scores.
13. Multiple users can have the admin role (managed via Cognito group/claim).

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
