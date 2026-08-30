# Load, Stress & Smoke Testing (Future Plan)

> **Status: planned, not started.** Unlike `docs/multi-tenancy.md`, this isn't "only if ever
> needed" — there's real intent to build this, mostly to learn how PuckDrop's Lambda/API
> Gateway/DynamoDB stack actually behaves under concurrency and failure, not because a
> friend-group hockey pool has a real capacity problem. Tracked here so the shape and the
> operational prerequisites are decided before any code gets written.

## Why none of this runs against `aspire start`

All three of load, stress, and smoke testing here target a real deployed environment
(`aspire deploy`'d — real Lambda, real API Gateway, real DynamoDB, real Cognito), never the local
Aspire stack. This isn't a style preference — it was proven necessary while building
`tests/PuckDrop.E2ETests/`: the local AWS Lambda Service Emulator (`lambda-test-tool`, via
`Aspire.Hosting.AWS`) processes **one invocation at a time**, confirmed by checking directly (no
concurrency flag on the CLI, no such setting anywhere in `Aspire.Hosting.AWS.dll`). Pointing a load
or stress test at it wouldn't measure anything about how the real app scales — it would just
measure the emulator's own request queue, which is exactly the flakiness `PuckDrop.E2ETests`
already fights. Real Lambda concurrency scaling, real API Gateway throttling, real DynamoDB
on-demand capacity behavior — none of that exists locally.

## Shared prerequisites, before any of the three can start

- **A deployed environment that isn't carrying real friend-group data.** Running these against the
  production stack risks corrupting real seasons/polls/leaderboard data (load and stress testing
  write real poll answers) and, for stress testing specifically, risks a real outage for real
  users mid-test. Needs either a dedicated `staging`/`load-test` CDK deployment, separate from
  whatever the actual friend group uses, or running these in a maintenance window against
  production with an explicit throwaway season only these tools touch.
- **Dedicated Cognito test users.** All three need real Cognito-issued JWTs (every endpoint except
  `/auth-config` requires one). Needs a handful of test accounts provisioned in the real user pool
  (at least one admin, several non-admin) with credentials in a secrets manager (AWS Secrets
  Manager, or CI secrets if these ever run there) — never hardcoded, never the real friend group's
  accounts.
- **Cost and safety guardrails.** This is a real AWS account being billed real money per DynamoDB
  request and Lambda invocation — a runaway or misconfigured run isn't just "the test failed," it
  can show up as a real charge. Set a hard cap on virtual users and run duration before ever
  running anything unsupervised, and put a budget alarm on the account this targets regardless.

## Load testing

Verifies the app behaves correctly and within acceptable latency under **expected** usage —
sized to what a friend-group hockey pool actually sees, which is small (dozens of concurrent
users at the absolute peak), not an attempt to prove it survives Black Friday traffic.

**Tool**: [NBomber](https://nbomber.com/) — a .NET-native load testing framework, fits this repo's
all-.NET ecosystem better than a JS-based tool like k6 or Artillery would. Lives at
`tests/PuckDrop.LoadTests/` (`OutputType=Exe`, matching `PuckDrop.E2ETests`' own shape), pointed at
a deployed base URL via a required CLI argument/config value — never a default, so it can't
accidentally run against the wrong environment.

**Token provider**: the same *shape* of problem `PuckDrop.E2ETests`' session-reuse helper solves,
but for bearer tokens instead of browser sessions — authenticate each seeded test user once via
Cognito's `AdminInitiateAuth`, cache the resulting JWTs, refresh as they approach expiry.

**Scenarios**, grounded in what this app actually is rather than generic CRUD hammering:

1. **Steady-state reads** — `GET /polls/active`, `GET /leaderboard`, `GET /polls/{id}` — the
   "checking the app" background traffic pattern.
2. **The thundering herd** — the actually-realistic peak-load moment for this app: N virtual users
   all `POST /answers` for the *same* poll inside a tight window, simulating everyone submitting
   picks in the last few minutes before puck drop. Worth explicitly checking
   `SeasonService.EnsureSeasonExistsAsync`'s check-then-act read/write under concurrent load too
   (already inspected once — currently benign, since `SeasonId` is derived deterministically from
   the date, so racing writers just overwrite identical data rather than create duplicates — but
   exactly the kind of thing worth re-confirming here rather than trusting the one-time read).
3. **Scoring burst** — a single admin `POST /scoring` on a poll with many questions and many
   submitted answers. `ScoringService.ScorePollAsync` fans out (evaluates every answer, updates
   every leaderboard entry in a loop) — a different load shape from scenario 2: few requests, each
   doing a lot of internal DynamoDB work, rather than many small concurrent ones.
4. **Cold-start measurement** — fire a single request after sitting deliberately idle, to isolate
   Lambda cold-start latency from the warm-path p50/p95/p99 the other scenarios report. Not a
   scenario a load test against a traditional always-on server would need at all.

**Output**: NBomber's built-in HTML/CSV reports (percentile latencies per scenario). Could later
back a CI gate against staging with explicit thresholds (e.g. "p95 < 2s for reads") — but CI itself
is still an undecided, explicit follow-up for this repo (`docs/implementation-plan.md`'s Phase 7),
so for now this is a manual, occasional-run tool.

## Stress testing

A different question from load testing: not "does it perform well under expected load" but "what
actually happens when that's exceeded, and does it recover cleanly once the load stops." Given
this app's real expected concurrency is tiny, "stress testing" here is mostly about *learning*
where AWS's own default account-level limits actually bite — API Gateway's default throttle,
Lambda's default concurrent-execution ceiling (shared across every Lambda in the account, not just
this one), DynamoDB on-demand's burst-capacity behavior — rather than proving PuckDrop itself has
a scaling bug.

Reuses the load-testing project and its scenarios (`tests/PuckDrop.LoadTests/`) rather than being
a separate codebase — stress testing here just means running the same scenarios at a deliberately
excessive intensity, via NBomber's ramping/step load simulations instead of the load test's
constant/expected-size ones.

**What to actually check**:
- **Breaking point** — ramp scenario 2 (the voting thundering herd) well past any realistic friend
  group size until error rates spike or latency blows up, to find where the real ceiling is and
  what hitting it looks like from a client's perspective (`429`s from API Gateway throttling?
  Lambda `TooManyRequestsException`? DynamoDB `ProvisionedThroughputExceededException`, even on
  on-demand billing under a sudden-enough burst? Plain timeouts?).
- **Recovery** — once the excessive load stops, confirm the app returns cleanly to normal: no
  stuck state, no orphaned/partial writes, leaderboard totals still internally consistent (a
  partially-applied `ScorePollAsync` fan-out under throttling is the specific failure shape worth
  checking for, given it loops over every answer without any batch-transaction wrapping today).
- **Soak** (sustained rather than spiked) — moderate load held for a long duration (hours, not
  minutes), to catch anything a short burst wouldn't: DynamoDB throttling creeping in under
  prolonged writes, Lambda cold-start frequency drifting as instances recycle, that kind of thing.

Deliberately sequenced after load testing and its safety guardrails already exist and are trusted
— stress testing is the same tooling turned up past the point of controlled, expected behavior, so
it carries the most cost/outage risk of the three and shouldn't be the first thing run against a
real account.

## Smoke testing

A fundamentally different tool and purpose from the other two: not a performance measurement, but
a fast, minimal pass/fail check that a deployment actually works — run right after every real
`aspire deploy`, to catch "the deploy succeeded but the app is broken" before calling it done (a
broken Cognito client config, a newly-missing IAM permission, a wrong environment variable — none
of which a successful CDK deploy alone rules out).

Also directly closes a real, already-known gap: `PuckDrop.E2ETests` only ever exercises Keycloak's
real flow — Cognito, the actual production IdP, is untested anywhere in this repo today.

**Project**: `tests/PuckDrop.SmokeTests/`, xUnit v3 (pass/fail semantics fit this better than
NBomber's report-based model), pointed at a deployed base URL the same way the load tests are.
Deliberately *not* built on `PuckDrop.E2ETests`' `AppHostFixture` — that fixture's whole design is
booting a local AppHost via `Aspire.Hosting.Testing`, which doesn't apply at all once there's a
real, already-running deployment to point at instead; a smoke test is closer to a handful of plain
`HttpClient` calls than a scaled-down E2E run.

**Scope, deliberately minimal** (this is not a second copy of `PuckDrop.E2ETests`, which already
covers full user-flow correctness against Keycloak):
- `GET /auth-config` returns 200 with the real Cognito authority/client ID — proves the
  unauthenticated route and its API Gateway wiring are up.
- A seeded test user can obtain a real token via Cognito's `AdminInitiateAuth` and successfully
  call an authenticated endpoint (`GET /polls/active` or similar) — proves the JWT authorizer is
  correctly wired to the real user pool, not just that the API process is running.
- A seeded *admin* test user's token is accepted by an `AdminPolicy`-gated endpoint — proves
  `CognitoClaimsTransformation`'s group-to-role mapping still works against the real user pool's
  actual group configuration, not just the unit tests' mocked claims.
- One minimal Playwright check (reusing techniques from `PuckDrop.E2ETests`, not its fixture) that
  the deployed static UI actually loads and reaches Cognito's real hosted login page — proves the
  UI's hosting (wherever it's actually served from) and CORS are correctly wired end to end. Not a
  full click-through login — that's what the "authenticated endpoint" check above already covers,
  more cheaply, without a browser.

## Suggested build order

1. **Smoke testing first** — smallest, fastest, immediately useful after every future deploy, and
   closes the real Cognito-untested gap. Low effort relative to its value.
2. **Load testing second** — the genuine "try this stuff out" learning goal; needs the shared
   prerequisites (dedicated environment, test users, cost guardrails) either way, and those are
   worth having in place and trusted before stress testing turns the intensity up.
3. **Stress testing last** — reuses load testing's project and scenarios, so there's little
   additional build cost once step 2 exists; carries the most cost/outage risk of the three, so it
   should only run once the safety guardrails from step 2 are already proven, not while they're
   still being worked out.
