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
| Status | enum | Draft, Open, Closed, Scored, Cancelled |
| CreatedBy | string | Admin user ID |
| CreatedAt | datetime | |
| CancelledFromStatus | enum? | What Restore returns it to. Set only while Cancelled |
| CancellationReason | string? | Shown to every player (trimmed, required, ≤200). Set only while Cancelled |
| CancelledBy | string? | Admin user ID. Set only while Cancelled |
| CancelledByName | string? | Admin's display name at the time, for admins to see. Set only while Cancelled |
| CancelledAt | datetime? | Set only while Cancelled |

**Status transitions:**
- Draft → Open (admin publishes)
- Open → Closed (deadline passes automatically, or admin manually closes)
- Closed → Scored (admin marks correct answers)
- Open → Scored (admin scores after deadline has passed — skips explicit close)
- Scored → Scored (admin **re-scores**: fixes a wrong correct option, finishes a partly scored poll,
  or retries a pass that failed halfway). Safe to repeat, because a poll's scoring facts are
  overwritten rather than accumulated.
- Draft / Open / Closed → Cancelled (admin cancels, with a reason, because the game didn't go ahead).
  Never from Scored, so cancelling can't change the leaderboard; a cancelled poll can't be scored,
  published, closed, rescheduled, edited or take picks. A rearranged game is a new poll.
- Cancelled → whatever it was cancelled from (admin restores). Picks were never touched, so they
  come back as they were.

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
| GameDaysPlayed | int | Scored polls with at least one graded pick, excluding voided ones. First tie-break |
| Rank | int | Competition ranking (1, 2, 2, 4) after the tie-break |
| IsRankShared | bool | Another player holds the same rank — shown as "=3" |
| TieBreak | object? | Who the player is level on points with, whether game days played and/or head-to-head separated them, and their head-to-head record. Null when no one shares their total |
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
11. Tie-breaking: players level on total points are separated by most game days played, then
    head-to-head — over the game days each pair both played, whoever scored more takes the day;
    among three or more, one mini-league ranked on net days taken (won minus lost). Anyone still
    level shares a rank. A point adjustment never affects the tie-break; a void removes that game
    day from it. See `GLOSSARY.md` for the terms.
12. Late joiners start from zero — no backfilling of scores.
13. Multiple users can have the admin role (managed via Cognito group/claim).

## Design Decisions

| Decision | Choice | Notes |
|----------|--------|-------|
| Multi-tenancy (groups) | Single group for v1 | Can add GroupId later as a partition prefix |
| Notifications | Future enhancement | Web Push via service worker, not in v1 |
| Tie-breaking | Game days played, then head-to-head, then shared rank | Derived on read, nothing stored, so cheap to change. Rejected: accuracy (with one question per poll, on equal points it just means fewer game days played), game-day wins and best game day (meaningless when a game day is worth 0 or 1), first to reach the score and recent form (need facts in time order, which re-scoring breaks). Known limit: with one question per poll, players level on points who played exactly the same game days are always level on head-to-head too |
| Pick visibility | Hidden until closed | API won't return others' answers before deadline |
| Late joiners | Start from zero | No backfill |
| Admin model | Multiple admins | Role-based via Cognito |
| Display name | Cognito attribute | Read from JWT claims, denormalised to leaderboard entries |
| Season creation | Auto-created | System derives season from game date (Aug–Apr). Created on first poll if it doesn't exist |
