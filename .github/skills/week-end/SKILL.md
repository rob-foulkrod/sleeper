---
name: week-end
description: 'Wrap up a fantasy football week end to end: refresh injuries, verify NFL and Sleeper scores, export snapshots and transactions, write the recap and next-week preview, build and validate the site, and optionally publish. Use for week-end, end-of-week, weekly closeout, or post-Monday wrapups. Preserve all previously filed articles and predictions.'
argument-hint: '[week] [season YYYY] [league ID] [publish | data-only]; defaults to full local closeout of latest completed week'
user-invocable: true
---

# Week-End Closeout

## Single-Command Contract

Invoke in VS Code Copilot Chat, not a terminal:

```text
/week-end 3
/week-end 3 season 2026 publish
/week-end 3 season 2026 data-only
```

These are instructions for the agent, not flags accepted by the reporting CLI.
An invocation without arguments selects the latest fully completed week, verified
against Sleeper state and the NFL schedule. The number identifies the completed
week: `/week-end 3` closes Week 3 and prepares Week 4.

- Default: refresh evidence, export data, create missing weekly filings, build and
   validate the site, and provide a local preview URL. This invocation authorizes
   those steps together; do not ask permission again between routine stages.
- `publish`: additionally authorizes a scoped commit and push, followed by deployment
   and live-site verification. Failed gates block publishing.
- `data-only`: refresh and validate evidence and datafiles only; leave `recaps/` and
   `site/` untouched. Do not write stories, commit, or publish.
- Conflicting modes or ambiguous league/season require clarification. Missing
   authentication, unavailable required evidence, or preservation failures are
   blockers; report them rather than silently skipping stages.

This is an agent-orchestrated workflow: web research and editorial judgment remain
required. Do not describe it as a deterministic unattended shell command.

## Run Order and Resume

Track these stages: preflight, injuries, results, roster/transaction exports,
weekly edition snapshot, recap and preview, editorial review, site validation,
and optional publication. Continue through the selected mode in one invocation.

Inventory outputs before starting. Existing recap, preview, Monday-watch, and
weekly edition files are filed artifacts: read and reuse them unchanged. Generate
only missing artifacts. A rerun must not overwrite a filed article to refresh its
timestamp, polish its style, or revise its predictions. If an existing filing
conflicts with newer evidence, report the discrepancy and ask about a separate
correction; do not silently edit it or publish contradictory new material.

When every artifact already exists, validate and report that the week is already
closed; perform publication only if requested and there are relevant unpublished
changes. Never create an empty commit. Do not rerun completed work from the current
invocation merely to fill the checklist; retain its retrieval timestamps.

## Establish Scope

1. Read the repository instructions, `docs/reporting-cli.md`, and
   `docs/injury-importing.md` from the workspace root.
2. Resolve season, completed NFL week, and Sleeper league ID from the request and
   current league information. The 2026 league ID is `1312539280601522176`;
   validate its season before use. Do not default to 2025 because older files exist.
3. Verify the NFL schedule and that all games in the requested week are final.
   An explicitly requested unfinished week blocks full closeout and publication.
   Offer an interim data-only update; do not label it final or advance the site.
   For an omitted week, choose the latest fully completed week, not simply state.week.
4. Record retrieval time and inspect `git status --short`. Preserve user changes.
   Before any exporting command, record hashes of existing files under `recaps/`
   and `datafiles/`, and record the site's baseline. Keep byte-for-byte backups of
   existing files that the legacy exporters or recap generator can rewrite. Save
   backups outside published content. Never use a Git version as a substitute for
   backing up a user's working file.
5. Check the requested season's `export.json` and layered lore under `docs/lore/`
   for first-name owner mappings. Verify the CLI, PowerShell, Node dependencies,
   and local host as needed. Use existing helpers, not a new project or framework.
   Parse any PowerShell script before running it. Check native exit codes.

## Refresh Injuries First

1. Use the injury MCP tools from the running local host. Check `/health`; follow
   `scripts/start-host.ps1` if startup is needed. If using HTTP `/mcp`, initialize
   a fresh MCP session instead of relying on a previous shell helper or session.
2. Run `import_sleeper_injuries` with `commit=false`. Review candidate count,
   observation timestamp, and errors before repeating with `commit=true`.
   Read back current statuses to verify the write. Report imported observations
   separately from material changes; a full snapshot is not hundreds of new injuries.
3. Research material changes and unresolved concerns using dated official team
   reports and reliable reporting. Include injuries from the week's final games.
   Append evidence with `record_injury_observation`; never edit SQLite directly.
