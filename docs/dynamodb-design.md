# DynamoDB Table Design

## Overview

Single-table design. One table (`PuckDrop`) holds all entities. Access patterns drive the key design.

## Table

| Setting | Value |
|---------|-------|
| Table name | `PuckDrop` |
| Billing mode | PAY_PER_REQUEST |
| Partition key | `PK` (String) |
| Sort key | `SK` (String) |

## Access Patterns

| # | Access Pattern | PK | SK | Notes |
|---|----------------|----|----|-------|
| 1 | Get season by ID | `SEASON#{seasonId}` | `SEASON` | |
| 2 | List all seasons | `SEASONS` | `SEASON#{seasonId}` | |
| 3 | Get poll by ID | `POLL#{pollId}` | `POLL` | |
| 4 | List polls for a season | `SEASON#{seasonId}` | `POLL#{gameDate}#{pollId}` | Sorted by game date |
| 5 | Get questions for a poll | `POLL#{pollId}` | `Q#{sortOrder}#{questionId}` | Sorted by order |
| 6 | Get options for a question | `POLL#{pollId}` | `OPT#{questionId}#{sortOrder}` | Grouped under poll |
| 7 | Get user's answers for a poll | `USERANSWER#{userId}#{pollId}` | `Q#{questionId}` | |
| 8 | Get all answers for a poll (results) | GSI1 | | See GSI1 below |
| 9 | Get a season's scoring facts (leaderboard) | `LEADERBOARD#{seasonId}` | `U#` prefix | One query; folded into standings in memory |
| 10 | Get active/open polls | GSI2 | | See GSI2 below |
| 11 | Get one player's facts | `LEADERBOARD#{seasonId}` | `U#{userId}#` prefix | Shared prefix, so one query covers all three fact types |

## Item Schemas

### Season Item

```
PK: SEASON#{seasonId}
SK: SEASON
GSI1PK: -
GSI1SK: -
```
Attributes: `seasonId`, `name`, `startDate`, `endDate`

Also written to the seasons collection:
```
PK: SEASONS
SK: SEASON#{seasonId}
```

### Poll Item

```
PK: POLL#{pollId}
SK: POLL
GSI1PK: POLL#{pollId}
GSI1SK: POLL
GSI2PK: SEASON#{seasonId}#STATUS#{status}
GSI2SK: DEADLINE#{deadline}
```
Attributes: `pollId`, `seasonId`, `gameDate`, `title`, `deadline`, `status`, `createdBy`, `createdAt`

Also written to the season's poll collection:
```
PK: SEASON#{seasonId}
SK: POLL#{gameDate}#{pollId}
```

### Question Item

```
PK: POLL#{pollId}
SK: Q#{sortOrder}#{questionId}
GSI1PK: POLL#{pollId}
GSI1SK: Q#{questionId}
```
Attributes: `questionId`, `pollId`, `text`, `sortOrder`, `correctOptionId`

### Option Item

```
PK: POLL#{pollId}
SK: OPT#{questionId}#{sortOrder}
GSI1PK: POLL#{pollId}
GSI1SK: OPT#{questionId}#{optionId}
```
Attributes: `optionId`, `questionId`, `text`, `sortOrder`

### UserAnswer Item

```
PK: USERANSWER#{userId}#{pollId}
SK: Q#{questionId}
GSI1PK: POLL#{pollId}
GSI1SK: ANSWER#{userId}#{questionId}
```
Attributes: `userId`, `displayName`, `pollId`, `questionId`, `selectedOptionId`, `submittedAt`, `isCorrect`

### Scoring fact items

There is **no stored leaderboard total**. A season's standings are folded on read from three kinds of
fact, all in the season's `LEADERBOARD#{seasonId}` partition under a shared `U#{userId}#` prefix so
one `begins_with` query returns everything for a player (or, unprefixed, the whole season):

```
PK: LEADERBOARD#{seasonId}   SK: U#{userId}#POLL#{pollId}
```
Attributes: `userId`, `seasonId`, `pollId`, `displayName`, `points`, `answered`, `scoredAt`

