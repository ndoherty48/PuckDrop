# API Design

## Overview

RESTful API served via AWS Lambda behind API Gateway (HTTP API v2). All endpoints prefixed with `/puckdrop`.

## Authentication

- Cognito User Pool issues JWTs
- API Gateway validates the JWT (authorizer)
- Lambda reads claims from the validated token (sub = userId, custom:role = admin/user)
- No anonymous access except health check

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
| GET | `/polls/{pollId}` | User | Get poll with questions and options |
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

This triggers scoring: evaluates all UserAnswers, sets IsCorrect, updates leaderboard.

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
    { "userId": "...", "displayName": "Nick", "totalPoints": 12, "totalAnswered": 15, "rank": 1 },
    { "userId": "...", "displayName": "Dave", "totalPoints": 10, "totalAnswered": 14, "rank": 2 }
  ]
}
```

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
