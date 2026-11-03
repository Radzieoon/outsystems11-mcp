# MCP edit sessions: `startOmlEditSession`, `applyModelApiCode`, `omlMerge`, `endOmlEditSession`, `omlReset`

> File name kept for link stability. Earlier builds of the server kept an implicit per-module
> "session pointer" that this file used to describe. Since Service Studio 11.55.94 the state is an
> explicit **edit session** per module, opened and closed by the agent. Everything below was
> verified live on 2026-10-30 unless a line says otherwise
> (see [`../../verification/skill-vs-server.md`](../../verification/skill-vs-server.md)).

## The model in one paragraph

A write starts with `startOmlEditSession`, which checks out a **working copy** of the live module
under `%TEMP%\ServiceStudio.MCPServer\<runId>-in.oml`. Every clean `applyModelApiCode` saves a new
`<runId>-out.oml`, deletes the previous working-copy file and makes the new file the working copy; the
response hands you its path as `mutatedOmlPath`. While the session is open, every read tool shows the
working copy, so your unmerged saves are visible to you and invisible to the user. `omlMerge` copies a
saved file into the live module (through the Compare-and-Merge window or silently, per the user's
setting) and changes nothing on the session side: you can keep editing and merge again.
`endOmlEditSession` closes the session, deletes the working copy and discards whatever was saved but
not merged. `omlReset` also closes the session, but leaves the last `-out.oml` on disk.

The agent never names input or output paths. It passes `eSpaceName`, `code` and `imports`, and gets
`mutatedOmlPath` back to hand to `omlMerge`.

## Lifecycle at a glance

| Call | Session | Files in `%TEMP%\ServiceStudio.MCPServer` | Response |
|---|---|---|---|
| `startOmlEditSession` | opens; fails if one is already open for the module | writes `<runId>-in.oml` (the live module, about 14 MB for a mid-size module); removes any read snapshot left by out-of-session reads | `{ "started": true }` |
| `applyModelApiCode` (clean) | unchanged | writes `<runId>-out.oml`, deletes the previous working-copy file; one file per session at any time | `mutatedOmlPath` = the new file |
| `applyModelApiCode` (exception, compile error, sandbox rejection, throw-probe) | unchanged | nothing written | `mutatedOmlPath: ""` or an MCP tool error |
| read tool inside the session | unchanged | nothing written; reads the working copy | the working copy's state |
| `omlMerge` | unchanged, whatever the user decides | the merged file stays | `{ "merged": bool, "message": string }` |
| `endOmlEditSession` | closes | deletes the working copy | `{ "ended": true, "discardedChanges": bool }` |
| `omlReset` | closes | **leaves** the last `-out.oml` (orphan until Service Studio restarts) | `{ "discardedChanges": bool, "eSpaceName": string }` |
| read tool outside a session | none | the first sidecar read writes one `<runId>-in.oml` snapshot of the live module; later reads reuse it until the next `startOmlEditSession` deletes it | the live module's state |

Service Studio wipes the folder when it starts (not while a second instance is running), and the
folder does not exist until the first read or session of the day creates it.

## Advance rules

The working copy only moves after `applyModelApiCode` runs successfully. "Success" means **the
host-allocated out file exists AND `exceptionMessage` is empty.** Code that does not compile never
runs, so it returns an MCP tool error instead of a result and cannot move the working copy. Read
tools never move it.

| Outcome of `applyModelApiCode` | `exceptionMessage` | `mutatedOmlPath` | Working copy | Next call reads from |
|---|---|---|---|---|
| Clean run | empty | path | **moves** to the new out file | the new out file |
| Runtime exception failure | populated (or surfaced as an MCP tool error) | `""` | unchanged | the last successful out file, or the `-in.oml` checkout if none |
| Compile error failure | *no result, an MCP tool error* | - | unchanged | same as above |
| Silent no-op (lambda never references `eSpace`) | empty | path | moves, to an **unmodified** OML | the silent-no-op file |
| Saved-but-invalid (`validationMessages` has a `type: "Error"` entry) | empty | path | moves, to an **invalid** OML | the invalid OML |

