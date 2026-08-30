# AI Features (Future Plan)

> **Status: future consideration, several genuinely worth building.** Not a committed roadmap
> item, and not framed around "add AI because AI" - the candidates below are here because they fit
> what PuckDrop actually is (a small friend-group social pool, not a serious betting platform), not
> because every app should have an AI feature. Confirmed nothing AI-related exists in the codebase
> today before writing this (checked, not assumed).

## Why these, and why not others

The strongest candidates below lean into the *social* purpose stated in `CLAUDE.md` ("game day
poll app for friend groups") rather than trying to make the app more efficient or more serious.
Two ideas were considered and deliberately left out, for the same reason in reverse:

- **Chat-based pick submission** - clicking a radio button on `Poll.razor` is already faster than
  typing a sentence to an LLM to do the same thing. Novelty, not improvement.
- **Anomaly/collusion detection on admin scoring** - solving a problem ("what if the admin
  cheats") that a friend group's actual trust already solves, at a level of engineering effort
  wildly disproportionate to the app's real scale.

## Candidates, ranked

### 1. Post-poll banter/recap generation

After a poll is scored (`ScoringService.ScorePollAsync` already has everything needed - per-user
correctness, points, the poll's own questions/options), generate a short, personality-driven
recap: who nailed it, who bombed, streaks, rivalries. The thing people actually screenshot into
the group chat. This is the feature I'd lead with - it's not "AI because AI," it directly serves
the app's stated social purpose, and it's the same pattern fantasy-sports-adjacent pools already
lean on for engagement (weekly AI recaps are common there). Cheap to build: one LLM call per
scored poll, over data the app already has.

### 2. End-of-season "awards"

A natural extension of #1 - a capstone LLM-generated writeup at season end (Most Consistent,
Biggest Upset Caller, Comeback Player of the Season, whatever fits the actual group's story that
season) from aggregated `LeaderboardEntry`/`PollResults` data across the whole season. One batch
call, high delight, a nice thing to close a season with rather than the leaderboard just... ending.

### 3. Draft poll-question suggestions for admins

Real, observed friction, not a guess: `CreatePoll.razor` → `EditPoll.razor`'s "Add Question" flow
adds one question and its options at a time, one full round trip each - there's no bulk-create.
EIHL games likely follow a repetitive question shape (who wins, total-goals over/under, that kind
of thing), so an LLM suggesting a draft set of questions from just the two team names would cut a
real piece of admin friction. Admin still reviews/edits every suggestion before publishing -
important given a bad or offensive auto-generated question would be visible to the whole group and
affects real scoring, so this stays human-in-the-loop by design, not an autonomous poll-creator.

### 4. Auto-suggested correct answers at scoring time

Right now `ScorePoll.razor` requires the admin to manually look up the real result and click
through every question themselves. Pre-filling the admin's scoring choices from a real results
feed (still confirmed manually before `Submit Scores`, for the same human-in-the-loop reason as
#3) would save real time on every single game day, not just occasionally.

**Real, unresolved caveat**: this only works if a clean, structured EIHL results source actually
exists. EIHL is a fairly small/regional league - this doc doesn't assume good structured data is
available, only that it's worth checking before committing to the idea. If the only sources are
news recaps/box scores rather than a queryable feed, this drops from "cut real admin time" to
"maybe parse a webpage with an LLM," which is a meaningfully bigger and less reliable build than
#1-#3.

### 5. Personalized season-long insight blurbs

("You always pick the home team." "Great on over/under, terrible at outright winners.") A
complement to #1/#2 - the underlying stats need no LLM at all (plain aggregation over
`UserAnswer`/`LeaderboardEntry`), only the readable summary does. Lowest priority of the five: real
but smaller value than the other four, and easy to fold in later once the LLM-calling
infrastructure already exists for #1/#2.

## Cross-cutting design notes

**Where the call happens**: server-side only, from the Lambda-hosted API - never from the Blazor
WASM client, for the same reason API keys never belong in a browser bundle (see `CLAUDE.md`'s Auth
section on why OIDC config itself moved server-side).

**Provider**: AWS Bedrock is the more natural fit here than a direct Anthropic/OpenAI API key,
given the whole backend is already AWS-native (Lambda, DynamoDB, the existing IAM execution-role
model in `DeploymentStack.cs`) - Bedrock access is one more IAM permission on the existing Lambda
role, not a new vendor credential/secret to provision and rotate.

**Cost**: needs to stay cheap in absolute terms - this is a small friend-group app, not a funded
product, and #1/#2/#5 are all low-frequency, low-token calls (once per scored poll, once per
season) rather than anything called per-request. Worth an explicit budget alarm regardless (same
guardrail already called out in `docs/load-stress-smoke-testing.md` for a different reason), since
it's real AWS spend either way.

**Tone/safety guardrail**: #1/#2/#5 generate text about real (if pseudonymous, via `DisplayName`)
friends by name. Worth an explicit system-prompt constraint that keeps this playful and never
genuinely mean - the difference between "content people screenshot and laugh at" and "content that
makes someone not want to play anymore" is entirely in how it's prompted, not something to leave
implicit.

## Suggested build order

1. **Post-poll recap (#1)** - smallest, most self-contained, most directly validates whether this
   whole direction is actually wanted before building anything else on top of it. If nobody enjoys
   the recap, the rest of this doc is moot and nothing else here should be built.
2. **Season awards (#2)** - trivial incremental cost once #1's LLM-calling infrastructure exists
   and is trusted.
3. **Draft question suggestions (#3)** - real, independent admin-friction value; doesn't depend on
   #1/#2 at all, so could equally be built first if the social features don't validate.
4. **Insight blurbs (#5)** - lowest-value, cheapest once the infra exists; fold in whenever.
5. **Auto-suggested scoring (#4)** - gated on the open data-source question above; investigate
   that first, separately from actually committing to build it.