4. Preserve source URL, source authority, body part, practice participation, notes,
   and verified publication/effective timestamps. Never guess a time zone or exact
   onset timestamp. A fresh snapshot does not establish when an injury occurred.
   Use short expiry windows for evolving reports and verify each recorded status.
5. Distinguish prior-game inactivity from next-week availability, planned IR from
   completed transactions, planned practice from participation, and optimistic
   reports from medical clearance. Preserve conflicting sources explicitly.
6. Call `get_weekly_injury_report` with season, week, league ID, and verified
   inclusive-start/exclusive-end timestamps with offsets (maximum 31 days).
   Include the postgame reporting cutoff when needed and state it clearly.
   The report is retrospective, not a strict as-of snapshot. Use requested-week
   roster ownership, not current ownership, when describing weekly impact.
   Report missing coverage and uncertain onset. Do not substitute historical
   nflverse imports for current-season evidence.

## Retrieve Results

1. Run `get_matchup_scoreboard` for the requested week and league.
2. Retrieve the NFL results from a verified source such as
   `https://www.nfl.com/schedules/{season}/reg{week}/`. Confirm every game's
   status; do not infer finality from scores alone.
3. Keep NFL scores and Sleeper fantasy points separate. Sleeper totals can still
   change through stat corrections. Record sources and the retrieval cutoff.
   Scoreboard reads alone do not create local weekly snapshot files.

## Export Snapshots and Asset History

Run from the repository root in PowerShell 7. Set the validated season and league
ID first, inventory existing weekly files, then run each command separately and
check `$LASTEXITCODE` immediately. Validate roster coverage and clean up newly
created future placeholders before running `asset-history`.

```powershell
$season = 2026
$leagueId = '1312539280601522176'
dotnet run --project ./src/Sleeper.RosterReport/Sleeper.RosterReport.csproj -- rosters-history --season $season --league-id $leagueId
if ($LASTEXITCODE -ne 0) { throw 'Roster history export failed.' }
```

Inspect emitted weeks against the verified schedule now, before the audit:

```powershell
dotnet run --project ./src/Sleeper.RosterReport/Sleeper.RosterReport.csproj -- asset-history --season $season --league-id $leagueId
if ($LASTEXITCODE -ne 0) { throw 'Asset history export failed.' }
```

- `rosters-history` writes `datafiles/{season}/week-NN.json` with weekly matchup
  rosters, starters, bench, and player points. It walks available matchup data,
  not a verified completed-week boundary, and rewrites existing weekly exports.
  Inspect the emitted weeks; future rosters or partial scores are not final data.
   Restore pre-existing weekly files from this run's byte-for-byte backups after
   export; retain only newly created completed-week files. This undoes only the
   exporter changes made by this invocation. Report score corrections against a
   preserved snapshot instead of changing it silently.
   If future placeholders were newly created by this run, explain and remove only
   those new files before auditing. Verify future dates against the NFL schedule;
   zero points alone do not prove a week is unfinished. Never delete pre-existing
   files without approval. If pre-existing unfinished or later-than-target weeks block a valid audit,
   ask how to handle them and do not present inflated start counts as real results.
   Do not alter the exporter as part of routine data refresh.
- `asset-history` must follow roster export. It writes `transactions.json`,
  `asset-movement-audit.json`, and `asset-movement-summary.txt` in the same directory.
  It fetches transaction buckets 1-18; empty future buckets are not completed weeks.
  Distinguish transaction coverage from the roster weeks available for the audit.
- Exports use current player labels and team-name metadata; do not describe those
  labels as historically verified names. Use historical name evidence when needed.
- These files capture Sleeper data, not an NFL game-score archive. Do not imply
  fetched NFL results were persisted unless a separate artifact was actually made.

## Validate Evidence

1. Parse generated JSON with `ConvertFrom-Json`. Check season, league ID, distinct
   roster IDs, team count, expected completed weeks, and populated roster arrays.
2. Compare the requested week's team totals against the fresh Sleeper scoreboard.
   Investigate mismatches rather than patching scores by hand.
3. Confirm all three asset-history outputs exist and the audit covers the intended
   roster weeks. Do not interpret empty transaction results as proof without a
   successful fetch. Summarize errors or partial coverage.
4. Verify hashes for protected weekly datafiles and `recaps/`, and inspect the
   changed-file list. Transaction history and its audit are refreshable outputs;
   previously filed stories and snapshots remain protected. A data-only run must
   also preserve the site's baseline.
5. Summarize exported paths, covered weeks, injury snapshot and sourced-update
   counts, NFL finality, score checks, and unresolved limitations. Stop here for
   `data-only`; otherwise continue to the weekly edition and articles.