A failed call never corrupts the session: the next attempt picks up from the last good state. The
silent no-op and saved-but-invalid cases are where the working copy moves to a save that does not
reflect a valid intended change. Defend by always mutating `eSpace` and by scanning
`validationMessages` for a `type: "Error"` entry (see [`lambda-contract.md`](lambda-contract.md)).

## `startOmlEditSession`

Required before the first `applyModelApiCode` on a module, throw-probes included.

```jsonc
// agent -> MCP server (call tool startOmlEditSession)
{ "input": { "eSpaceName": "MyModule" }, "sessionToken": "<token>" }
// -> { "started": true }
```

Errors (text as returned):

- `An OML edit session is already started for this module. Call endOmlEditSession before starting a new one.`
- `applyModelApiCode` without a session: `No OML edit session is started. Call startOmlEditSession before making edits.`

Sessions are per module (`eSpaceName`): every error message says "for this module", and nothing
observed suggests one module's session affects another's. Cross-module behaviour was not exercised
on 2026-10-30.

## Reads inside and outside a session

- **Inside** a session, every read (`getDataModel`, `getStructures`, `getScreen`, `runQuery`,
  `getValidationMessages`, the name lists) answers from the working copy. `getStructures` listed a
  structure created one call earlier and not yet merged. This is what makes the verify-by-read-back
  step work, and it is also why a read cannot tell you what the live module holds.
- **Outside** a session, reads answer from the live module. The first sidecar read writes one
  `-in.oml` snapshot and later reads reuse it (four reads shared one file); the snapshot is dropped
  by the next `startOmlEditSession`. If the user edits in Service Studio meanwhile, a read may
  answer from that snapshot; open a session (which re-reads the live module) when it matters.
- To confirm what Service Studio actually kept after a merge, read **after** `endOmlEditSession`.

## `omlMerge`

Host-only tool that merges a saved OML into the module named in `eSpaceName`. Whether that opens
the Compare-and-Merge window or is accepted with no dialog is the user's own setting; there is no
argument for it.

```jsonc
// agent -> MCP server (call tool omlMerge)
{ "input": { "eSpaceName": "MyModule", "mutatedOmlPath": "C:\\...\\<runId>-out.oml" }, "sessionToken": "<token>" }
// -> { "merged": true, "message": "..." }
// cancelled in the window -> { "merged": false, "message": "The merge was cancelled in the Compare and Merge window." }
```

- Pass the `mutatedOmlPath` from the **latest** successful `applyModelApiCode`. Earlier files no
  longer exist (each save deletes the previous one).
- `omlMerge` **does not end the session and does not touch the working copy.** After `merged: true`
  the next `applyModelApiCode` continues on the same working copy; merge again whenever you want a
  checkpoint in the module. The merged file stays on disk, and the same path can be passed again
  (a cancelled merge was retried with the same path and accepted on 2026-10-30).
- `merged: false` with the cancel message leaves everything in place, also when the client aborted
  the call before the user cancelled (verified 2026-11-03): fix, save, merge again. For
  `merged: false` with "The merge failed. See the log for details." see [`../SKILL.md`](../SKILL.md) § 3.1.
- Under manual review the call blocks until the user decides, which may take minutes. Claude Code
  aborts a call silent for 300 s; the window stays open, the user's accept still lands, and only the
  response is lost. Announce the window to the user before calling, raise the client timeout
  ([`../SKILL.md`](../SKILL.md) § 9), and after an aborted call ask the user what they chose instead
  of re-merging.
- The window may list one line you did not ask for: a `(System)` entity reference flagged "Reference
  has changed" (`Espace` or `Role` under `Dependencies > (System) > Entities > Database`), at every
  merge until that change has been merged once. It is a side effect of the sidecar's save; tell the
  user to accept it with the rest. The window labels the live module "Your changes" and your OML
  "The other version".

