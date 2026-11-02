# 04 - Discarding work: `endOmlEditSession` and `omlReset` walkthrough

**Transport**: MCP (Service Studio in-process MCP Server).
**Scenario**: The agent has built two saves inside an edit session. The user changes direction mid-task and wants the agent to start over from the live module instead of layering more saves onto the working copy. The agent ends the session, which discards the unmerged saves, and opens a new one. Part 2 shows `omlReset` fired *by mistake* and what is still recoverable.

For routine multi-step edits (where you just want each save to build on the last), do nothing special - the session carries successive saves forward (see [`03-multi-step-edit.md`](03-multi-step-edit.md)). To **accept** work into the live module rather than drop it, call `omlMerge` with the `mutatedOmlPath` the apply response returns. For the full session mechanics, verified live on 2026-10-30, see [`../reference/mcp-session-pointer.md`](../reference/mcp-session-pointer.md). All `code` payloads below are **full `eSpace => { ... }` lambdas** that end in `}` and do **not** include `eSpace.Save(...)` - see [`../reference/lambda-contract.md`](../reference/lambda-contract.md).

---

## User messages

> Add a `Customer` entity to the module I have open in Service Studio.

…then…

> Also add an `Email` attribute to `Customer`.

…and after the second save lands…

> Actually, scrap that. I want to start fresh from the live module - no `Customer`, no `Email`.

## Turn

### Step 0 - open the session

```jsonc
{ "input": { "eSpaceName": "MyModule" }, "sessionToken": "<token>" }   // startOmlEditSession
// -> { "started": true }
```

### Step 1 - first save lands

**Request**:

```jsonc
{
  "input": {
    "eSpaceName": "MyModule",
    "code":    "eSpace => { var key = eSpace.Key.CreateNewKeyBasedOnThis(new object[] { \"Customer\" }); eSpace.CreateServerEntity(\"Customer\", key); }",
    "imports": []
  },
  "sessionToken": "<token>"
}
```

**Response**:

```jsonc
{
  "exceptionMessage":   "",
  "stdoutOutput":       "",
  "stderrOutput":       "",
  "validationMessages": [],
  "mutatedOmlPath":     "C:\\…\\<run1>-out.oml"
}
```

Empty `exceptionMessage` + out file written ⇒ `<run1>-out.oml` is the working copy.

### Step 2 - second save lands

```jsonc
{
  "input": {
    "eSpaceName": "MyModule",
    "code":    "eSpace => { var customer = eSpace.Entities.OfType<OutSystems.Model.Data.IServerEntity>().Named(\"Customer\"); var emailKey = customer.Key.CreateNewKeyBasedOnThis(new object[] { \"Email\" }); var email = customer.CreateAttribute(\"Email\", emailKey); email.DataType = eSpace.TextType; email.Length = 200; }",
    "imports": ["OutSystems.Model.Data"]
  },
  "sessionToken": "<token>"
}
```

**Response**:

```jsonc
{
  "exceptionMessage":   "",
  "stdoutOutput":       "",
  "stderrOutput":       "",
  "validationMessages": [],
  "mutatedOmlPath":     "C:\\…\\<run2>-out.oml"
}
```

The working copy is now `<run2>-out.oml`; `<run1>-out.oml` was deleted.

### Step 3 - the user changes direction

The user wants to drop everything and start from the live module. The agent ends the session:

**Request** (call tool `endOmlEditSession`):

```jsonc
{ "input": { "eSpaceName": "MyModule" }, "sessionToken": "<token>" }
```

**Response**:

```jsonc
{ "ended": true, "discardedChanges": true }
```

The working copy with `Customer` + `Email` is deleted and nothing reached the live module. `discardedChanges: true` says the session had saves; it says the same after a session whose saves were all merged, so on its own it is not a "work lost" signal.

### Step 4 - a fresh session shows the live module

```jsonc
{ "input": { "eSpaceName": "MyModule" }, "sessionToken": "<token>" }   // startOmlEditSession
// -> { "started": true }
```

A verifying read (`getDataModel`, `includeJson` always `"Never"`) now answers from a fresh working copy of the live module:

```csharp
(OutSystems.Model.IReference _System_) => {
    /*** creating IStaticEntity 'MenuItem' ***/
    var menuItem = testBasicTypesWithou.CreateStaticEntity("MenuItem");
    /* ... no Customer, no Email ... */
}
```

From here, any subsequent `applyModelApiCode` mutates the new working copy.

---

## Part 2 - `omlReset` by mistake

> Wait, no - I didn't mean to throw away the Customer work.

Suppose the agent had called `omlReset` in Step 3 instead of `endOmlEditSession`:

```jsonc
{ "input": { "eSpaceName": "MyModule" }, "sessionToken": "<token>" }   // omlReset
// -> { "discardedChanges": true, "eSpaceName": "MyModule" }
```

`omlReset` ends the session exactly like `endOmlEditSession` (the next `applyModelApiCode` fails with `No OML edit session is started. Call startOmlEditSession before making edits.`), with one difference: **`<run2>-out.oml` stays on disk** until Service Studio restarts (verified 2026-10-30). The tool description says that file can be passed to `omlMerge` to bring the work back:

```jsonc
{ "input": { "eSpaceName": "MyModule", "mutatedOmlPath": "C:\\…\\<run2>-out.oml" }, "sessionToken": "<token>" }   // omlMerge
```

That recovery path was **not exercised** in the 2026-10-30 verification, so offer it as a best effort: tell the user the file survives only until Service Studio restarts, try the merge, and if it fails, re-apply the lambdas in a new session. After `endOmlEditSession` there is nothing to recover; the saves have to be redone.

---

## What just happened

1. `startOmlEditSession` - working copy checked out.
2. `applyModelApiCode` - added `Customer`; the save became the working copy.
3. `applyModelApiCode` - added `Email`; the save replaced the working copy.
4. `endOmlEditSession` - working copy deleted, unmerged saves gone, nothing in the live module.
5. `startOmlEditSession` + a read - a fresh working copy that shows the live state.

There is **no `revertChanges` verb**. `endOmlEditSession` covers the drop-the-work case; a failed `applyModelApiCode` needs nothing, because a failed call writes no out file and leaves the working copy where it was.

## When NOT to end or reset

Don't end the session as a defensive measure between steps of one task, and don't reach for `omlReset` or `endOmlEditSession` to recover from a failed `applyModelApiCode`: a failed call never moves the working copy, so there is nothing to clean up. End the session when:

- The user **explicitly** wants to drop the unmerged work.
- The agent wants to throw away its saves and restart against the live module.
- The task is done and the last `omlMerge` has returned (the normal closing step).

When the user wants to **accept** the work, that is `omlMerge`, not a reset.

## Things to remember

- **`endOmlEditSession` takes `input: { eSpaceName }`** (plus `sessionToken`) and returns `{ ended, discardedChanges }`. It fails with `No OML edit session is started. Call startOmlEditSession first.` when there is no session.
- **`discardedChanges: true` means the working copy had saves**, merged or not. Only you know whether the last save was merged.
- **`endOmlEditSession` deletes the working copy; `omlReset` leaves it.** Both end the session. Prefer `endOmlEditSession`; use `omlReset` only when the user wants the file kept as a safety net until Service Studio restarts.
- **A merge does not end the session.** After `omlMerge` you can keep saving and merge again; end the session yourself when the task is done.
- **Sessions are per module.** Ending one module's session does not touch another's (not exercised on 2026-10-30, but every error message speaks of "this module").
- **The next write after ending needs a new `startOmlEditSession`.** Reads work without one and show the live module.
