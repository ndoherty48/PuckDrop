# UI Pages

## Page Inventory

| Page | Route | Auth | Role | Purpose |
|------|-------|------|------|---------|
| Home | `/` | User | All | Dashboard — next game, active poll CTA, recent results |
| Poll | `/poll/{pollId}` | User | All | View questions, submit/edit answers |
| Results | `/results/{pollId}` | User | All | Post-game results — who got what right |
| Leaderboard | `/leaderboard` | User | All | Season standings |
| History | `/history` | User | All | Past game days and their results |
| Admin: Polls | `/admin/polls` | User | Admin | List/manage all polls |
| Admin: Create Poll | `/admin/polls/create` | User | Admin | Create a new game day poll |
| Admin: Edit Poll | `/admin/polls/{pollId}` | User | Admin | Edit poll, add/edit questions |
| Admin: Score Poll | `/admin/polls/{pollId}/score` | User | Admin | Mark correct answers post-game, or re-score |
| Admin: Point Adjustments | `/admin/leaderboard` | User | Admin | Deduct or award points, with a public reason |
| Login Redirect | `/authentication/login` | None | — | Triggers Cognito OIDC flow |
| Logout | `/authentication/logout` | None | — | Clears session |
| Not Authorized | `/unauthorized` | None | — | Shown when user lacks permission |

## Page Descriptions

### Home (`/`)

**Layout:** Single column, mobile-first.

- **Next Game Card** — Game title, date, countdown to deadline
  - If poll is Open: "Make your picks →" button
  - If poll is Closed/Scored: "View results →" button
  - If no active poll: "No upcoming poll" message
- **Quick Leaderboard** — Top 5 with user's own position highlighted
- **Recent Results** — Last 2-3 scored polls with user's score

### Poll (`/poll/{pollId}`)

**Layout:** Single column, card per question.

- **Header** — Game title, deadline countdown (or "Voting closed" badge)
- **Questions** — For each question:
  - Question text
  - Radio button group for options
  - Selected option highlighted
- **Submit button** — Saves all answers at once
  - Disabled if past deadline
  - Shows "Saved ✓" confirmation
- **State handling:**
  - Before deadline: editable, submit enabled
  - After deadline: read-only, shows what user picked

### Results (`/results/{pollId}`)

**Layout:** Question-by-question breakdown.

- **Header** — Game title, final result context
- **Per question:**
  - Question text
  - Correct answer highlighted in green
  - Each user's pick shown (green tick / red cross)
- **Summary** — Points scored this game day per user
- **Only visible** when poll status is `Scored`

### Leaderboard (`/leaderboard`)

**Layout:** Table/list, mobile-friendly.

- **Season selector** — Dropdown (defaults to current season). *Not built — the page always shows the
  current season.*
- **Table columns:** Rank, Player, Points, Answered, Accuracy %. Answered and Accuracy fold into a
  line under the player's name below 768px.
- **Current user's row** highlighted (not pinned)
- **Sorting:** By effective points descending; ties share a rank
- **Sanctions** — any point adjustments and voided game days are listed under the player's name with
  their reasons, at every width. Public on purpose: a penalty nobody can see the reason for is what
  starts the argument.
- **Negative totals** are shown as-is with a real minus sign (U+2212), and rank below zero. Accuracy
  is computed from earned points, so a deduction can never show a negative hit rate.

### History (`/history`)

**Layout:** List of past game days.

- **Grouped by month** (or just a reverse-chronological list)
- **Per item:** Game title, date, user's score, link to full results
- **Filtered to current season** (with season selector)

### Admin: Polls (`/admin/polls`)

**Layout:** Table of all polls.

- **Columns:** Game date, title, status, # questions, actions
- **Actions:** Edit, Publish, Close, Score (contextual by status)
- **Create button** at top

### Admin: Create Poll (`/admin/polls/create`)

**Layout:** Form.

