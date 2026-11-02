# Skill vs. live server: `servicestudio-mcp-oml` against Service Studio

Living file. The weekly sync run updates the discrepancy table when upstream commits or a new Service Studio build
change a row, and adds a line to the sync log at the end; git history keeps the earlier states. Last full live
verification: 2026-10-30, Service Studio 11.55.94.65328.

Companion to [`2026-09-30-claude-oml-tool-claims.md`](2026-09-30-claude-oml-tool-claims.md). That file checked the skill's
claims about tool names, envelopes and response shapes. This file checks the **state model** the skill teaches (the
per-module "session pointer", `omlReset`, `omlMerge`) against what the server actually does, plus the catalogue
differences found on the way.

## Setup of the last live verification (2026-10-30)

| Item | Value |
|---|---|
| Service Studio | 11.55.94.65328 (`ServiceStudio.exe` file version, process started 07:16 local) |
| Server | in-process MCP server, `http://127.0.0.1:41820/mcp`, write permission left on **manual review** (Compare-and-Merge window) |
| Client | Claude Code (desktop app), `.mcp.json` of this repo without a per-server `"timeout"` |
| Module | a scratch Reactive Web module (many references, 30 TrueChange warnings) |
| Skill under test | this fork at `9f987e6` (upstream `main` as of 2026-10-30, commits `515b6df`, `2fafeda`, `9f987e6` included) |
| Date | 2026-10-30, 14:30 to 15:20 local |
| Token | issued once (after a first attempt timed out, see D9), never written to disk, not reproduced here |

Catalogue and schemas were taken from the server's live `tools/list` in the session (62 tools), not from memory.
Every host call after the first batch was made **one at a time** (see D16).

