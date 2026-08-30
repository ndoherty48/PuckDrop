# Auth & Security Testing (Future Plan)

> **Status: planned, not started.** Unlike `docs/load-stress-smoke-testing.md`, this one isn't
> exploratory — it closes a real, already-identified gap in existing coverage (see "The gap this
> closes" below), not a "try some stuff out" learning exercise. It's the highest-priority of the
> newer test-project ideas discussed for that reason, and needs no new deployed environment or AWS
> spend to start.

## Different from load/stress/smoke: mostly runs locally

Load/stress/smoke testing had to target a real deployed environment because the local AWS Lambda
Service Emulator processes one invocation at a time, so it can't stand in for real concurrency
behavior. None of that applies here — every test in this doc is "does the API correctly reject or
handle one adversarial request," not "how does it behave under load," so the existing local Aspire
stack (real Keycloak, the real Lambda-hosted API, via `Aspire.Hosting.Testing`) is a perfectly
faithful target. A smaller subset — confirming `CognitoClaimsTransformation`'s claim-shape
assumptions actually hold against a real Cognito-issued token, not just Keycloak's — does need a
real deployed environment, and should reuse `PuckDrop.SmokeTests`' Cognito test-user/token-
acquisition plumbing (see `docs/load-stress-smoke-testing.md`) rather than duplicating it.

## The gap this closes

`docs/implementation-plan.md`'s Phase 7 describes `PuckDrop.E2ETests` as superseding the
originally-deferred "API full-HTTP-pipeline tests" item (`[Authorize]`/`AdminPolicy` enforcement
through the real MVC pipeline). That's true for user-flow correctness, but imprecise for one
specific thing: `RoleGatingTests` proves the *UI* correctly hides admin controls and redirects a
non-admin browsing session — it says nothing about whether the *API itself* would reject a
non-admin user's real, valid token if they bypassed the UI entirely and called
`POST /polls`/`PublishPollAsync`/etc. directly. Nothing in the current suite (unit, bUnit, or E2E)
independently proves that. This doc's project is the actual follow-up on that original deferred
item, scoped correctly this time - raw API calls, not a browser.

## Scope

Verified against the real current code while writing this, not assumed:

1. **Missing/malformed/garbage `Authorization` header** → `401`, never a `500` or a leaked
   exception detail.
2. **Valid token, wrong role** → a real non-admin token used directly against an
   `AdminPolicy`-gated endpoint (`POST /polls`, `POST /polls/{id}/publish`, etc.) via raw HTTP →
   `403`. The actual point of this project: proven independently of the UI, not just observed as a
   UI redirect.
3. **Expired token** → `401`.
4. **Wrong audience/issuer token** — a token minted for a different client, or (if a second test
   realm/pool is ever set up) a different realm/pool entirely → rejected, not silently accepted.
5. **Unhandled-exception response shape is genuinely unknown today, worth actually checking**:
   `DomainExceptionFilter` (`API/src/PuckDrop.Api/Filters/DomainExceptionFilter.cs`) only wraps
   three exception types (`KeyNotFoundException`, `InvalidOperationException`, `ArgumentException`)
   into the sanitized `ErrorResponse` shape - everything else hits `return;` and falls through to
   ASP.NET Core's own default handling. There's no `UseExceptionHandler`/
   `UseDeveloperExceptionPage` configured anywhere in this codebase, and no explicit
   `ASPNETCORE_ENVIRONMENT` branching for it either - so what a genuinely unhandled exception
   actually returns to a real client (a bare `500` with no details, or something more revealing)
   is an open question this project should actually answer, not assume either way. Also worth
   checking the three *handled* types don't leak anything sensitive either -
   `context.Exception.Message` is returned to the client verbatim (line 26 of that file), and nets
   from framework/library-thrown exceptions of those types aren't guaranteed to be caller-safe.
6. **State-based authorization guards, not just role-based ones** - these are easy to silently
   regress since no policy/middleware enforces them, only application code:
   `ResultsService.GetPollResultsAsync` already guards `GET /polls/{id}/results` against a poll
   that isn't `Scored` yet (confirmed reading the code - `if (pollData.Poll.Status !=
   PollStatus.Scored)`), so a friend can't peek at everyone's picks via that endpoint before a
   poll's deadline. Worth an explicit regression test precisely because a role-based policy
   wouldn't catch this if it ever broke.
7. **User-identity resolution never trusts client input** - checked directly in
   `AnswersController`: both `GetAnswers` and `SubmitAnswers` resolve `userId` exclusively from
   `User.GetUserId()` (the authenticated principal's own claim), never from a request parameter -
   so there's no "submit picks as another user" IDOR today. Worth locking in with an explicit test
   precisely because it'd be an easy mistake for a future endpoint to introduce (e.g. accepting a
   `userId` in the request body "for convenience").
8. **`/auth-config`'s `[AllowAnonymous]` surface stays minimal** - it's intentionally reachable
   pre-authentication (the UI needs it to bootstrap), so confirm it only ever returns
   Authority/ClientId/ResponseType and nothing else, now or after future changes to it.
9. **CORS policy is actually scoped**, not accidentally wide open to any origin.

## Approach / tooling

Project: `tests/PuckDrop.SecurityTests/`, xUnit v3. Boots the real AppHost via
`Aspire.Hosting.Testing` - the same proven pattern `PuckDrop.E2ETests` already uses (inheriting its
already-fixed AppHost health checks, so no need to rediscover those races) - but talks to the API
directly via `HttpClient`. No Playwright/browser needed for most of this; it's raw HTTP calls
against real endpoints with deliberately crafted or missing credentials.

**Obtaining a real-but-wrong-role token** is the one open design question, deliberately left
undecided here rather than defaulted:

- **Reuse `PuckDrop.E2ETests`' proven approach** - a real (Playwright-driven, but minimal - no
  page assertions needed after) login once per role against the real Keycloak realm, capturing the
  issued access token for reuse across the run. No realm changes needed, consistent with the
  explicit decision made earlier this session against modifying the realm purely for test
  convenience.
- **Enable `directAccessGrantsEnabled`** on a dedicated test-only Keycloak client, allowing the
  OAuth2 password grant (`grant_type=password`) for direct, browser-free token acquisition -
  simpler, but this is the exact realm change explicitly rejected earlier this session for the E2E
  suite specifically. Worth genuinely reconsidering here rather than assuming the same answer
  applies: the reasoning that killed it there ("browser-driven login already covers this fine")
  doesn't really hold for a project whose entire point is raw-HTTP behavior, not UI behavior - but
  that's a call to make deliberately when this is actually built, not to inherit by default.

Malformed/missing-token tests (items 1 and 4's audience-mismatch case) need no real login at all -
those are constructed directly as literal header strings.