- **Fields:**
  - Title (text)
  - Game date (date picker)
  - Deadline (datetime picker, defaults to game date 19:00)
  - Season (auto-selected based on game date)
- **Save as Draft** → goes to Edit Poll to add questions

### Admin: Edit Poll (`/admin/polls/{pollId}`)

**Layout:** Poll details + question builder.

- **Poll fields** — Title, deadline (editable if Draft/Open)
- **Questions list:**
  - Add question button
  - Per question: text input + options list (add/remove/reorder)
  - Delete question button
- **Next step panel** — the poll's next action, by status, so a poll is managed from here rather
  than back on Manage polls:
  - Draft → **Publish poll** (Draft → Open). Stays enabled with no questions and says what's
    missing; the API rejects an empty poll too
  - Open → **Close voting** (Open → Closed) + a link to the live poll
  - Closed → link to **Score poll**
  - Scored → link to the results
- Publishing and closing keep the admin on the page: the status badge changes and the change is
  announced, rather than navigating away
- **Only editable** while Draft or Open — the question editor and Delete buttons go once voting
  has closed

### Admin: Score Poll (`/admin/polls/{pollId}/score`)

**Layout:** Question-by-question scoring.

- **Per question:**
  - Question text displayed
  - Options shown as selectable buttons/radio
  - Admin picks the correct answer
- **Submit scores** — triggers scoring flow, updates leaderboard
- **Re-scoring** — reachable from the Manage polls row of a `Scored` poll. The screen pre-selects the
  answers the poll already has, so correcting one question doesn't blank the rest, and a partly
  scored poll shows exactly which questions are still unset. Wording switches to "Ready to
  re-score?" / "Update scores", and the confirm warns the leaderboard is recalculated.
- **Reachable** when poll status is `Closed` (score) or `Scored` (re-score)

### Admin: Point Adjustments (`/admin/leaderboard`)

**Layout:** An adjustment form above a standings table. Linked from Manage polls.

- **Adjust a player** — player picker, signed points, required reason. Negative deducts, positive
  awards; the reason is shown to everyone.
- **Player picker** offers only players who already have a standing. Names are only ever captured from
  a player's own answers and there is no user directory, so there is nobody else to adjust.
- **Standings table:** Player, Earned, Adjustments, Total — split out so it's clear where a total came
  from before changing it.
- **Each adjustment** is listed under the player's name with a Remove control (confirmed first).
  Removing restores the total exactly.
- **Voided game days** are shown here too, read-only, with a pointer to the poll's results page where
  voiding is done.

### Admin controls on Results (`/results/{pollId}`)

Voiding a player's picks for a game day happens on that poll's results page, where the picks
themselves are visible.

- **Void picks / Restore picks** toggle per player, admin-only, below the normal results
- Voiding opens an inline reason field; the reason is required and shown publicly
- A voided player gets a "Voided" badge beside their name everywhere the page names them
- Restoring is confirmed first, and puts the game day back towards the season total

## User Flows

### Flow 1: User submits picks

1. User opens app → Home shows active poll card
2. Taps "Make your picks" → navigates to Poll page
3. Selects one option per question
4. Taps "Submit" → API saves answers
5. Can return and edit until deadline

### Flow 2: Admin creates and publishes a poll

1. Admin goes to Admin: Polls → taps "Create"
2. Fills in title, date, deadline → saves as Draft
3. Redirected to Edit Poll → adds questions and options
4. Taps "Publish" → poll becomes Open, users can see it

### Flow 3: Post-game scoring

1. Game ends, admin opens Admin: Polls
2. Finds the Closed poll → taps "Score"
3. Selects correct answer for each question
4. Taps "Submit scores" → system evaluates all answers, updates leaderboard
5. Users see Results page and updated Leaderboard

### Flow 4: Viewing results

1. After scoring, user opens app → Home shows "View results" for last game
2. Taps through → sees per-question breakdown
3. Can also check Leaderboard for updated standings