Status legend: **CONFIRMED** = observed live on 2026-10-30; **MATCHES** = the skill's claim held live; **DOC-ONLY** = evidence
comes only from an earlier session on a large customer module (2026-10-20) and was not reproduced live; **UNCONFIRMED** = not tested
(rules of the test forbade the call, or the user's confirmation is still pending).

## Discrepancy table

| # | Status | Skill claim (where) | Server behaviour | Evidence | Fixed upstream? | Proposed wording / action |
|---|---|---|---|---|---|---|
| D1 | CONFIRMED | The host keeps a per-module **session pointer** that auto-chains edits; no start or end step (SKILL.md § 3 steps 1-4, § 3.1, § 8; `reference/mcp-session-pointer.md`; `verb-reference.md` intro and host-only table; `ARCHITECTURE.md` T4; all adapters; `integration-examples/01-05`) | Writes need an **edit session**: `startOmlEditSession` returns `{"started":true}`; `applyModelApiCode` without one fails `No OML edit session is started. Call startOmlEditSession before making edits.`; a second start fails `An OML edit session is already started for this module. Call endOmlEditSession before starting a new one.`; `endOmlEditSession` returns `{"ended":true,"discardedChanges":bool}` and without a session fails `No OML edit session is started. Call startOmlEditSession first.` | live 14:45-15:18 | No. Neither tool name appears anywhere in `9f987e6` | Rewrite § 3, § 3.1 and the pointer reference around edit sessions; add both tools to § 8, the verb tables and every adapter |
| D2 | CONFIRMED | "Read tools auto-snapshot the live module on the first call and subsequently follow the session pointer" (§ 2) | Outside a session reads see the **live module**; the first sidecar read writes one `<runId>-in.oml` and later reads reuse it (one file survived four reads, 15:16:55); `startOmlEditSession` deletes that snapshot. Inside a session reads see the **working copy**, unmerged saves included (`getStructures` listed the new structure before any merge) | live | No | Reword § 2 and `verb-reference.md` intro |
| D3 | CONFIRMED | `applyModelApiCode` "snapshots to `<runId>-in.oml`, allocates `<runId>-out.oml`"; superseded out files are "best-effort deleted" (§ 3; `mcp-session-pointer.md`; `MCP-DESIGN-CHOICES.md` 1) | `startOmlEditSession` writes `<runId>-in.oml` (14,059,721 B). Each clean `applyModelApiCode` writes a new `<runId>-out.oml` and **deletes the previous working-copy file**, so a session owns exactly one file at any moment. `endOmlEditSession` deletes it. `omlReset` leaves it behind (orphan until Service Studio restarts). The folder itself does not exist until first use (absent at 14:31 with Service Studio running since 07:16) | PowerShell listings of `%TEMP%\ServiceStudio.MCPServer` after every call | No | Describe the one-file working copy; keep the "folder wiped at startup" note |
| D4 | CONFIRMED | `omlMerge` "clears that module's session pointer", "the chain does not continue past a merge", "a successful merge deletes that file, so a repeat fails with file-not-found" (§ 3.1; `verb-reference.md`; `mcp-session-pointer.md`; T4; `MCP-DESIGN-CHOICES.md` 3) | `omlMerge` **does not end the session and does not touch the working copy** (also stated in its live description). After the accepted merge a throw-probe and a further `applyModelApiCode` ran on the same working copy with no new start. The merged file stayed on disk after both the cancelled and the accepted merge; the **same path was merged twice** (cancelled, then accepted) | live 15:05-15:16; the accepted merge's response was lost to the client timeout (D9), the result was verified in the Service Studio tree and by an out-of-session `getStructures` | No | Rewrite § 3.1: merge as often as needed, end the session with `endOmlEditSession` |
| D5 | MATCHES (+message) | "Nothing is lost when the merge doesn't succeed (the OML stays on disk)" (§ 3.1) | Cancel in the window returns `{"merged":false,"message":"The merge was cancelled in the Compare and Merge window."}`; file and session intact | live 15:05 | n/a | Quote the message. The 2026-10-20 session saw the `-out.oml` vanish after a cancel; not reproduced, that call had already been aborted client-side (D9) |
| D6 | CONFIRMED | `omlReset` "drops the chain and re-snapshots the live module", "reversible within the session"; `discardedChanges:true` means "an unmerged chain was thrown away" (§ 3.1, § 8; `mcp-session-pointer.md`; `verb-reference.md`) | Inside a session `omlReset` returns `{"discardedChanges":true,"eSpaceName":...}` and **ends the edit session** (the next `applyModelApiCode` fails with "No OML edit session is started"); the last `-out.oml` stays on disk. Outside a session it returns `{"discardedChanges":false,...}` with no error. Bringing the orphan back through `omlMerge` was not tested | live 15:16 | No | Document `omlReset` as "ends the session, keeps the file"; recommend `endOmlEditSession` for a clean discard |
| D7 | CONFIRMED | `discardedChanges: true` = work lost (`mcp-session-pointer.md`, SKILL.md § 3.1) | `endOmlEditSession` returned `false` after a session with no saves and `true` after a session with one unmerged save; the 2026-10-20 session saw `true` after a successful merge with nothing pending. So `true` means "the working copy had saves", not "work was lost" | live 15:18 (both cases); 2026-10-20 session (merged case) | No | State the real meaning |
| D8 | CONFIRMED | `listApps` returns "the open module + its loaded references" (§ 2 table; `verb-reference.md`); example row has a `style` field (`module-style-support.md`) | Only the open module: `{"key","name","kind":"eSpace","version":"0","description","isOpen":true,"isReference":false,"moduleType":"Reactive"}`; no reference rows although the module references OutSystemsUI and others and the tool description promises them. `version` is `"0"` | live 14:42; the fork's own D10 (2026-09-30) and the 2026-10-20 session said the same | No | Fix both tables and the example row |
| D9 | CONFIRMED | Timeouts are the server's: 300 s writes, 120 s reads (§ 9; `MCP-DESIGN-CHOICES.md` 7). § 0 (`2fafeda`) says the token call waits for the dialog | Claude Code aborts any tool call silent for 300 s: `sent no response or progress for 300s; aborting`. Hit on `createSessionToken` (dialog unanswered; the token issued on the late approval is lost; a second call shows a second dialog and works) and on `omlMerge` under manual review (the window stayed open, the user accepted after the abort, the merge completed, the response was lost). Remedy: per-server `"timeout"` in `.mcp.json` (1800000 ms worked in the 2026-10-20 session) or `CLAUDE_CODE_MCP_TOOL_IDLE_TIMEOUT` | live 14:31-14:36 and 15:06-15:11; 2026-10-20 session | Partly: `2fafeda` added "tell the user to approve first"; the abort and the remedy are missing | Add a client-timeout paragraph to § 0 and § 9; add `"timeout"` to this repo's `.mcp.json` |
| D10 | CONFIRMED (size: DOC-ONLY) | A throw-probe still returns `validationMessages` (§ 1.1) | Every `applyModelApiCode`, probe included, returns the module's **complete** TrueChange list: 30 warnings and about 9 k characters here; 119 warnings and about 53 k characters on a large customer module, past the Claude Code inline result limit (spilled to a file) | live; 2026-10-20 session | No | Warn in § 1.1 and § 2; prefer the inventory probe's own string over the raw list on big modules |
| D11 | MATCHES (+fields) | `Screens` is not a field on `Root`; use `MobileFlows`/`WebFlows` (`verb-reference.md`, `2fafeda`) | `Screens`, `Blocks` and `UIFlows` are all rejected with `Schema is not configured correctly to fetch field 'X' from type 'ESpace'`; `Root { Name Key }` works; the live tool description still shows `Root { Screens { ... } }` | live 14:42-14:45 | Yes (`2fafeda`) for `Screens` | Add `Blocks` and `UIFlows` to the note |
| D12 | MATCHES | Send `sessionToken` to `omlPublish`, `omlRefreshReferences`, `getRoleExceptions` although their schemas omit it (§ 0, `2fafeda`) | `getRoleExceptions` without the token: `-32002 This tool requires a valid session token. Call createSessionToken first and pass the returned value as the sessionToken argument.` With the token (Claude Code accepted the undeclared property) it returns an empty lambda | live 14:38 and 14:46 | Yes (`2fafeda`) | Add the 2026-10-30 date |
| D13 | MATCHES | `imports` only `OutSystems.Model.*`; no `System.`-qualified names in `code`; `System` and `System.Linq` pre-imported (§ 1.1, § 4) | `imports:["System"]` fails `Import 'System' is not allowed. Only 'OutSystems.Model' (or one of its sub-namespaces) may be imported.`; `System.Exception` in code fails `'code' must only reference the OutSystems Model API - direct file, process, environment, reflection, registry, interop, or network access is not allowed (found 'System')`; bare `Exception`, `Count()`, `Any()`, `Single()` compile | live 14:50 | n/a | none |
| D14 | MATCHES | Probe response shape (§ 3) | `{"exceptionMessage":"PROBE:...","stdoutOutput":"","stderrOutput":"","validationMessages":[...],"mutatedOmlPath":""}` | live 14:52 | n/a | none |
| D15 | CONFIRMED | silent | The Compare-and-Merge window lists, next to the new element, a `(System)` entity reference flagged "Reference has changed" (`Dependencies > (System) > Entities > Database > Espace` on 2026-10-30, `Role` in the 2026-10-20 session); it labels the live module "Your changes" and the mutated OML "The other version" | user's screenshot 2026-10-30; 2026-10-20 session | No | Note in § 3.1 so the agent can explain the extra line |
| D16 | CONFIRMED (cause inferred) | silent (the chatmode lists "firing several calls at once" as a mistake, without a reason) | With seven calls in flight (reads, `omlReset`, an apply without a session) `getRoleExceptions` failed `[-32000] OML not found: ...\<runId>-in.oml`. The same read alone, and a pair of reads in parallel, succeeded. A host tool that re-snapshots (`omlReset`) most likely removed the snapshot the read was about to use | live 14:42 (failure), 14:46 and 15:17 (controls); observed once | No | "Call host tools one at a time; never run `omlReset` / `startOmlEditSession` / `endOmlEditSession` while a read is in flight" |
| D17 | CONFIRMED | absent | `getDataExposure` exists: `eSpaceName`, optional `includeServerSide` (default false), `includeSources` (default true); "data sources of a module that reach the browser ... grouped by entity". Not called | live `tools/list` | No | Add to the § 2 table and `verb-reference.md` as present, untested |
| D18 | CONFIRMED (partly) | `getSerializedObjectByKey` fetches "by key" (`verb-reference.md`) | The `key` that `listApps` returns is rejected `[-32602] Object with key '...' not found`; the module's model key from `runQuery Root { Key }` is a different GUID. The 2026-10-20 session: local element keys rejected too | live 14:45; 2026-10-20 session | No | Note which keys the tool does not accept |
| D19 | UNCONFIRMED | `omlRefreshReferences` "drops the session pointer" (§ 3.2; `verb-reference.md`; adapters; `integration-examples/03`) | Not called (only on explicit request). Behaviour with an open edit session unknown | none on 2026-10-30 | No | Reword as "verified 2026-09-30 under the pointer model; not re-verified with edit sessions" |
| D20 | UNCONFIRMED | `omlPublish` returns `blocked` until the write permission is granted; needs the token (§ 3.2, `2fafeda`) | Not called (never publish). Live schema: `input.eSpaceName` only; description matches the skill | live `tools/list` | Yes (`2fafeda`, 2026-10-17) | none |
| D21 | UNCONFIRMED | Menu "Edit > MCP Server" (README, `515b6df`); permission "allow automatic merge and publish" (§ 3.2) | The 2026-10-20 session on the same build: menu "Edit > MCP Configuration...", one "Write permissions" (Beta) setting with "Manual review changes in compare and merge" / "Allow automatically merge and publish". Not checked on 2026-10-30 | screenshots of the 2026-10-20 session | `515b6df` changed the label the other way | Ask the user to read both texts off the dialog before changing the README |
| D22 | CONFIRMED | `getRoleExceptions` works with the token (empty) (`verb-reference.md`, 2026-10-01) | Still true when called alone (see D16 for the parallel failure) | live 14:46 | n/a | add date |

## Live run log 2026-10-30 (condensed)

Times are local. Every row is one tool call unless marked "batch".

| Time | Call | Result |
|---|---|---|
| 14:31 | `createSessionToken` | no response for 300 s, aborted by Claude Code; the dialog had appeared in Service Studio and was approved later by the user |
| 14:38 | `getRoleExceptions` without token | `-32002 This tool requires a valid session token ...` |
| 14:42 | `createSessionToken` (user watching) | token returned at once after approval |
| 14:42 | batch of 7: `listApps`, `getScreenNames`, `getRoleExceptions`, `applyModelApiCode` (probe, no session), `runQuery` x2, `omlReset` | `listApps`: one row, no references. `getScreenNames`: works without a session (includes `IsReferenced:true` rows). `getRoleExceptions`: `OML not found: <runId>-in.oml`. probe: `No OML edit session is started`. `runQuery Blocks` / `Screens`: schema error. `omlReset`: `{"discardedChanges":false}` |
| 14:43 | `getDataModel`, `getUserExceptions` (parallel), `runQuery Root { Name }`, `runQuery UIFlows` | all reads work without a session; `UIFlows` rejected; folder now holds one `-in.oml` (14,059,721 B) |
| 14:45 | `startOmlEditSession` | `{"started":true}`; folder: one new `-in.oml`, the read snapshot gone |
| 14:45 | `runQuery Root { Name Key }`, `getSerializedObjectByKey(listApps key)` | key returned; `listApps` key rejected `not found` |
| 14:46 | `getRoleExceptions` alone | works; no new file in the folder |
| 14:50 | probe with `imports:["System"]`, then with `System.Exception` | both rejected by the sandbox as documented |
| 14:52 | probe `throw new Exception("PROBE:"+...)` | `exceptionMessage` populated, 30 warnings, `mutatedOmlPath:""`, no new file |
| 14:53 | `startOmlEditSession` again | `already started` error |
| 15:04 | `applyModelApiCode` creates structure `MCP_SkillCheck_20261030` | `mutatedOmlPath` = `<runId>-out.oml` (14,188,804 B); folder holds **only** that file; `getStructures` in session lists the structure |
| 15:05 | `omlMerge` (user cancels) | `{"merged":false,"message":"The merge was cancelled in the Compare and Merge window."}`; file still there; probe: 7 structures |
| 15:06 | `omlMerge` same path (user accepts) | no response for 300 s, aborted by Claude Code; the user accepted in the window; structure visible in the module tree; window also listed `(System) > Entities > Database > Espace` as "Reference has changed" |
| 15:15 | probe | session alive, 7 structures; the merged `-out.oml` still on disk |
| 15:16 | `applyModelApiCode` sets the structure description (change 3) | new `-out.oml` (14,188,676 B) replaces the previous file |
| 15:16 | `omlReset` | `{"discardedChanges":true}`; probe afterwards: `No OML edit session is started`; the `-out.oml` stays |
| 15:16 | `endOmlEditSession` | `No OML edit session is started. Call startOmlEditSession first.` |
| 15:16 | `getSerializedObjectByNameAndType(IStructure)`, `getStructures` (out of session) | live module has the structure with the **original** description (change 3 discarded); one read snapshot `-in.oml` appears and is reused |
| 15:17 | `getUserExceptions` + `getRoleExceptions` in parallel | both succeed |
| 15:18 | `startOmlEditSession`, `endOmlEditSession` | `{"ended":true,"discardedChanges":false}`; working copy deleted, orphan `-out.oml` from `omlReset` remains |
| 15:18 | `startOmlEditSession`, `applyModelApiCode` (change 4), `endOmlEditSession` | `{"ended":true,"discardedChanges":true}`; working copy deleted; `getStructures` confirms change 4 never reached the module |

Left behind on purpose: structure `MCP_SkillCheck_20261030` in the scratch module (unpublished, safe to delete) and one orphan
`-out.oml` in `%TEMP%\ServiceStudio.MCPServer` (gone at the next Service Studio start).

## Evidence from the 2026-10-20 session not reproduced live

- `-out.oml` vanished after a cancelled merge (session journal, 2026-10-20). On 2026-10-30 the file stayed after a cancel that
  completed inside the client timeout. That cancel followed a client-side abort; the difference may be the abort.
- `discardedChanges: true` from `endOmlEditSession` right after a successful merge with nothing pending. Consistent with
  D7 ("the working copy had saves"); not repeated to avoid a third merge window.
- Validation payload of 53 k characters (D10). The sandbox module is too small to reproduce the overflow.

## Open items

1. Read the exact menu label and the write-permission texts off Service Studio (D21) before touching the README.
2. Re-verify `omlRefreshReferences` with an open edit session (D19), only on explicit request.

Decided 2026-11-02: `omlReset` stays documented as a tool, with the advice to prefer `endOmlEditSession` (D6). The
`.mcp.json` `"timeout"` stays in this fork; no upstream PR for now (D9).

## Sync log

One line per sync run. "Rows changed" names the D-rows whose status or "Fixed upstream?" cell the run changed.

| Date | Upstream `main` | Service Studio on disk | Rows changed | Live check |
|---|---|---|---|---|
| 2026-11-02 | `9f987e6`, no commits since 2026-10-30 | 11.55.94.65328 | none (file created from the 2026-10-30 report) | none |
