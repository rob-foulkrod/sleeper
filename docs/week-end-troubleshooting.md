# Week-End Closeout Troubleshooting

This document records friction observed during the 2026 Week 5 closeout on
October 13, 2026. It is intended to improve the `/week-end` workflow without
changing the filed Week 5 artifacts.

## Workflow and Product Issues

### Injury MCP requires manual HTTP session orchestration

The closeout needed to initialize a streamable HTTP MCP session, preserve the
`Mcp-Session-Id`, send `notifications/initialized`, parse server-sent event
responses, and issue each tool call as JSON-RPC. There is no repository helper
for this workflow.

**Impact:** Injury refresh and verification required fragile, repeated
PowerShell request construction.

**Suggested improvement:** Add a checked-in PowerShell MCP client helper that:

- initializes a fresh session;
- sends the initialized notification;
- exposes a typed `Invoke-SleeperMcpTool` function;
- parses JSON and SSE responses;
- closes the session when complete;
- stops immediately on JSON-RPC or tool errors.

### Roster history exports every available week

`rosters-history` reported data for Weeks 1 through 18 and wrote all 18 weekly
files even though only Week 5 was complete. Weeks 6 through 18 were newly
created future placeholders. It also rewrote filed Weeks 1 through 4.

**Impact:** The closeout had to restore Weeks 1 through 4 byte-for-byte from its
backup and remove 13 specifically identified future files before running
`asset-history`.

**Suggested improvement:** Add an optional completed-week boundary to
`rosters-history`, or add a closeout-safe command that writes only missing
completed weeks and refuses to overwrite filed snapshots. Preserve the existing
command for callers that intentionally want all available matchup buckets.

### Recap generation selected an unavailable Copilot model

The recap command requested `claude-opus-5.5`, which was not in the available
Copilot model list. It then tried Foundry, where the cached Azure refresh token
had expired, and finally wrote a data-only envelope.

**Impact:** The command exited successfully and created the expected recap path,
but the file was a debugging fallback rather than a publishable article. The
closeout had to detect and replace it manually.

**Suggested improvement:**

- Validate the configured Copilot models before building the recap envelope.
- Select an allowed fallback model from the current session instead of disabling
  Copilot when one configured model is unavailable.
- Add a distinct nonzero exit code, output status, or machine-readable marker
  when only a data envelope was written.
- Add a recap preflight command that checks the Copilot session and live model
  roster without writing files.

**Resolved:** Recap and season generation now resolve every configured role against
the signed-in Copilot CLI session's live model list. An unavailable preference uses
another configured live Copilot model, then the first model reported by that session.
Reporting no longer switches to Foundry when Copilot model selection fails.

**Validation:** A live Week 5 recap run on October 13 reproduced the unavailable
`claude-opus-5.5` preference, selected live Copilot model
`claude-sonnet-5.5` for the league writer, completed the writer and proofreader
passes, and never attempted Foundry. The generated draft was retained outside
published content for debugging and the canonical recap was restored after the
draft failed separate editorial checks.

### Recap generation migrated a filed sidecar schema

The recap command appended the Week 5 team-name snapshot but also rewrote the
oldest filed snapshot. Historical `DisplayName` and `Username` fields were
replaced by `OwnerName`.

**Impact:** The mutation violated the preservation contract and required a
targeted repair that restored the prior snapshot while retaining the new Week 5
entry. `power-history.json` did not have this problem.

**Suggested improvement:** Deserialize historical team-name snapshots through a
schema-preserving representation, or append the new JSON object without
reserializing prior entries. Add a regression test that compares every existing
snapshot structurally before and after a new recap is generated.

### Injury report summary fields are easy to misread

The first summary attempt expected `CohortCount`, `RosteredPlayerCount`,
`CoveredPlayerCount`, `Classification`, and `MissingCoverage`. The actual JSON
uses `RosteredPlayers`, `TrackedPlayers`, `Category`, and
`MissingTrackedPlayerIds`.

**Impact:** The initial summary showed null coverage values and blank categories
even though the report itself was valid.

**Suggested improvement:** Publish a JSON schema or generated DTO example for
MCP consumers. A repository helper should deserialize the report into the same
types used by the application rather than relying on ad hoc property names.

### Editor test discovery did not accept the project path

The VS Code test tool reported that no tests were found when given the reporting
test project. Running the detected VSTest command directly succeeded:

```powershell
dotnet test .\tests\Sleeper.RosterReport.Tests\Sleeper.RosterReport.Tests.csproj --verbosity minimal
```

All 256 tests passed.

**Impact:** The closeout needed a second test invocation through the CLI.

**Suggested improvement:** Document the CLI command as the primary validation
path for this project, or configure editor test discovery so the project is
recognized consistently.

### Browser validation could not complete dark and mobile checks

The Playwright MCP server refused its session because the browser profile was
already in use. The shared VS Code browser could load pages and capture desktop
light-theme screenshots, but:

- theme-toggle clicks timed out while waiting for the button to become stable;
- forced dispatch and page-evaluation attempts did not change the captured
  theme;
- viewport resize attempts did not change the captured viewport.

The homepage, Week 5 recap, and Week 6 preview all returned HTTP 200. Desktop
light-theme screenshots were readable, but dark-theme and mobile visual checks
remained unverified.

**Suggested improvement:**

- Make the browser validation helper start an isolated Playwright context.
- Add an automated visual smoke test that accepts route, viewport, and saved
  theme as parameters.
- Add DOM assertions for horizontal overflow, missing images, article headings,
  and theme state so validation is not dependent only on screenshots.

### Site build emits unresolved font warnings

The Astro build succeeded but Vite warned that the three Courier Prime font URLs
were unresolved at build time and would be resolved at runtime.

**Impact:** The warning added ambiguity to an otherwise clean build. The pages
did load, but the browser also warned that preloaded fonts were not used shortly
after load.

**Suggested improvement:** Verify that the font files are present under the
expected public path and that preload URLs, `@font-face` URLs, and the configured
Astro base path use one consistent strategy. Add a built-site check that requests
each font and confirms HTTP 200.

## Agent-Side Execution Mistakes

These problems were caused by the closeout execution rather than repository
behavior.

### PowerShell pipeline construction errors

Several inspection commands placed a pipeline immediately after a `foreach`
statement or used invalid multi-property `Sort-Object` syntax. PowerShell
reported parser errors before those commands ran.

**Prevention:** Build loop output with `$result = @(foreach (...) { ... })`, then
pipe `$result`. Use calculated properties when sorting multiple fields with
independent directions.

### Incorrect PowerShell JSON array assumptions

One Sleeper response and one weekly export were initially treated as top-level
arrays when they were wrapped or object-shaped. That produced counts of one and
null roster totals.

**Prevention:** Inspect the top-level JSON properties before calculating counts.
Use `Invoke-WebRequest` plus `ConvertFrom-Json` when response enumeration behavior
matters, and validate the expected shape explicitly.

### Browser-tool fallback consumed time without improving coverage

After the primary Playwright tool reported a profile conflict, several attempts
were made to force theme changes and viewport resizing through the shared
browser. The screenshots remained unchanged.

**Prevention:** After one failed interaction and one fallback attempt, record the
browser gate as blocked. Do not repeat equivalent interactions unless a tool
returns evidence that the page state changed.

## Recommended Priority

1. Prevent historical sidecar migration.
2. Make recap backend preflight and fallback status explicit.
3. Add a closeout-safe roster export boundary.
4. Add a reusable MCP PowerShell client.
5. Add isolated automated browser smoke checks.
6. Resolve the font-path warnings.
