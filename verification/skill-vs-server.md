# Skill vs. live server: `servicestudio-mcp-oml` against Service Studio

Living file. The weekly sync run updates the discrepancy table when upstream commits or a new Service Studio build
change a row, and adds a line to the sync log at the end; git history keeps the earlier states. Last full live
verification: 2026-10-30, Service Studio 11.55.94.65328; targeted live check of D5: 2026-11-03; targeted live check of D24
and reading of the MCP Server dialog (D21): 2026-11-20, Service Studio 11.55.97.65412; targeted live check of upstream
`be8e730`'s claims (D3, D4, D6, D7, D8, D19): 2026-11-20 evening, same build.

Companion to [`2026-09-30-claude-oml-tool-claims.md`](2026-09-30-claude-oml-tool-claims.md). That file checked the skill's
claims about tool names, envelopes and response shapes. This file checks the **state model** the skill teaches (the
per-module "session pointer", `omlReset`, `omlMerge`) against what the server actually does, plus the catalogue
differences found on the way.

## Setup of the last live verification (2026-10-30)

| Item | Value |
|---|---|
| Service Studio | 11.55.94.65328 (`ServiceStudio.exe` file version, process started 07:16 local) |
| Server | in-process MCP server, `http://127.0.0.1:41820/mcp`, write permission left on **manual review** (Compare-and-Merge window) |
| Client | Claude Code (desktop app); `servicestudio` loaded from an entry without a per-server `"timeout"` (neither this repo's `.mcp.json` nor the user-scope entry had one then; which of them the session used was not recorded, see D9) |
| Module | a scratch Reactive Web module (many references, 30 TrueChange warnings) |
| Skill under test | this fork at `9f987e6` (upstream `main` as of 2026-10-30, commits `515b6df`, `2fafeda`, `9f987e6` included) |
| Date | 2026-10-30, 14:30 to 15:20 local |
| Token | issued once (after a first attempt timed out, see D9), never written to disk, not reproduced here |

Catalogue and schemas were taken from the server's live `tools/list` in the session (62 tools), not from memory.
Every host call after the first batch was made **one at a time** (see D16).

Status legend: **CONFIRMED** = observed live on 2026-10-30; **MATCHES** = the skill's claim held live; **DOC-ONLY** = evidence
comes only from an earlier session on a large customer module (2026-10-20) and was not reproduced live; **UNCONFIRMED** = not tested
(rules of the test forbade the call, or the user's confirmation is still pending). **CONFIRMED (offline)** = reproduced with a script on a graph reconstructed from a live read-back, not in Service Studio.

## Discrepancy table

| # | Status | Skill claim (where) | Server behaviour | Evidence | Fixed upstream? | Proposed wording / action |
|---|---|---|---|---|---|---|
| D1 | CONFIRMED | The host keeps a per-module **session pointer** that auto-chains edits; no start or end step (SKILL.md § 3 steps 1-4, § 3.1, § 8; `reference/mcp-session-pointer.md`; `verb-reference.md` intro and host-only table; `ARCHITECTURE.md` T4; all adapters; `integration-examples/01-05`) | Writes need an **edit session**: `startOmlEditSession` returns `{"started":true}`; `applyModelApiCode` without one fails `No OML edit session is started. Call startOmlEditSession before making edits.`; a second start fails `An OML edit session is already started for this module. Call endOmlEditSession before starting a new one.`; `endOmlEditSession` returns `{"ended":true,"discardedChanges":bool}` and without a session fails `No OML edit session is started. Call startOmlEditSession first.` | live 14:45-15:18 | Yes (`be8e730`, SKILL.md § 2.1, the pointer reference, verb tables and adapters) | Done: the overlay took upstream's edit-session text in the 2026-11-20 merge |
| D2 | CONFIRMED | "Read tools auto-snapshot the live module on the first call and subsequently follow the session pointer" (§ 2) | Outside a session reads see the **live module**; the first sidecar read writes one `<runId>-in.oml` and later reads reuse it (one file survived four reads, 15:16:55); `startOmlEditSession` deletes that snapshot. Inside a session reads see the **working copy**, unmerged saves included (`getStructures` listed the new structure before any merge) | live | Partly (`be8e730`: reads see the working copy inside a session, a snapshot outside it) | Reword § 2 and `verb-reference.md` intro |
| D3 | CONFIRMED | `applyModelApiCode` "snapshots to `<runId>-in.oml`, allocates `<runId>-out.oml`"; superseded out files are "best-effort deleted" (§ 3; `mcp-session-pointer.md`; `mcp-design-choices.md` § 1) | `startOmlEditSession` writes `<runId>-in.oml` (14,059,721 B). Each clean `applyModelApiCode` writes a new `<runId>-out.oml` and **deletes the previous working-copy file**, so a session owns one file between calls. A throw-probe writes a second `-out.oml` and deletes it within 0.5 s (2026-11-03); the working copy is untouched. `endOmlEditSession` deletes it. `omlReset` leaves it behind (orphan until Service Studio restarts). The folder itself does not exist until first use (absent at 14:31 with Service Studio running since 07:16). `omlMerge` refuses the file `omlReset` left: `'mutatedOmlPath' does not match the working copy currently tracked for module '...'` (2026-11-20). An `omlRefreshReferences` that ends a session leaves its working copy on disk too (2026-11-20) | PowerShell listings of `%TEMP%\ServiceStudio.MCPServer` after every call; 0.5 s poll on 2026-11-03; live 2026-11-20 18:12-18:15 (see "Live check 2026-11-20 evening") | Yes (`be8e730`; its claim that `omlMerge` refuses the file held live 2026-11-20) | Upstream's wording taken in the 2026-11-20 merge; still to add: `omlRefreshReferences` leaves the file too |
| D4 | CONFIRMED | `omlMerge` "clears that module's session pointer", "the chain does not continue past a merge", "a successful merge deletes that file, so a repeat fails with file-not-found" (§ 3.1; `verb-reference.md`; `mcp-session-pointer.md`; T4; `mcp-design-choices.md` § 3) | `omlMerge` **does not end the session and does not touch the working copy** (also stated in its live description). After the accepted merge a throw-probe and a further `applyModelApiCode` ran on the same working copy with no new start. The merged file stayed on disk after both the cancelled and the accepted merge; the **same path was merged twice** (cancelled, then accepted) | live 15:05-15:16; the accepted merge's response was lost to the client timeout (D9), the result was verified in the Service Studio tree and by an out-of-session `getStructures`; again live 2026-11-20 18:12 (accepted merge, then a further `applyModelApiCode` in the same session) | Yes (`be8e730`) | Done: upstream's wording taken in the 2026-11-20 merge |
| D5 | MATCHES (+message) | "Nothing is lost when the merge doesn't succeed (the OML stays on disk)" (§ 3.1) | Cancel in the window returns `{"merged":false,"message":"The merge was cancelled in the Compare and Merge window."}`; file and session intact. Also when the client stops the call while the window is open and the user cancels afterwards: file and session intact, and the same path opens the window again | live 2026-10-30 15:05; live 2026-11-03 12:56-12:59 (see "Live check 2026-11-03") | n/a | Quote the message. The 2026-10-20 session saw the `-out.oml` vanish after a client-side abort followed by a cancel; the same order on 2026-11-03 kept the file, so the abort alone does not explain it; cause unknown |
| D6 | CONFIRMED | `omlReset` "drops the chain and re-snapshots the live module", "reversible within the session"; `discardedChanges:true` means "an unmerged chain was thrown away" (§ 3.1, § 8; `mcp-session-pointer.md`; `verb-reference.md`) | Inside a session `omlReset` returns `{"discardedChanges":true,"eSpaceName":...}` and **ends the edit session** (the next `applyModelApiCode` fails with "No OML edit session is started"); the last `-out.oml` stays on disk. Outside a session it returns `{"discardedChanges":false,...}` with no error. Inside a session with nothing unmerged it returns `discardedChanges:false` and the session stays open (a throw-probe ran right after it). `omlMerge` refuses the orphan (see D3), so a reset can't be undone | live 15:16; live 2026-11-20 18:11 and 18:13 | Yes (`be8e730`; both its added claims held live 2026-11-20) | Done: upstream's wording taken in the 2026-11-20 merge |
| D7 | CONFIRMED | `discardedChanges: true` = work lost (`mcp-session-pointer.md`, SKILL.md § 3.1) | `endOmlEditSession` returned `false` after a session with no saves and `true` after a session with one unmerged save; the 2026-10-20 session saw `true` after a successful merge with nothing pending, and so did two sessions on 2026-11-20 (each: one save, one accepted merge, then end). So `true` means "the working copy had saves", not "work was lost" | live 15:18 (both cases); 2026-10-20 session (merged case); live 2026-11-20 17:37 and 17:45 (merged case, 11.55.97.65412); live 2026-11-20 18:15 (third merged case: one save, accepted merge, end) | No: `be8e730` contradicts the live result (it says `true` means edits applied after the last merge, and to expect `false` after a merge; it records `false` after a fully merged session on 2026-11-19, probably on a newer build) | Done in the 2026-11-20 merge: the overlay keeps upstream's text and adds that on 11.55.97.65412 and earlier builds `true` means only that the working copy had saves (SKILL.md § 2.1, verb reference, example 01) |
| D8 | CONFIRMED | `listApps` returns "the open module + its loaded references" (§ 2 table; `verb-reference.md`); example row has a `style` field (`module-style-support.md`) | Only the open module: `{"key","name","kind":"eSpace","version":"0","description","isOpen":true,"isReference":false,"moduleType":"Reactive"}`; no reference rows although the module references OutSystemsUI and others and the tool description promises them. `version` is `"0"`. On 11.55.97.65412 (2026-11-20) still one row and no references, but `version` is a non-zero number (`"245709"`); at 18:11 the same day one row again, no references, `version` `"0"`, no `tabId`. This build advertises `listApps` and none of `listOpenModules`, `searchApps`, `openModule`, `closeModule` | live 14:42; the fork's own D10 (2026-09-30) and the 2026-10-20 session said the same; live 2026-11-20 17:34 and 18:11 (`tools/list` of the session) | No: `be8e730` renames the tool `listOpenModules` (`listApps` on older builds; the 2026-11-20 check called `listApps` on 11.55.97.65412) and still promises the references for both | Done for `listApps` in the 2026-11-20 merge (SKILL.md § 2 table, verb reference, module discovery); check `listOpenModules` live on a build that has it |
| D9 | CONFIRMED | Timeouts are the server's: 300 s writes, 120 s reads (§ 9; `mcp-design-choices.md` § 7). § 0 (`2fafeda`) says the token call waits for the dialog | Claude Code aborts any tool call silent for 300 s: `sent no response or progress for 300s; aborting`. Hit on `createSessionToken` (dialog unanswered; the token issued on the late approval is lost; a second call shows a second dialog and works) and on `omlMerge` under manual review (the window stayed open, the user accepted after the abort, the merge completed, the response was lost). Remedy: per-server `"timeout"` on the entry the client loads (its effect on a call longer than 300 s is not measured yet) or `CLAUDE_CODE_MCP_TOOL_IDLE_TIMEOUT`. Which entry loads depends on the client surface: this repo approves its `.mcp.json` servers in its tracked `.claude/settings.json` (`enabledMcpjsonServers`, since the seed commit), and a Claude Code desktop-app session in this repo lists `servicestudio` as a project server (connector status, 2026-11-03), so it uses the `.mcp.json` entry; the CLI's `claude mcp get servicestudio` in the same folder reports the user-scope entry ("Scope: User config", 2026-11-02). Neither entry had a `"timeout"` on 2026-10-30 (this repo's `.mcp.json` got 1800000 ms in `a14076c`). Claude Code 2.1.303 defaults the idle limit to 300 s for HTTP servers (30 min for stdio); a per-server `"timeout"` raises it and also caps the call's total time | live 14:31-14:36 and 15:06-15:11; 2026-10-20 session; `claude mcp get` and the Claude Code 2.1.303 binary, 2026-11-02; desktop-app connector status, 2026-11-03 | Partly: `2fafeda` added "tell the user to approve first"; the abort and the remedy are missing | Add a client-timeout paragraph to § 9 that says which entry each client surface loads, and point § 0, § 3.1 and the other mentions to it; add `"timeout"` to this repo's `.mcp.json` |
| D10 | CONFIRMED (size: DOC-ONLY) | A throw-probe still returns `validationMessages` (§ 1.1) | Every `applyModelApiCode`, probe included, returns the module's **complete** TrueChange list: 30 warnings and about 9 k characters here; 119 warnings and about 53 k characters on a large customer module, past the Claude Code inline result limit (spilled to a file) | live; 2026-10-20 session | No | Warn in § 1.1 and § 2; prefer the inventory probe's own string over the raw list on big modules |
| D11 | MATCHES (+fields) | `Screens` is not a field on `Root`; use `MobileFlows`/`WebFlows` (`verb-reference.md`, `2fafeda`) | `Screens`, `Blocks` and `UIFlows` are all rejected with `Schema is not configured correctly to fetch field 'X' from type 'ESpace'`; `Root { Name Key }` works; the live tool description still shows `Root { Screens { ... } }` | live 14:42-14:45 | Yes (`2fafeda`) for `Screens` | Add `Blocks` and `UIFlows` to the note |
| D12 | MATCHES | Send `sessionToken` to `omlPublish`, `omlRefreshReferences`, `getRoleExceptions` although their schemas omit it (§ 0, `2fafeda`) | `getRoleExceptions` without the token: `-32002 This tool requires a valid session token. Call createSessionToken first and pass the returned value as the sessionToken argument.` With the token (Claude Code accepted the undeclared property) it returns an empty lambda | live 14:38 and 14:46 | Yes (`2fafeda`); the `be8e730` chatmode says `omlPublish` and `omlRefreshReferences` "take none" | Done: the 2026-10-30 date added, and the chatmode corrected in the overlay (2026-11-20 merge) |
| D13 | MATCHES | `imports` only `OutSystems.Model.*`; no `System.`-qualified names in `code`; `System` and `System.Linq` pre-imported (§ 1.1, § 4) | `imports:["System"]` fails `Import 'System' is not allowed. Only 'OutSystems.Model' (or one of its sub-namespaces) may be imported.`; `System.Exception` in code fails `'code' must only reference the OutSystems Model API - direct file, process, environment, reflection, registry, interop, or network access is not allowed (found 'System')`; bare `Exception`, `Count()`, `Any()`, `Single()` compile | live 14:50 | n/a | none |
| D14 | MATCHES | Probe response shape (§ 3) | `{"exceptionMessage":"PROBE:...","stdoutOutput":"","stderrOutput":"","validationMessages":[...],"mutatedOmlPath":""}` | live 14:52 | n/a | none |
| D15 | CONFIRMED | silent | The Compare-and-Merge window lists, next to the new element, a `(System)` entity reference flagged "Reference has changed" (`Dependencies > (System) > Entities > Database > Espace` on 2026-10-30 and 2026-11-03, `Role` in the 2026-10-20 session) until that reference change has been merged once: on 2026-11-03 it was in the window at the second and third merge of the same unmerged path, and in the 2026-10-20 session it was gone at the second merge, after the first had merged it (not recorded for the cancelled merge of 2026-10-30 15:05 nor for the first merge of 2026-11-03); it labels the live module "Your changes" and the mutated OML "The other version" | user's screenshot 2026-10-30 (the accepted merge, 15:06); user's report 2026-11-03; 2026-10-20 session | No | Note in § 3.1 that the window may show the extra line until it has been merged once, so the agent can explain it |
| D16 | CONFIRMED (cause inferred) | silent (the chatmode lists "firing several calls at once" as a mistake, without a reason) | With seven calls in flight (reads, `omlReset`, an apply without a session) `getRoleExceptions` failed `[-32000] OML not found: ...\<runId>-in.oml`. The same read alone, and a pair of reads in parallel, succeeded. A host tool that re-snapshots (`omlReset`) most likely removed the snapshot the read was about to use | live 14:42 (failure), 14:46 and 15:17 (controls); observed once | Partly (`be8e730`: parallel reads outside a session race on the snapshot file; nothing about `omlReset` or the session calls while a read is in flight) | "Call host tools one at a time; never run `omlReset` / `startOmlEditSession` / `endOmlEditSession` while a read is in flight" |
| D17 | CONFIRMED | absent | `getDataExposure` exists: `eSpaceName`, optional `includeServerSide` (default false), `includeSources` (default true); "data sources of a module that reach the browser ... grouped by entity". Not called | live `tools/list` | No | Add to the § 2 table and `verb-reference.md` as present, untested |
| D18 | CONFIRMED (partly) | `getSerializedObjectByKey` fetches "by key" (`verb-reference.md`) | The `key` that `listApps` returns is rejected `[-32602] Object with key '...' not found`; the module's model key from `runQuery Root { Key }` is a different GUID. The 2026-10-20 session: local element keys rejected too | live 14:45; 2026-10-20 session | No | Note which keys the tool does not accept |
| D19 | CONFIRMED (with unmerged edits, 2026-11-20) | `omlRefreshReferences` "drops the session pointer" (§ 3.2; `verb-reference.md`; adapters; `integration-examples/03`) | Inside a session with one unmerged save it returned `{"message":"References were refreshed to their latest version. The module currently has validation warnings.","hasErrors":false,"hasWarnings":true}` and ended the session (the next `applyModelApiCode` failed with "No OML edit session is started"); the unmerged save never reached the module and its working copy stayed on disk. Outside a session, or in a session with nothing unmerged: not tested | live 2026-11-20 18:13-18:14, called on the user's explicit request | Yes (`be8e730`; its claim held live 2026-11-20) | Done: upstream's wording taken in the 2026-11-20 merge |
| D20 | UNCONFIRMED | `omlPublish` returns `blocked` until the write permission is granted; needs the token (§ 3.2, `2fafeda`) | Not called (never publish). Live schema: `input.eSpaceName` only; description matches the skill | live `tools/list` | Yes (`2fafeda`, 2026-10-17) | none |
| D21 | MATCHES (menu); CONFIRMED (permission texts, 2026-11-20) | Menu "Edit > MCP Server" (README, `515b6df`); permission "allow automatic merge and publish" (§ 3.2) | Menu read off Service Studio 11.55.94 on 2026-11-04: `Edit > MCP Server...`, then `Start MCP Server`; the Edit menu has no "MCP Configuration..." item, so the 2026-10-20 label was wrong. The dialog, titled "MCP Server", on 11.55.97.65412: section "MCP Server Settings" (Port 41820, "Running", the URL, "Read timeout" 120 Seconds, "Write timeout" 300 Seconds, "Auto-start when Service Studio opens", a note "Your agent also needs the Service Studio skills. Get started with OutSystems MCP"); section "Writing permissions" (Beta) with two options, "Manually review changes in Compare and Merge" (selected) and "Allow automatically merge and publish", the latter with the warning "Unpublished changes can be overwritten by the agent"; buttons "Stop MCP server" and "Close". The 2026-10-20 wording ("Write permissions", "Manual review changes in compare and merge") was a misreading or an older build | user's reading 2026-11-04 (menu); user's screenshot of the dialog 2026-11-20 | `515b6df` has the right label | Quote both option labels and the warning in § 3.2; mention that the read and write timeouts are settings in this dialog (see D9) |
| D22 | CONFIRMED | `getRoleExceptions` works with the token (empty) (`verb-reference.md`, 2026-10-01) | Still true when called alone (see D16 for the parallel failure) | live 14:46 | n/a | add date |
| D23 | CONFIRMED (handshake: one observation) | "You get an 'allow this agent?' prompt on every MCP handshake, including reconnects" (`reference/mcp-design-choices.md` § 5) | The dialog, titled "Allow AI agent to connect?", comes with each `createSessionToken` call, and a second call shows a second dialog. A bare handshake (`claude mcp get servicestudio` from the CLI) connected in about 2 s and showed no dialog, with the server running since the previous day | live 2026-10-30 14:31 and 14:42 (D9); 2026-11-03 12:41 (window poll); 2026-11-04 07:18 (user watching) | No: `be8e730` keeps "every MCP handshake, including reconnects" | Rewrite § 5 as one approval per `createSessionToken` call (done in the overlay, kept over upstream's text in the 2026-11-20 merge) |
| D24 | CONFIRMED (offline); fix CONFIRMED live (2026-11-20) | The full layout pass ends with "a final check that no two nodes share a grid cell and no forward edge points upward" (SKILL.md § 3.1; `reference/model-api-tips.md` § "The validated layout pass"; `examples/LayoutActionFlow.cs`) | Not a server behaviour: the recipe's own gap. A new server action built in one edit session (about 40 nodes, three sequential For Each loops, each body an If to 2-3 action calls and a log call, plus an exception handler) passed that check and was unreadable on the canvas; the developer re-laid it out by hand after the merge. Cause: the pass drops loop-back edges for placement, which also drops the only link between a body and the loop's exit, so each exit, and with it the next loop, landed one row under its For Each. The three For Each nodes ended up on consecutive rows, the later bodies were pushed right onto the earlier bodies' rows, each later cycle connector ran horizontally through an earlier body's node and the main flow sat beside the first body. No cell was shared and no forward edge pointed up | positions read back from the live module right after the merge (2026-11-09 17:16, client project, not reproduced here); offline check 2026-11-09, see "Offline check 2026-11-09 (D24)"; live check of the new pass 2026-11-20, see "Live check 2026-11-20 (D24, D21)" | No | Exit of a For Each below its body's deepest node; check extended with loop-block bounding boxes and connectors through nodes (done in the overlay, verified live on a scratch module 2026-11-20) |

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

## Live check 2026-11-03 (D5)

Service Studio 11.55.94.65328, write permission on manual review (`McpAutoMergeAndPublishAllowed` false in its settings
file). A scratch Reactive Web module; a second module was open in the same Service Studio and was not touched. Client:
Claude Code desktop app, whose connector status listed `servicestudio` as a project server, that is this repo's
`.mcp.json` entry with `"timeout": 1800000`. Times are local. Window and file times come from a 0.5 s poll of the Service
Studio windows and of `%TEMP%\ServiceStudio.MCPServer`; the stop time comes from the client transcript.

| Time | Call or event | Result |
|---|---|---|
| 12:41 | `createSessionToken` | approval dialog 12:41:25-12:41:28, token returned |
| 12:41 | `startOmlEditSession` | `{"started":true}`; one `-in.oml` (14,059,691 B) |
| 12:42 | `applyModelApiCode` creates structure `MCP_D5Check_20261103` | the `-in.oml` replaced by one `<runId>-out.oml` (14,189,252 B), copied outside the folder |
| 12:42:21 | `omlMerge` | window open 12:42:28; the planned client-side stop did not happen (Esc was pressed in Service Studio, not in the client); user cancelled 12:47:14-12:47:18; cancel message as in D5 at 12:47:19; file intact |
| 12:56:09 | `omlMerge` same path | window open 12:56:21, listing `(System) > ... > Espace` "Reference has changed"; **client stopped the call 12:56:30**; user cancelled 12:56:41-12:56:43; file intact (same size and time) |
| 12:57:50 | throw-probe | session alive, structure present in the working copy; a second `-out.oml` (14,189,155 B) appeared and was deleted within 0.5 s; working copy unchanged |
| 12:58:06 | `omlMerge` same path | window open again 12:58:24, same `(System)` line; user cancelled 12:59:06; cancel message as in D5 |
| 12:59:24 | `endOmlEditSession` | `{"ended":true,"discardedChanges":true}`; folder empty |

Nothing reached the module: all three merges were cancelled.

## Offline check 2026-11-09 (D24)

Input: the node list (kind, position, outgoing connectors, If/Switch/For Each targets) of the failed flow, read back from the live
module right after the merge. Only node kinds and connector structure were used; names stay in the client project. Two harnesses: a
Python port of the pass and checks, and the C# lambdas of `examples/LayoutActionFlow.cs` (the `HEAD` version and the new one) compiled
against stub `OutSystems.Model.Logic.Nodes` interfaces with the .NET 8 SDK and run on the same graph.

| Run | Result |
|---|---|
| previous lambda on the graph | positions equal to the agent's layout for 39 of 39 nodes (comment node excluded), so the agent ran the documented pass; old check passes |
| new check on those positions | fails: the three loop boxes hold 6, 14 and 15 foreign nodes; two cycle connectors run through 3 nodes of earlier bodies; one If-to-For Each loop-back runs through a node of another loop |
| new lambda on the graph | check passes; the three loops occupy rows 7-13, 14-19 and 20-25, column 0 stays empty beside each body, the main flow continues at row 26 |
| synthetic flows | the 2026-10-17 shape (If merge, 3-way Switch, one For Each, handler) passes with both versions; nested loops, and a loop with an early exit followed by a second loop, fail the new check with the old placement and pass it with the new one |

Not covered: how Service Studio draws the result (straight connectors are assumed, which matches the absence of a waypoint API), and
label width. Column spacing is 1828 units and long action names are wider than an icon, so labels of neighbouring columns can still
touch; the check works in grid cells and does not see that.

## Live check 2026-11-20 (D24, D21)

Service Studio 11.55.97.65412, write permission on manual review. A scratch Reactive Web module, the only module open. Client: Claude
Code desktop app with this repo's `.mcp.json` entry. Every call one at a time; times are local.

| Time | Call or event | Result |
|---|---|---|
| 17:34 | `createSessionToken` | approval dialog, token returned after the user's Allow |
| 17:34 | `listApps` | one row, no references, `version` `"245709"` (D8) |
| 17:35 | `startOmlEditSession` | `{"started":true}` |
| 17:35 | `applyModelApiCode` creates server action `D24LoopLayoutCheck` and runs the pass of `examples/LayoutActionFlow.cs` (overlay `175b096`) unchanged in the same lambda | clean run; new check passed; TrueChange: only "Unused user action" on the new action. Shape: Start, three sequential For Each loops over Text lists, each body an If whose True branch has two Assigns and whose False branch has one, both joining a "log" Assign that cycles back; then a summary Assign and End; an exception handler with its own Assign and End; 24 nodes |
| 17:36 | throw-probe of node positions (grid cells) | Start (0,0); For Each nodes at (0,1), (0,5), (0,9); each body in columns 1-2 over the For Each's row and the three below it; summary Assign (0,13), End (0,14); handler chain in column 4, rows 0-2 |
| 17:36 | `omlMerge` | user accepted in the Compare-and-Merge window; response `{"merged":true,"message":"All of the agent's changes were merged."}` |
| 17:37 | `endOmlEditSession` | `{"ended":true,"discardedChanges":true}` with nothing unmerged (D7); `%TEMP%\ServiceStudio.MCPServer` empty |
| 17:43 | canvas, user's screenshot | the three loops stand one below the other, each with its body to the right; no node or label overlaps another; the loop-back connectors run left of their own body and cross no other block; the main flow continues in column 0 below the third loop; the handler sits apart, top right. One cosmetic detail: the diagonal False-branch connector ends on the merge Assign's label |
| 17:43 | MCP Server dialog, user's screenshot | texts as in D21 |
| 17:44 | `startOmlEditSession`, `applyModelApiCode` (delete the action), `omlMerge` (user accepted), `endOmlEditSession` | `merged:true` with the same message; `ended:true, discardedChanges:true`; out-of-session `getActionNames` no longer lists the action |

Nothing is left in the module. Not covered: a flow with long action names, where labels are wider than the 1828-unit column.

## Live check 2026-11-20 evening (D3, D4, D6, D7, D8, D19)

Run of the weekly sync after upstream `be8e730` (edit sessions and module tools). Service Studio 11.55.97.65412, write permission on
manual review, the same scratch Reactive Web module, the only module open. Client: Claude Code desktop app with this repo's
`.mcp.json` entry. Every call one at a time; times are local. D16 was not tested: it needs calls in parallel. D21 was not read again:
the dialog was read on the same build at 17:43.

| Time | Call or event | Result |
|---|---|---|
| 18:11 | `createSessionToken` | approval dialog, token returned after the user's Allow |
| 18:11 | `listApps` | one row, no references, `version` `"0"`, no `tabId` (D8) |
| 18:11 | `startOmlEditSession`, `omlReset`, throw-probe | `{"started":true}`; `{"discardedChanges":false}`; the probe ran, so the session stayed open (D6) |
| 18:12 | `applyModelApiCode` x2: structure `MCP_SyncCheck_20261120` (description "v1"), then an attribute | first save carried the TrueChange error "must have at least one attribute", the second cleared it; one `-out.oml` on disk, the earlier one deleted (D3) |
| 18:12 | `omlMerge` | user accepted; `{"merged":true,"message":"All of the agent's changes were merged."}` |
| 18:13 | `applyModelApiCode` (description "v2") in the same session | clean run, new `-out.oml` (D4) |
| 18:13 | `omlReset` | `{"discardedChanges":true}` |
| 18:13 | `omlMerge` with the reset's `-out.oml` | refused: `'mutatedOmlPath' does not match the working copy currently tracked for module '...'`; no window opened (D3, D6) |
| 18:13 | throw-probe | `No OML edit session is started`; the reset's file still on disk |
| 18:13 | `startOmlEditSession`, `applyModelApiCode` (description "v3") | clean run |
| 18:14 | `omlRefreshReferences` (on the user's explicit request) | `{"message":"References were refreshed to their latest version. The module currently has validation warnings.","hasErrors":false,"hasWarnings":true}` |
| 18:14 | throw-probe | `No OML edit session is started` (D19); the session's `-out.oml` still on disk |
| 18:14 | `startOmlEditSession`, `applyModelApiCode` (asserts the description is "v1", deletes the structure) | assertion held, so neither "v2" nor "v3" reached the module |
| 18:15 | `omlMerge` | user accepted; `merged:true`, same message |
| 18:15 | `endOmlEditSession` | `{"ended":true,"discardedChanges":true}` with nothing unmerged (D7, third time) |
| 18:15 | out-of-session `getStructures` | the structure is gone; folder holds one read snapshot `-in.oml` and the two `-out.oml` files left by `omlReset` and `omlRefreshReferences` |

Nothing is left in the module. The module's references were refreshed to their latest versions by the D19 call.

## Evidence from the 2026-10-20 session not reproduced live

- `-out.oml` vanished after a cancelled merge (session journal, 2026-10-20); there the client had aborted the call after
  300 s with the window still open, and the user cancelled afterwards. On 2026-10-30 the file stayed after a cancel that
  completed inside the client timeout. On 2026-11-03 the 2026-10-20 order was repeated on the scratch module (client stop
  while the window was open, then cancel) and the file stayed too (D5). Differences left: the 2026-10-20 abort came from
  the client's 300 s idle limit, not from a user stop; the module was a large customer module; what happened there after
  the cancel was not recorded.
- `discardedChanges: true` from `endOmlEditSession` right after a successful merge with nothing pending. Consistent with
  D7 ("the working copy had saves"); not repeated to avoid a third merge window.
- Validation payload of 53 k characters (D10). The sandbox module is too small to reproduce the overflow.

## Open items

1. Bring § 3.2 in line with the write-permission texts read on 2026-11-20 (D21).
2. Add to the skill that an `omlRefreshReferences` which ends a session leaves its working copy on disk (D3).

Done 2026-11-20: the write-permission texts were read off the dialog (D21), and the new layout pass ran live on a scratch module
with three sequential loops (D24). Also done 2026-11-20: `omlRefreshReferences` with an open edit session (D19), and the merge of upstream `be8e730` into
the overlay. That merge took upstream's text in every conflicting file and then re-applied what upstream lacks or states
against the live results: D7, D8 (for `listApps`), D9, D10, D11, D12 (the chatmode's "take none"), D15, D16, D17, D18, D23 and
D24 (its eval scenario renumbered `scenario_28`, since upstream took 25-27).

Decided 2026-11-02: `omlReset` stays documented as a tool, with the advice to prefer `endOmlEditSession` (D6). The
`.mcp.json` `"timeout"` stays in this fork; no upstream PR for now (D9).

## Upstream PRs the overlay carries

The overlay merges these PR branches so the installed skill has their fixes before upstream merges them. The sync run
checks each PR's state; once upstream merges one, its row says so and the next merge of `main` brings the same change.

| PR | Branch | Change | State (checked) |
|---|---|---|---|
| [OutSystems/outsystems11-mcp#6](https://github.com/OutSystems/outsystems11-mcp/pull/6) | `fix/mcp-design-choices-link` | moves `MCP-DESIGN-CHOICES.md` to `servicestudio-mcp-oml/reference/mcp-design-choices.md`, so the SKILL.md § 6 link works in an installed skill | closed without merge 2026-11-20 (checked 2026-11-20); the same move landed upstream in `be8e730` |

## What the overlay carries

Every fix the overlay holds over upstream `main`, one line per place, as `row  path  ::  anchor`. The anchor is a phrase
that `git grep -F` finds in that file on `skill-overlay` and not on `main`. After each merge of `main` the sync run checks
every line: an anchor that is gone means the merge dropped that fix, unless upstream now says the same thing (then the line
goes, and the D-row's "Fixed upstream?" cell says so). Lines are added with each new overlay fix and removed when upstream
takes the fix over. Built 2026-11-20 from the diff of `e8d149e` against `be8e730`.

```text
D7   servicestudio-mcp-oml/SKILL.md  ::  the flag says only that the
D7   servicestudio-mcp-oml/reference/verb-reference.md  ::  it is `true` whenever the working copy had saves
D7   servicestudio-mcp-oml/integration-examples/01-add-entity-via-mcp.md  ::  there the flag means only that the working copy had saves
D8   servicestudio-mcp-oml/SKILL.md  ::  `listApps` returned only the open module, with no reference rows
D8   servicestudio-mcp-oml/reference/verb-reference.md  ::  with no reference rows and no `tabId`, despite its tool description
D8   servicestudio-mcp-oml/reference/module-discovery.md  ::  no reference rows came back at all
D9   servicestudio-mcp-oml/SKILL.md  ::  Your client has a clock of its own
D9   servicestudio-mcp-oml/SKILL.md  ::  Under manual review the call waits for the user, and your client may give up
D9   servicestudio-mcp-oml/SKILL.md  ::  Your MCP client has a clock of its own
D9   servicestudio-mcp-oml/reference/verb-reference.md  ::  blocks until the user answers the dialog
D9   servicestudio-mcp-oml/reference/verb-reference.md  ::  Claude Code aborts after 300 s of silence while the merge still completes
D9   servicestudio-mcp-oml/reference/mcp-session-pointer.md  ::  Your client may give up first
D9   servicestudio-mcp-oml/reference/mcp-design-choices.md  ::  Your MCP client has a limit of its own
D9   servicestudio-mcp-oml/integration-examples/01-add-entity-via-mcp.md  ::  make sure your client's tool timeout allows it
D9   AGENTS.md  ::  a token approved later is lost
D9   AGENTS.md  ::  merge waits for the
D9   .github/copilot-instructions.md  ::  a token approved later is lost
D9   .github/copilot-instructions.md  ::  make sure your client's tool timeout allows it
D9   .github/chatmodes/servicestudio-mcp-oml.chatmode.md  ::  a token approved later is lost
D9   .github/chatmodes/servicestudio-mcp-oml.chatmode.md  ::  after an abort, ask the
D9   .github/prompts/oml-edit.prompt.md  ::  make sure your client's tool timeout allows it
D9   .mcp.json  ::  "timeout": 1800000
D10  servicestudio-mcp-oml/SKILL.md  ::  alone can exceed your client's inline result
D10  servicestudio-mcp-oml/reference/lambda-contract.md  ::  the whole module's list
D10  AGENTS.md  ::  the whole module's
D10  .github/copilot-instructions.md  ::  the whole module's
D11  servicestudio-mcp-oml/reference/verb-reference.md  ::  `Screens`, `Blocks` and `UIFlows` are **not** fields
D12  .github/chatmodes/servicestudio-mcp-oml.chatmode.md  ::  but a call without it is rejected
D15  servicestudio-mcp-oml/SKILL.md  ::  The window may show one extra line
D15  servicestudio-mcp-oml/reference/mcp-session-pointer.md  ::  The window may list a `(System)` entity reference
D16  servicestudio-mcp-oml/SKILL.md  ::  while another call to the same server is still in flight
D16  servicestudio-mcp-oml/reference/verb-reference.md  ::  while another call is in flight
D16  servicestudio-mcp-oml/reference/mcp-session-pointer.md  ::  while another call is in flight
D16  AGENTS.md  ::  is in flight: call host tools one at a time
D16  .github/copilot-instructions.md  ::  is in flight: call host tools one at a time
D16  .github/chatmodes/servicestudio-mcp-oml.chatmode.md  ::  while another call is in flight
D17  servicestudio-mcp-oml/SKILL.md  ::  Data sources that reach the browser
D17  servicestudio-mcp-oml/reference/verb-reference.md  ::  Data sources that reach the browser
D18  servicestudio-mcp-oml/SKILL.md  ::  is not a model key
D18  servicestudio-mcp-oml/reference/verb-reference.md  ::  is not one and fails with
D23  servicestudio-mcp-oml/reference/mcp-design-choices.md  ::  Every agent session needs approval
D24  servicestudio-mcp-oml/SKILL.md  ::  each For Each laid out as a block
D24  servicestudio-mcp-oml/SKILL.md  ::  sits inside a loop block's bounding box
D24  servicestudio-mcp-oml/effectiveness.yml  ::  scenario_28:
D24  servicestudio-mcp-oml/examples/LayoutActionFlow.cs  ::  A loop's body: what the cycle reaches before coming back
D24  servicestudio-mcp-oml/reference/model-api-tips.md  ::  Lay out each For Each as a block.
D24  servicestudio-mcp-oml/reference/logic-and-integrations.md  ::  and the exit goes below the body's deepest node
```

## Sync log

One line per sync run that finds a new upstream commit, a new Service Studio build or a changed PR state, or that runs a
live check. "Rows changed" names the D-rows whose status or "Fixed upstream?" cell the run changed.

| Date | Upstream `main` | Service Studio on disk | Rows changed | Live check |
|---|---|---|---|---|
| 2026-11-02 | `9f987e6`, no commits since 2026-10-30 | 11.55.94.65328 | none (file created from the 2026-10-30 report) | none |
| 2026-11-02 | `8f36d5a`, 1 new commit (#13, property labels vs. model names; touches no D-row) | 11.55.94.65328 | none | none |
| 2026-11-03 | `8f36d5a`, no commits since 2026-11-02 | 11.55.94.65328 | none | D5 scenario (client stop while the Compare-and-Merge window was open, then cancel): file kept; see "Live check 2026-11-03" |
| 2026-11-09 | `8f36d5a`, no commits since 2026-11-03 | 11.55.95.65352 (new build) | none | proposed (new Service Studio build) |
| 2026-11-20 | `8f36d5a` still the last merged; `be8e730` (#16, edit sessions and module tools) seen upstream and left for the next sync run | 11.55.97.65412 (new build, first seen 2026-11-16) | D7, D8, D21, D24 | D24 fix verified on the canvas; D21 dialog texts read; see "Live check 2026-11-20 (D24, D21)" |
| 2026-11-20 | `be8e730`, 1 new commit (#16, edit sessions and module tools); `main` fast-forwarded; merged into the overlay after the live check (18 conflicting files taken from upstream, overlay-only fixes re-applied) | 11.55.97.65412 | D1, D2, D3, D4, D6, D7, D8, D16, D19 | upstream's session claims checked (D3, D4, D6, D19 hold; D7 and D8 still contradict it); the new module tools are absent on this build; see "Live check 2026-11-20 evening" |
