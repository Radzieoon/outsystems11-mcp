# MCP host design choices & caveats

## 1. OMLs being worked on live in a temp folder

```
%TEMP%\ServiceStudio.MCPServer\<runId>-in.oml     working copy checked out by startOmlEditSession (or a read snapshot outside a session)
%TEMP%\ServiceStudio.MCPServer\<runId>-out.oml    the working copy after a successful write (the previous file is deleted)
```

An edit session owns one file at a time. A clean `applyModelApiCode` returns the new `-out.oml` path
as `mutatedOmlPath`. That is the file to open by hand, or pass to `omlMerge`, if you need to recover
a change. `endOmlEditSession` deletes it (verified 2026-10-30).

`omlReset` ends the edit session too but does **not** delete the oml file - a discarded working
copy stays on disk. A reset with no session open does nothing at all.


## 2. Starting Service Studio wipes %TEMP%\ServiceStudio.MCPServer

Recover anything you need **before** restarting Service Studio. Skipped while two or more Service
Studio instances are running, so leftovers accumulate instead.


## 3. Your mid-chain edits are invisible to the agent

`startOmlEditSession` takes a copy of your module and the agent edits that;
your real module is untouched until a merge. Everything the agent reads while the session
is open comes off its copy, so anything you change in the UI meanwhile is invisible to it.

Two things end the session, so the next read or `startOmlEditSession` takes a fresh copy
off your live module:

- `endOmlEditSession` - discards unmerged edits and deletes the copy.
- `omlReset` - the same, but leaves the copy's file on disk.

`omlMerge` does **not** end the session (verified 2026-10-30): it copies the agent's
file into your module, through the Compare-and-Merge window or silently per your
setting, and the agent keeps working on its copy afterwards. A merge you cancel changes
nothing on either side, and the agent can offer the same file again. The agent is asked
to end the session when its task is done.


## 4. Each open module is tracked separately

Chains on different modules do not interfere. A module can sit on an unmerged copy
indefinitely — merge or reset it explicitly.


## 5. Every connection needs approval

You get an "allow this agent?" prompt on every MCP handshake, including reconnects
of an agent you already approved. Per-connection, by design.


## 6. Loopback-only, but it runs arbitrary C#

Bound to `127.0.0.1` and gated by #5 — but once approved, `applyModelApiCode`
compiles and runs whatever C# the agent sends, with Service Studio's privileges.
Only approve agents you trust, pointed at modules you own.


## 7. Timeouts and port

Writes timeout at 300s, reads at 120s; the default server listens on `41820`,
moving forward if the port is busy by up to 20 retries.


## 8. The server starts on demand, not on login

Service Studio does not start the MCP server automatically — the user starts
it. A client that connects (or reconnects) before that gets `ConnectionRefused`
at the endpoint (e.g. `Failed to reconnect to servicestudio: ConnectionRefused
at http://127.0.0.1:41820`); that is not a broken install, just the server not
started yet.