## Capture the Weekly Edition

For a missing `recaps/{season}/week-NN-data.json`, run the existing script after
parsing it and confirming the requested week is closed:

Before running it, inspect `datafiles/narrative-state.json`. It must contain one
explicit entry for the completed season/week. Existing and omitted historical
editions are level `0`; do not backfill them. A new value may hold steady or
increase by one only. Never change a filed snapshot to revise the level. If the
entry is missing, duplicated, out of range, regresses, jumps, or conflicts with a
filed snapshot, stop and report the validation error rather than bypassing it.

```powershell
./scripts/build-weekly-edition.ps1 -Season $season -Week $week -LeagueId $leagueId
```

The script refuses an existing output; reuse that snapshot rather than bypassing
the guard. Validate `completed_week`, `next_week`, roster count, pairings, scores,
owner mappings, full trade packages, and the internal `_narrative.sentience_level`.
The raw level is operator state and must not appear in public site JSON. This snapshot drives the homepage;
`datafiles/{season}/week-NN.json` alone does not advance the edition.

Current roster ownership belongs in the upcoming preview; requested-week ownership
belongs in the recap. Attribute availability to its source and date, accounting for
reporter updates newer than platform tags. Surface material changes since an
existing snapshot rather than passing them off as captured snapshot facts.

The current script requires a following NFL week. For Week 17, preserve the
league's no-Week-18 rule: use a season-closeout outlook rather than fantasy Week 18
fixtures. If the existing site pipeline cannot represent that honestly, stop the
site/publication stage and explain the limitation; do not invent matchups or
silently start a season recap or code change outside the requested scope.

## Write Missing Articles

Create `recaps/{season}/week-NN.md` and, while the league season continues,
`recaps/{season}/week-(NN+1)-preview.md`. Use the existing content conventions;
preview frontmatter identifies `kind: weekly-preview`, week, title, and filing date.
Do not create a Monday-watch article before there is a Monday-specific update.

The assistant may write directly from verified evidence or use the existing
`recap` CLI as a first draft. When using the CLI, first confirm the target article
does not exist, pass season, league ID, week, and both injury-window arguments.
It also rewrites team-name and power-history sidecars: preserve backups and restore
this run's changes to prior entries before accepting a new week's entry. Compare
prior entries structurally, including their schema fields. Do not allow a serializer
to migrate earlier history unnoticed. If that cannot be done safely, restore the
original sidecar bytes and report the skipped sidecar update.

Check which backend actually ran. A data-only fallback, unavailable story, failed
proofreading placeholder, or unverified forecast is not a finished article. Repair
new drafts using verified facts, or stop publication. Never remove a caveat merely
to make a failed draft sound confident. File next-week picks in the separate
preview; avoid competing sets of new predictions in the recap and preview.

Each completed recap includes all matchups, key starter contributions, standings
consequences, relevant injuries, and an honest review of prior picks. Explicitly
read the standalone preview and any Monday-watch article; the CLI's previous-recap
callback does not cover every filing. The next-week preview covers every verified
pairing with current ownership, real NFL opponents, meaningful availability
questions, and clearly labeled editorial picks. Use creative license for voice,
headlines, and grounded humor while preserving factual claims.

Read league lore and original predictions before composing a separate dated
wrapup. Keep the original calls intact and compare them fairly with final results.
Use approved first-name owner mappings; do not expose private names or handles.
In this league, "Crazy" in a team name may evoke old "Crazy Davey is giving away
the store; no deal is too low" commercials. Treat that as a suspected
wheeling-and-dealing mindset, not a confirmed motive or evidence of actual trades.
It is background writing context, not an explanation to add to the site.

### Editorial Pass for New Drafts

Published articles are immutable unless the user explicitly authorizes a correction.
Apply style improvements to new drafts only. Never regenerate or polish a filed
article as part of this pass.

1. Lead with the play, player, score, decision, or consequence. Use direct
   statements and vary the rhythm across matchup sections.
2. Read the entire assembled article after generation and factual corrections.
   Search for `not`, `rather than`, and `instead of`, including variants such as
   "isn't". Review the matches in context; this is an editorial check, not a banned
   word count. Cut habitual "X, not Y" constructions, imaginary-reader corrections,
   and repeated "not permission" or "not proof" explanations. Keep contrasts that
   resolve a genuine ambiguity. Swapping in another contrast phrase is insufficient.
