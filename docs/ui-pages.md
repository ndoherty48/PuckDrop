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
  - A cancelled poll is never shown here: it drops off the card and "Also open" the moment it's
    cancelled. Players find out from History (below).
  - With several polls open, the card is the next game: the soonest poll still taking picks,
    whether or not the user has picked it. An Open poll past its deadline (waiting to be closed)
    only leads when nothing else is open.
  - **Also open** — every other open poll in date order, as a compact row linking to
    `/poll/{pollId}`: title, game date, time left, and "Not in yet" / "Picks in" / "Picks closed".
    Hidden when only one poll is open.
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
  - Cancelled: the title, game date, a "Cancelled" badge and the reason, and nothing else — no
    questions, no picks (not even the viewer's own) and no submit. Picks may still have been hidden
    when it was cancelled, and with nothing to score they mean nothing. This is where History's
    "View details" lands, and where an old link to the poll ends up.

### Results (`/results/{pollId}`)

**Layout:** Question-by-question breakdown.

- **Header** — Game title, final result context
- **Per question:**
  - Question text
  - Correct answer highlighted in green
  - Each user's pick shown (green tick / red cross)
- **Summary** — Points scored this game day per user
- **Only visible** when poll status is `Scored` — so a cancelled poll, never scored, has no results
  page to explain itself on; that's the Poll page's job (above)

### Leaderboard (`/leaderboard`)

**Layout:** Table/list, mobile-friendly.

- **Season selector** — Dropdown (defaults to current season). *Not built — the page always shows the
  current season.*
- **Table columns:** Rank, Player, Points, Answered, Accuracy %. Answered and Accuracy fold into a
  line under the player's name below 768px.
- **Current user's row** highlighted (not pinned)
- **Sorting:** By effective points descending, then the tie-break (most game days played, then
  head-to-head). Players still level share a rank, shown as "=3" and read by screen readers as
  "Tied 3rd" — on the Home page's top 5 too.
- **Tie-break footnote** — one line under the table states the rule, rather than a note on each tied
  row. Per-row notes were tried first and dropped: they repeated the same sentence on every tied row,
  and a note naming one step read as wrong for players that step didn't separate.
- **Sanctions** — any point adjustments and voided game days are listed under the player's name with
  their reasons, at every width. Public on purpose: a penalty nobody can see the reason for is what
  starts the argument.
- **Negative totals** are shown as-is with a real minus sign (U+2212), and rank below zero. Accuracy
  is computed from earned points, so a deduction can never show a negative hit rate.

### History (`/history`)

**Layout:** List of past game days.

- **Grouped by month** (or just a reverse-chronological list)
- **Per item:** Game title, date, user's score, link to full results
- **Cancelled polls** are listed in date order with the rest: a "Cancelled" badge and the reason in
  place of a score, and a "View details" link to the Poll page. Only polls cancelled after being published
  appear — a Draft that was cancelled was never seen by players, so it isn't here either.
- **Filtered to current season** (with season selector)

### Admin: Polls (`/admin/polls`)

**Layout:** The season's polls in two sections, split by status rather than date, so the page opens
on what still needs doing rather than on the season's finished polls.

- **Upcoming:** `Draft`, `Open` and `Closed` polls, oldest game first, so a `Closed` poll waiting to be
  scored sits at the top once its game has been played. When it's empty it says "All caught up" and
  links to Import fixtures.
- **Past:** `Scored` and `Cancelled` polls — both have nothing left to do. Newest game first, the
  three most recent shown and the rest behind a "Show N earlier polls" toggle, since a recent game is
  the likely reason to re-score. Left out entirely until something has been scored or cancelled.
  A cancelled poll carries a "Cancelled" badge in the status column and its reason, with no picks
  count; a cancelled Draft is listed here too, since this is the only place it can be seen.
- **Players this season** (the picks column's denominator) counts only picks on polls that aren't
  cancelled, so someone whose only picks were on a cancelled game isn't counted.
- Polls on the same date keep the order they were created in (newest first under Past).
- **Columns:** Game date, title, status, picks (picked / players this season), actions
- **Actions:** Edit, Publish, Close voting, Score, View results, Re-score (contextual by status).
  Edit is offered on Draft, Open, Closed and Cancelled polls: cancelling isn't a row action — it lives
  on Edit Poll, where the reason is entered — and neither is restoring.
- **Create poll, Import fixtures and Point adjustments** buttons at top

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
  - Cancelled → the reason, who cancelled it and when (admins only — players see just the reason),
    and **Restore poll**, which puts it back to the status it was cancelled from (Draft, Open or
    Closed) with its questions and picks untouched. Confirmed first, through the shared
    `ConfirmDialogService`. A poll restored to Open after
    its deadline is simply an Open poll past its deadline. The reason can't be edited: restore and
    cancel again to change it.
- **Cancel game** — offered while Draft, Open or Closed, as a secondary destructive button below
  the next step panel (it's the rare path). Opens a `Modal` (the same component as Edit date and
  time) that says the poll will drop off players' home screens and never be scored, and holds the
  reason field: required, and shown to every player (except for a Draft, which players never saw). "Cancel game" / "Keep
  game", never a bare OK/Cancel. Not offered once `Scored` — a game that didn't count must be
  cancelled before it's scored.
- Cancelling and restoring keep the admin on the page, like publishing and closing
- Publishing and closing keep the admin on the page: the status badge changes and the change is
  announced, rather than navigating away
- **Only editable** while Draft or Open — the question editor and Delete buttons go once voting
  has closed, and while Cancelled (restore first to edit)

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
- **Reachable** when poll status is `Closed` (score) or `Scored` (re-score) — never `Cancelled`

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

### Flow 5: Admin cancels a game day

The game doesn't go ahead (postponed, ice problem, travel) after its poll is published.

1. Admin opens Admin: Polls → taps "Edit" on the poll (it's under Upcoming: Open, or Closed if the
   deadline has already passed)
2. Taps "Cancel game" → a confirmation modal opens → enters a reason, e.g. "Postponed — Steelers
   travel issues, rearranged for 14 Jan" → taps "Cancel game"
3. The poll's badge changes to Cancelled; it moves to Past on Admin: Polls
4. Players: the poll is gone from Home; History lists it as Cancelled with the reason. Any picks
   made are kept but shown to nobody, and nothing reaches the leaderboard
5. If the game is rearranged, the admin creates a new poll for the new date (Flow 2) — friends pick
   again. The cancelled poll stays cancelled

### Flow 6: Admin restores a cancelled poll

The wrong game was cancelled, or the game goes ahead after all.

1. Admin opens Admin: Polls → finds the poll under Past → taps "Edit"
2. Taps "Restore poll" → confirms
3. The poll returns to the status it was cancelled from, with its picks intact: back on Home if
   Open, back under Upcoming ready to score if Closed