What one player earned in one scored poll. Rewritten on every re-score, so it is always the current
truth for that pair. `answered` counts only *graded* answers, so a partly scored poll doesn't tank
anyone's accuracy.

```
PK: LEADERBOARD#{seasonId}   SK: U#{userId}#VOID#{pollId}
```
Attributes: `userId`, `seasonId`, `pollId`, `pollTitle`, `reason`, `voidedBy`, `voidedAt`

An admin voiding one player's picks for one game day. Kept **separate from the score fact** on
purpose: re-scoring rewrites that, and must not be able to wipe out a void. It also means a void can
be recorded before the poll is scored.

```
PK: LEADERBOARD#{seasonId}   SK: U#{userId}#ADJ#{adjustmentId}
```
Attributes: `userId`, `seasonId`, `adjustmentId`, `displayName`, `points`, `reason`, `createdBy`, `createdAt`

A manual points adjustment. `points` is signed; a deduction may take a total below zero, which is
displayed and ranked as-is rather than clamped. `displayName` is denormalised because names are only
ever captured from a player's own answers and there is no user directory.

The fold lives in `PuckDrop.Domain/Standings/SeasonStandings.cs`:
`total = Σ points (non-voided polls) + Σ adjustments`, `answered = Σ answered (non-voided polls)`,
and accuracy is measured on earned points only. Because every total is derived, removing a void or an
adjustment restores it exactly — there is no delta to reverse.

> An earlier design stored a running total at `SK: SCORE#{invertedPoints}#{userId}`, with
> `invertedPoints = 999999 - totalPoints` so ascending sort order gave descending points. That is
> gone: it could not express a subtraction, and a negative total produced a 7-character key that
> sorted *first* rather than last.

## Global Secondary Indexes

### GSI1 — Poll-centric queries

| Setting | Value |
|---------|-------|
| Partition key | `GSI1PK` (String) |
| Sort key | `GSI1SK` (String) |
| Projection | ALL |

**Used for:**
- Get all answers for a poll (results): `GSI1PK = POLL#{pollId}`, `GSI1SK begins_with ANSWER#`
- Get poll + questions + options in one query: `GSI1PK = POLL#{pollId}`

### GSI2 — Active poll lookup

| Setting | Value |
|---------|-------|
| Partition key | `GSI2PK` (String) |
| Sort key | `GSI2SK` (String) |
| Projection | ALL |

**Used for:**
- Get open polls for current season: `GSI2PK = SEASON#{seasonId}#STATUS#Open`, `GSI2SK begins_with DEADLINE#`
- When poll status changes, the GSI2PK value changes → item disappears from the "Open" partition automatically.

## Scoring Flow

When admin calls `POST /polls/{pollId}/score`:

1. Read all questions for the poll (PK = `POLL#{pollId}`, SK begins_with `Q#`)
2. Update each question's `correctOptionId`
3. Query all user answers (GSI1PK = `POLL#{pollId}`, GSI1SK begins_with `ANSWER#`)
4. For each answer, set `isCorrect = (selectedOptionId == correctOptionId)`
5. Aggregate graded points per user
6. Write one `U#{userId}#POLL#{pollId}` fact per player (a `Put`, so a re-score overwrites)
7. Update poll status to `Scored` — **last**, so a pass that fails partway leaves the poll `Closed`
   and simply re-runnable. This also moves it out of the GSI2 "Open" partition.

Every write uses a deterministic key, so the whole call is idempotent: re-scoring a poll — to fix a
wrong correct option, finish a partly scored one, or retry a failed pass — overwrites rather than
double-counting. Voiding and adjusting are single writes on the same model, and undoing either is a
single conditional delete (conditional, so undoing something absent is a 404 rather than a silent
success).

## Capacity Considerations

At friend-group scale (~10-20 users, ~30 games/season):
- Reads: ~50 RCU peak during results reveal
- Writes: ~20 WCU peak during scoring
- Storage: < 1 MB total

PAY_PER_REQUEST is ideal — cost will be effectively zero.
