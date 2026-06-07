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
| 9 | Get leaderboard for a season | `LEADERBOARD#{seasonId}` | `SCORE#{totalPoints (zero-padded inverted)}#{userId}` | Sorted by points desc |
| 10 | Get active/open polls | GSI2 | | See GSI2 below |

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
Attributes: `userId`, `pollId`, `questionId`, `selectedOptionId`, `submittedAt`, `isCorrect`

### LeaderboardEntry Item

```
PK: LEADERBOARD#{seasonId}
SK: SCORE#{invertedPoints}#{userId}
```
Attributes: `userId`, `seasonId`, `displayName`, `totalPoints`, `totalAnswered`, `lastUpdated`

`invertedPoints` = zero-padded `999999 - totalPoints` so that DynamoDB's ascending sort order gives descending points.

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
5. Aggregate points per user
6. Update each user's LeaderboardEntry (read current → add new points → write with new inverted sort key)
7. Update poll status to `Scored`, which moves it out of the GSI2 "Open" partition

## Capacity Considerations

At friend-group scale (~10-20 users, ~30 games/season):
- Reads: ~50 RCU peak during results reveal
- Writes: ~20 WCU peak during scoring
- Storage: < 1 MB total

PAY_PER_REQUEST is ideal — cost will be effectively zero.
