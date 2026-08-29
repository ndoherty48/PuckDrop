# Multi-Tenancy (Future Consideration)

> **Status: not planned.** This is a design sketch for if/when it's needed, not a committed
> roadmap item. Nothing here should block or shape unrelated work. See the "Recommendation"
> section for when it's actually worth picking up.

## Current model: single-tenant, one deployment per friend group

PuckDrop today has no concept of a group/tenant boundary. One deployment serves exactly one
friend group:

- **Auth** — admin is a single, deployment-wide `ClaimTypes.Role` "admin" claim (normalized from
  whichever provider is configured — see CLAUDE.md's Auth section), checked by one global
  `AdminPolicy` in `ApiServiceCollectionExtensions.AddApis`. There's no "admin of X" — a user is
  an admin everywhere in the deployment or nowhere.
- **Data** — nothing carries a tenant ID. `Season`, `GameDayPoll`, `LeaderboardEntry` etc. are all
  keyed as if there's exactly one group in the table (e.g. `SEASONS` is a single global
  collection, `LEADERBOARD#{seasonId}` is the top-level leaderboard partition — see
  `docs/dynamodb-design.md`).
- **UI** — no group switcher exists or is implied anywhere in the page set (`docs/ui-pages.md`).

This is a legitimate architecture, not a corner cut — plenty of self-hosted open-source apps work
this way ("deploy your own copy for your own group"), and it's a large part of why the rest of
the app has stayed simple.

## What "adding a League" would actually mean

The trigger for this doc was: *would it be worth adding a `League` concept with join/leave, so
one deployment can host multiple independent friend groups?* That is a genuine multi-tenancy
retrofit — it touches every layer, not just the UI.

### Option A — True multi-tenant leagues

One deployment, many independent friend groups. Each league has its own admins, season, polls,
and leaderboard. Users join via invite code/link and can belong to more than one league.

**Domain changes**
- New `League` entity: `LeagueId`, `Name`, `InviteCode` (or invite link/token), `CreatedBy`,
  `CreatedAt`.
- New `LeagueMembership` entity: `LeagueId`, `UserId`, `Role` (`Admin` | `Member`), `JoinedAt`.
  Admin becomes membership data instead of a deployment-wide IdP claim.
- `Season`, `GameDayPoll`, `LeaderboardEntry` all gain a `LeagueId` field.

**DynamoDB key changes** (see `docs/dynamodb-design.md` and `DynamoDbKeys.cs` for the current
scheme) — every existing key prefix needs a `LEAGUE#{leagueId}#` segment ahead of it:

| Access pattern | Current key | Multi-tenant key |
|---|---|---|
| Get season | `PK=SEASON#{seasonId}` | `PK=LEAGUE#{leagueId}#SEASON#{seasonId}` |
| List seasons | `PK=SEASONS` | `PK=LEAGUE#{leagueId}#SEASONS` |
| Get poll | `PK=POLL#{pollId}` | `PK=LEAGUE#{leagueId}#POLL#{pollId}` |
| Leaderboard | `PK=LEADERBOARD#{seasonId}` | `PK=LEAGUE#{leagueId}#LEADERBOARD#{seasonId}` |
| Active/open polls (GSI2) | `SEASON#{seasonId}#STATUS#{status}` | `LEAGUE#{leagueId}#SEASON#{seasonId}#STATUS#{status}` |

Plus a **new** access pattern that doesn't exist today: "leagues a user belongs to" — likely a
GSI keyed on `USER#{userId}` → `LEAGUE#{leagueId}`, needed to render the league switcher and to
authorize any request.

**Auth changes** — `AdminPolicy` as it exists today (a single ASP.NET Core authorization policy
keyed off an IdP claim) stops making sense once "admin" is per-league. Every write endpoint needs
to resolve `(userId, leagueId) → role` via `LeagueMembership` rather than a static policy check.
This is closer to resource-based authorization than the current claims-based policy.

**API changes** — nearly every controller/service method gains a `leagueId` route segment or
parameter (`PollService`, `AnswerService`, `ScoringService`, `ResultsService`,
`LeaderboardService`, `SeasonService` all become league-scoped). New endpoints: create league,
join league (by invite code), leave league, list my leagues.

**UI changes** — league switcher (likely in the nav), join-by-invite-code flow, create-league
flow, leave-league action. Every page that currently assumes "the" active season/poll/leaderboard
needs to instead operate within the currently-selected league.

**Cost**: a multi-week rewrite of the data layer and auth model, done once, but every future
feature has to be written league-aware from that point on — permanently more surface area, not
just a one-time migration cost.

### Option B — Invite-only access control, still single global group

If the actual pain point is "I don't want this open to anyone who signs up" rather than "I want
multiple independent groups," a much smaller change covers it: gate signup with an invite
code/allowlist at the IdP (both Cognito and Keycloak support required-registration-code or
admin-pre-created-account flows) plus, if self-serve approval is wanted, a lightweight
"approved" flag rather than a full `LeagueMembership` model.

No key redesign, no per-tenant scoping of `Season`/`Poll`/`Leaderboard`, no rewrite of the auth
policy shape. Doesn't enable running multiple *independent* groups off one deployment — a second
friend group still deploys their own stack, which (CDK + Aspire already exist) is not a big lift
for a technical friend to do.

None of Option B's work is wasted if Option A is picked up later — it doesn't foreclose it.

### Option C — Do nothing, revisit if actually needed

Keep the current single-tenant, deploy-per-group model. Revisit if and when a second, unrelated
friend group actually wants in on the same deployment.

## Recommendation

Don't take on Option A speculatively. It's a lot of structural risk (rewriting the key design of
a working app, replacing the auth model) for a capability — multiple unrelated friend groups
sharing one instance — that isn't required by the stated goal ("open-source game day poll app for
friend groups", `CLAUDE.md`). It's the right call only if PuckDrop is meant to become something
other friend groups sign up to *inside your deployment*, i.e. a real SaaS-shaped product rather
than a self-hosted app.

If the itch is really about access control, Option B solves that cheaply today without closing
off Option A later.

If Option A is ever greenlit, doing it early (small codebase, no real user data yet) is
considerably cheaper than doing it after the single-tenant key design and auth model are load-
bearing for live data.