Errors documented for the earlier server and not re-verified on 11.55.94: `MODULE_NOT_LOADED`
(`eSpaceName` not open), `INVALID_ARGUMENT` (`mutatedOmlPath` empty, not a file this server produced,
no longer on disk, or superseded by a later save).

## `endOmlEditSession`

The closing step of every mutating task, after the last `omlMerge`.

```jsonc
// agent -> MCP server (call tool endOmlEditSession)
{ "input": { "eSpaceName": "MyModule" }, "sessionToken": "<token>" }
// -> { "ended": true, "discardedChanges": false }   // session had no saves
// -> { "ended": true, "discardedChanges": true }    // session had saves, merged or not
```

- Deletes the working copy and discards anything saved but not merged. Merged changes are already
  in the module and stay there.
- **`discardedChanges: true` means "the working copy had saves"**, including saves you merged. It
  came back `true` after a merge with nothing pending (2026-10-20) and after one unmerged save
  (2026-10-30); `false` after a session with no saves. Do not report it to the user as lost work
  unless you know a save was not merged.
- Without a session: `No OML edit session is started. Call startOmlEditSession first.`

## `omlReset`

Kept from the earlier server. Today it does two things: it **ends the edit session** (the next
`applyModelApiCode` fails with "No OML edit session is started") and it **leaves the last `-out.oml`
on disk**, where `endOmlEditSession` would delete it. Without a session it returns
`{ "discardedChanges": false, "eSpaceName": "MyModule" }` and does nothing.

```jsonc
// agent -> MCP server (call tool omlReset)
{ "input": { "eSpaceName": "MyModule" }, "sessionToken": "<token>" }
// -> { "discardedChanges": true, "eSpaceName": "MyModule" }
```

Use `endOmlEditSession` to discard work. Reach for `omlReset` only when the user wants the discarded
file kept around: the tool description says the file can be passed to `omlMerge` to bring the work
back, which was not exercised on 2026-10-30. The file survives until Service Studio restarts.

Neither tool is error recovery. A failed `applyModelApiCode` leaves the working copy where it was,
so the next attempt resumes from the last good state.

## Concurrency

Call host tools **one at a time** against a server. With seven calls in flight (reads, `omlReset`
and an apply) one sidecar read failed with `[-32000] OML not found: ...\<runId>-in.oml`: a tool that
re-snapshots had removed the file the read was about to open. The same read alone, and two reads in
parallel, succeeded. Never run `omlReset`, `startOmlEditSession` or `endOmlEditSession` while another
call to the same server is pending.

## Choosing the closing call

| User intent | Call |
|---|---|
| "Put my changes into the module" | `omlMerge` with the latest `mutatedOmlPath`, then `endOmlEditSession` |
| "Checkpoint now, keep going" | `omlMerge`; the session stays open |
| "Discard everything not merged" | `endOmlEditSession` |
| "Discard, but keep the file in case" | `omlReset` (file stays until Service Studio restarts) |
| "That merge window, I cancelled it by mistake" | `omlMerge` again with the same path |
| Failed `applyModelApiCode` you want to retry | nothing; the working copy is unchanged |

## Summary

- `startOmlEditSession` before the first write; `endOmlEditSession` when the task is done. Both take
  `input: { eSpaceName }` plus the token.
- The working copy moves only on a clean save; `mutatedOmlPath` is its path and the argument for
  `omlMerge`. Each save deletes the previous file, so only the latest path is valid.
- Reads inside a session show the working copy; outside, the live module.
- `omlMerge` syncs into the module and leaves the session alone; merge as often as needed; the
  file stays on disk; a cancelled merge can be retried with the same path.
- `endOmlEditSession` deletes the working copy; `discardedChanges: true` only means it had saves.
- `omlReset` ends the session too, but keeps the file; it is not error recovery.
- One host call at a time; raise the client timeout before a manual-review merge.