3. Keep internal guardrails out of the published voice: roster verification,
   snapshot handling, and preserving old picks are workflow responsibilities.
   The assistant owns its predictions: write "I picked" and "I was wrong," not
   "the column" or "the prediction desk." A miss is a wrong pick, not automatically
   a bad pick; acknowledge the result plainly, then keep making picks next week.
   Call the analysis bad only when the reasoning itself warrants that judgment.
   Remove audience-facing fantasy basics and defensive disclaimers about points,
   lineups, probabilities, or preserving the archive. Keep a brief evidence cutoff
   in source notes and consequential injury uncertainty beside the affected player.
   For example: "His Sunday availability remains unresolved."
4. Draw humor from specific league events and personalities. Trim repeated jokes
   about the prediction desk or the column's own writing process.
5. In Monday watches, compare the remaining starters and point gap. If the
   trailing team has no players left, treat the matchup as settled for practical
   purposes and own a wrong pick; do not manufacture suspense. Keep close-game
   commentary for contests with a live, meaningful path.
6. Finish with another fact check: style edits must preserve numbers, ownership at
   the relevant time, picks, attribution, and uncertainty. Search again and read
   aloud for repeated sentence shapes across the whole piece. Complete this pass
   before filing; leave the existing archive untouched.

Example revision for a future draft:
- Before: "This is a lean, not a coronation."
- After: "Devin, narrowly. Travon's quarterback pair makes this uncomfortable."

The CLI's shared writer prompts are in
`src/Sleeper.RosterReport/FoundryAgentInstructions.cs`. These instructions guide
generation; the full-article editorial pass above remains necessary, including
when the assistant writes or repairs sections manually.

## Build and Validate the Site

After the editorial pass, run from the repository root:

```powershell
dotnet run --project ./src/Sleeper.RosterReport/Sleeper.RosterReport.csproj -- site-data
if ($LASTEXITCODE -ne 0) { throw 'Site data generation failed.' }
npm run build --prefix site
if ($LASTEXITCODE -ne 0) { throw 'Site build failed.' }
dotnet test ./tests/Sleeper.RosterReport.Tests/Sleeper.RosterReport.Tests.csproj --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Reporting tests failed.' }
```

Use the editor test tool if it can discover the full reporting project; otherwise
use the command above. Install locked site dependencies with `npm ci --prefix site`
if missing. Required checks:

- `site/src/data/league.json` selects the intended `current_edition`, recap, and
   preview; a historical closeout must not roll the current homepage backward.
- Saved totals agree with the source; historical articles, weekly datafiles, and
   edition snapshots retain their baseline hashes. Only the new history entry may
   be added to an existing recap sidecar. Generated site data is expected to change.
- The archive contains no new privacy leaks, failed-generation placeholders, or
   uncited medical assertions. Review the changed-file list against the baseline.
- Serve the built site with `npm run preview --prefix site -- --host 127.0.0.1 --port PORT`
   on an available port. Reuse a known suitable server or choose another port;
   never kill an unrelated server. Keep the preview available for the user.
- Use Playwright to review the homepage, new recap, and upcoming preview on desktop
   and mobile in light and dark themes. Check navigation, readable tables, overflow,
   article headings, font/assets loading, and console errors; inspect screenshots.
   If browser validation is unavailable, report the missing gate and stop before
   automatic publishing. Remove only temporary artifacts created by this run.

Routine closeout uses the existing design. Make only small related fixes needed
to present the new edition correctly; do not redesign the site during this skill.

## Publish Only When Requested

With `publish`, proceed after all gates pass without asking again for permission
to commit. Check the actual branch, remotes, and publishing workflow first. The
verified destination is `target` (`https://github.com/robfoulk/sleeper.git`), branch
`main`; `origin` is a different repository. Stop on a mismatch or divergence
instead of force-pushing, changing branches, or guessing the destination.

Stage explicit closeout paths only. Include new evidence files, articles, edition
snapshot, and intended generated site data. Exclude the local injury database,
credentials, backups, screenshots, and unrelated dirty files. If an intended path
contains pre-existing user edits, clarify inclusion before committing that path.
Run `git diff --check`, commit, and push to `target main`, checking exit codes.

Track the Pages workflow for the exact pushed SHA. Confirm successful build and
deployment, then check the public homepage and new article URLs at
`https://robfoulk.github.io/sleeper/`. A successful push alone is not a verified
deployment. If deployment is still running or verification is blocked, report the
workflow URL and pending status accurately. Never claim the site is live on a 404.

## Completion Message

Report the completed week and upcoming edition, evidence cutoff, injury update
counts, new/reused artifact paths, checks passed, and preservation result. Include
the local URL in default mode; for `publish`, include the commit, workflow status,
and live URLs when verified. Call out partial coverage or blockers. State explicitly
whether anything was committed or published. Do not stop at an intermediate stage
while required work remains possible within the selected mode.