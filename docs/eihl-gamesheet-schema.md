# EIHL Official Gamesheet — Data Schema Reference

> Supporting reference for [docs/ai-features.md](ai-features.md)'s idea #4 (auto-suggested
> scoring). That doc covers the product decision and the still-open questions (fixture→`gameId`
> mapping, publish timing, ToS); this one is the field-level schema for whoever actually implements
> it, so it doesn't need to be re-derived by re-fetching and re-parsing the page from scratch.
> Verified twice against the live site (once during the original investigation, re-verified while
> writing this doc after a session/scratchpad reset) — not written from memory.

## Source

`https://eihlhq.co.uk/pdf/print/de-html/{gameId}` — a print-formatted HTML page (not an actual
PDF, despite the URL path), served with no `<title>` tag. `gameId` is a plain sequential integer
across the whole league's fixture history, not opaque or per-team-scoped — confirmed by fetching
adjacent IDs around a known one (`4351`/`4352`/`4353` resolved to three different real fixtures
from the same few days: Coventry Blaze vs Cardiff Devils, Belfast Giants vs Coventry Blaze, Glasgow
Clan vs Dundee Stars).

Worked example throughout this doc: `gameId=4352`, Belfast Giants 7–4 Coventry Blaze, EIHL
2025/26, 01.04.2026, SSE Arena Belfast.

## Document structure

The raw HTML contains the whole print document as one page-source (no client-side rendering
needed to get the data — a plain `HttpClient.GetStringAsync` + HTML parser is enough, no headless
browser required). Named `<table id="...">` elements mark each section:

| `id` | Contents |
|---|---|
| `heading` | League name, "Official Gamesheet" title |
| `topinfo` | Competition, venue, date, start time, attendance, "Gameday" number |
| `homestats` | Home team: roster, goals, penalties, coaching staff |
| `awaystats` | Away team: same shape as `homestats` |
| `summary` | Period-by-period totals, goalie summary/changes, officials |
| `info`, `home`, `visitor`, `teams`, `signature` | Repeated per-team detail/signature sheets, appearing to duplicate data already in `homestats`/`awaystats`/`summary` for print pagination - **not independently explored**; treat `homestats`/`awaystats`/`summary` as the primary source and these as a fallback only if something's missing there. |

The whole `homestats`/`awaystats`/`summary` block (everything needed) appeared within roughly the
first 2,600 lines of the raw HTML in the worked example.

## `topinfo` — game header

| Field | Example value |
|---|---|
| Competition | `EIHL 2025/26` |
| Place | `SSE Arena, Belfast` |
| Date | `01.04.2026` |
| Start | `19:00 (UTC)` |
| Attendance | `6973` |
| Gameday | `257` (a league-wide sequential game counter, distinct from `gameId`) |

## `homestats` / `awaystats` — per-team

Each is labeled `Home (A)` / `Visitor (B)` respectively, followed by the team name (e.g. `Belfast
Giants`, `Coventry Blaze`).

**Roster table** — one row per player:

| Column | Meaning | Example |
|---|---|---|
| `No.` | Jersey number | `73` |
| `Name` | `SURNAME Firstname` | `KUPSKY Jake` |
| `Pos.` | `GK` / `D` / `F` | `GK` |
| `L` | Unconfirmed - appears alongside SoG, likely a per-player stat column (not verified what "L" stands for) | `1` |
| `SoG` | Shots on goal | `35` |

**Goals table** — one row per goal scored *by this team*:

| Column | Meaning | Example (goal 1, home) |
|---|---|---|
| `#` | Goal sequence number (per team, not game-wide) | `1` |
| `Time` | Game clock `MM:SS` | `16:58` |
| `G` | Scorer's jersey number | `63` |
| `A1` | Primary assist jersey number (blank if unassisted) | `71` |
| `A2` | Secondary assist jersey number (blank if none) | `23` |
| `GS` | Game state at the time - `5/5` even strength, `5/4` power play, `4/5` shorthanded, etc. | `5/5` |
| `P1`-`P6` | Scoring team's six on-ice players (by jersey number) | `63, 71, 36, 91, 23, 58` |
| `N1`-`N6` | Opposing team's on-ice players at that moment | `77, 10, 13, 86` (fewer than 6 when short-handed) |

**Penalties table** — one row per penalty taken *by this team*:

| Column | Meaning | Example |
|---|---|---|
| `Time` | Game clock when called | `09:29` |
| `No.` | Player's jersey number, or `T` for a team/bench penalty | `7` |
| `Min` (home) / `Min.` (away - inconsistent label between the two tables in the raw markup) | Penalty length in minutes | `2` |
| `Penalty` | Infraction code (abbreviated, all-caps - `ABUSE`, `HOLD`, `CROSS`, `FIGHT`, `HI-ST`, `TRIP`, `BOARD`, `HOOK`, `HO-ST` seen in the worked example; not a documented enum, would need a lookup table built from observed values) | `HOLD` |
| `Start` / `End` | Penalty's active window on the game clock | `09:29` / `11:29` |

Coaching staff (plain text, not tabular): `Team Manager:`, `Head Coach:`, `Assistant Coach:` -
name after each label (`Team Manager:` can be blank).

## `summary` — game-wide

**Period-by-period table** (rows: `1`, `2`, `3`, `TOTAL`; each cell is `home:away`):

| Column | Meaning |
|---|---|
| `Period` | Period number or `TOTAL` |
| `G A:B` | Goals |
| `SoG A:B` | Shots on goal |
| `PIM A:B` | Penalty minutes |
| `PPG A:B` | Power-play goals |
| `SHG A:B` | Shorthanded goals |

`TOTAL` row's `G A:B` is the final score (`7:4` in the worked example).

**Goalie summary** (`GKA1`/`GKA2`/`EGA` for home, `GKB1`/`GKB2`/`EGB` for away) - appears to track
up to two goalies' save/shot figures per team plus empty-net-goals-against (`EG*`); the raw values
extracted were numeric but the exact per-column meaning of `GKA1` vs `GKA2` wasn't fully resolved
from this one example (both Belfast and Coventry only used two goalies each, one column pair
mostly empty) - worth checking against a second, differently-shaped example before relying on this
table.

**Goalie changes table** (`No. A` / `Min` / `GA` for home, `No. B` / `Min` / `GA` for away) - one
row per goalie who played, with minutes played and goals-against:

| No. A | Min | GA | No. B | Min | GA |
|---|---|---|---|---|---|
| 73 | 54:08 | 3 | 20 | 54:08 | 7 |
| 35 | 05:52 | 1 | 33 | 05:52 | 0 |

Plus plain-text fields: `Local start time:`, `Local end time:`, `Timeout A:`, `Timeout B:`, and a
`Time` / `GKA` / `GKB` grid showing which goalie was in net at `00:00`, each change point, and
`60:00`.

**Officials** (plain text labels, not tabular):

`Referee:`, `Referee 2:`, `Goal judge:`, `Doctor:`, `Scorekeeper:`, `Linesmen:`, `Supervisor:`,
`Timekeeper:` - each followed by a name, most (referees/linesmen) prefixed with a jersey-style `#`
number and a nationality code in parens, e.g. `#25 Andy Dalton (UK)`. `Linesmen:` holds two names,
comma-separated, in one field. Trailing `Sign. Team A:` / `Sign. Team B:` / `Sign. Scorekeeper:` /
`Sign. Referee:` fields are blank signature-line placeholders in the HTML, not real data.

## What this schema would answer for PuckDrop poll questions

Directly scoreable from one gamesheet fetch: match winner, total-goals over/under, largest-margin
questions, first-goal-scorer, period-with-most-goals, power-play-goals over/under, "will there be
a fight" (any `Penalty == FIGHT` row), a named player's shots-on-goal over/under. Anything finer
than what's in the tables above (e.g. shot location, hit counts) isn't in this data source at all.

## Known gaps in this investigation

- Only one gamesheet was ever actually fetched and parsed (`gameId=4352`) - column meanings that
  looked ambiguous (`L` on the roster, `GKA1`/`GKA2` on the goalie summary) are stated as such
  above rather than guessed; confirming them needs a second, differently-shaped example (a shutout,
  a three-goalie game, an OT/shootout game - this one had neither).
- Shootout/overtime formatting was never observed (this game ended 7-4 in regulation) - how the
  schema represents an OT winner or a shootout-goal is unknown.
- `info`/`home`/`visitor`/`teams`/`signature` tables were seen to exist but never parsed for
  content - only `homestats`/`awaystats`/`summary` were actually read field-by-field.
