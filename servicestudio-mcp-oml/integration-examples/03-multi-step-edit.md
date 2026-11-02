# 03 - Multi-step edit (several saves in one edit session)

**Transport**: MCP (Service Studio in-process MCP Server).
**Scenario**: User asks for two related mutations - add a `Customer` entity, *then* add an `Email` attribute to it. The key teaching point: **inside one edit session, successive `applyModelApiCode` calls build on each other**. Each successful call writes a new out file that becomes the session's working copy, and the next call reads that working copy - invisibly to the agent, which never threads a path.

See [`../reference/mcp-session-pointer.md`](../reference/mcp-session-pointer.md) for the session mechanics (verified live 2026-10-30). All `code` payloads below are **full `eSpace => { ... }` lambdas** that end in `}` and do **not** include `eSpace.Save(...)` - see [`../reference/lambda-contract.md`](../reference/lambda-contract.md).

---

## User message

> Add a `Customer` entity to the module I have open in Service Studio. Then add an `Email` text attribute (length 200, mandatory) to that `Customer` entity.

## Default - two saves in one session (no path threading)

Open the session once, then two separate `applyModelApiCode` calls. (Every call carries the `sessionToken` obtained once from `createSessionToken` - see [`01-add-entity-via-mcp.md`](01-add-entity-via-mcp.md) Call 0.)

### Call 0 - `startOmlEditSession`

Required once per module before the first write. It checks out the live module as the session's working copy (`%TEMP%\ServiceStudio.MCPServer\<run0>-in.oml`).

```jsonc
{ "input": { "eSpaceName": "MyModule" }, "sessionToken": "<token>" }
// -> { "started": true }
```

A second start on the same module fails with `An OML edit session is already started for this module. Call endOmlEditSession before starting a new one.`

### Call 1 - create the `Customer` entity

**Request** (call tool `applyModelApiCode` - full lambda, no paths, no `Save`):

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

**Response** (structured content - result object directly):

```jsonc
{
  "exceptionMessage":   "",
  "stdoutOutput":       "",
  "stderrOutput":       "",
  "validationMessages": [],
  "mutatedOmlPath":     "C:\\Users\\…\\<run1>-out.oml"
}
```

Empty `exceptionMessage` + the out file was written ⇒ `<run1>-out.oml` is now the working copy (the `<run0>-in.oml` checkout is deleted) and its path came back as `mutatedOmlPath`.

### Call 2 - add the `Email` attribute

The critical detail under MCP: **the agent does not thread paths between calls**. The working copy already holds Call 1's output, so Call 2 only needs to send `eSpaceName`, `code` and `imports` inside `input`:

```jsonc
{
  "input": {
    "eSpaceName": "MyModule",
    "code":    "eSpace => { var customer = eSpace.Entities.OfType<OutSystems.Model.Data.IServerEntity>().Named(\"Customer\"); var emailKey = customer.Key.CreateNewKeyBasedOnThis(new object[] { \"Email\" }); var email = customer.CreateAttribute(\"Email\", emailKey); email.DataType = eSpace.TextType; email.Length = 200; email.IsMandatory = true; }",
    "imports": ["OutSystems.Model.Data"]
  },
  "sessionToken": "<token>"
}
```

Note: `imports: ["OutSystems.Model.Data"]` because we're using `OutSystems.Model.Data.IServerEntity` for the cast - that namespace is not in the default imports (3 host + 7 sidecar). `imports` accepts only `OutSystems.Model` and its sub-namespaces.

**Response**:

```jsonc
{
  "exceptionMessage":   "",
  "stdoutOutput":       "",
  "stderrOutput":       "",
  "validationMessages": [],
  "mutatedOmlPath":     "C:\\Users\\…\\<run2>-out.oml"
}
```

The working copy moves again: `<run1>-out.oml` is gone and `<run2>-out.oml` is the only file the session owns.

### Verify

`getDataModel` with the required `includeJson` (always `"Never"`) - inside the session the read answers from the working copy:

```jsonc
{ "input": { "eSpaceName": "MyModule", "includeJson": "Never" }, "sessionToken": "<token>" }
```

Excerpt:

```csharp
/*** creating IServerEntity 'Customer' ***/
var customer = testBasicTypesWithou.CreateServerEntity("Customer");
/* ... */
var email = customer.CreateAttribute("Email");
email.DataType = testBasicTypesWithou.TextType;
email.IsMandatory = true;
email.Length = 200;
```

Both mutations are in the working copy. The live module still has neither until the merge.

### Finalise - merge, then end the session

After the **last** successful `applyModelApiCode` call (here, Call 2), take its `mutatedOmlPath` and call `omlMerge` with it (see [`../SKILL.md`](../SKILL.md) § 3.1). Only the *latest* `mutatedOmlPath` exists; the intermediate file was deleted by Call 2.

```jsonc
// call tool omlMerge
{ "input": { "eSpaceName": "MyModule", "mutatedOmlPath": "C:\\Users\\…\\<run2>-out.oml" }, "sessionToken": "<token>" }
// -> { "merged": true, "message": "..." }
```

Service Studio merges that OML into the open module - through its Compare-and-Merge window for the user to review and accept (or cancel), or with no dialog, per their setting. The session is still open afterwards: more saves and another merge are possible. When the task is done, close it:

```jsonc
// call tool endOmlEditSession
{ "input": { "eSpaceName": "MyModule" }, "sessionToken": "<token>" }
// -> { "ended": true, "discardedChanges": true }
```

`discardedChanges: true` here only says the working copy had saves (the ones just merged); nothing was lost. It would be `false` after a session with no saves.

---

## Composition - both mutations in one lambda

You can also fold both mutations into a single `applyModelApiCode` call - one lambda whose body builds both elements:

```jsonc
{
  "input": {
    "eSpaceName": "MyModule",
    "code":    "eSpace => { var key = eSpace.Key.CreateNewKeyBasedOnThis(new object[] { \"Customer\" }); var customer = eSpace.CreateServerEntity(\"Customer\", key); var emailKey = key.CreateNewKeyBasedOnThis(new object[] { \"Email\" }); var email = customer.CreateAttribute(\"Email\", emailKey); email.DataType = eSpace.TextType; email.Length = 200; email.IsMandatory = true; }",
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
  "mutatedOmlPath":     "C:\\Users\\…\\<runId>-out.oml"
}
```

Reasons to prefer composition:

- The two mutations share local state (here, the `customer` variable from creation flows directly into the attribute creation - no `Named("Customer")` lookup needed, no `OutSystems.Model.Data` import).
- You want exactly one row in the audit log instead of two.
- You're scripting outside an interactive session and don't need intermediate inspection.

Under MCP, composition is mostly stylistic - a failed call never moves the working copy (no out file written), so atomicity is rarely an issue across two successive calls.

---

## Mid-task discard

Sometimes you've made one successful save but want to discard it and start over from the live module. End the session and open a new one:

```jsonc
// Call 1: successful applyModelApiCode - working copy moves
{ "input": { "eSpaceName": "MyModule", "code": "eSpace => { /* first mutation */ }", "imports": [] }, "sessionToken": "<token>" }
// -> { "exceptionMessage": "", ..., "mutatedOmlPath": "C:\\…\\<run1>-out.oml" }

// Call 2: endOmlEditSession - discards the unmerged save and deletes the working copy
{ "input": { "eSpaceName": "MyModule" }, "sessionToken": "<token>" }
// -> { "ended": true, "discardedChanges": true }

// Call 3: startOmlEditSession - a fresh working copy of the live module
{ "input": { "eSpaceName": "MyModule" }, "sessionToken": "<token>" }
// -> { "started": true }

// Call 4: next applyModelApiCode runs against the live state
{ "input": { "eSpaceName": "MyModule", "code": "eSpace => { /* fresh mutation against the live state */ }", "imports": [] }, "sessionToken": "<token>" }
// -> { "exceptionMessage": "", ..., "mutatedOmlPath": "C:\\…\\<run4>-out.oml" }
```

`omlReset` ends the session the same way but leaves `<run1>-out.oml` on disk; see [`04-oml-reset.md`](04-oml-reset.md) for when that matters.

**`omlRefreshReferences` acts on the live module.** Under the earlier server it dropped the in-flight chain; its effect on an open edit session has not been re-verified. Refresh references *before* Call 0, or after `endOmlEditSession`.

---

## What just happened

One session, two `applyModelApiCode` calls, one merge, one close:

0. Call 0 checked out the live module as the working copy.
1. Call 1 finalised the agent's lambda (stripped trailing `}`, injected `Save`, re-closed), mutated the working copy, wrote `<run1>-out.oml` and made it the working copy.
2. Call 2 read that working copy (transparent), mutated it, wrote `<run2>-out.oml` and made it the working copy.
3. `omlMerge` put `<run2>-out.oml` in front of the user; `endOmlEditSession` deleted the working copy.

The agent passed only `eSpaceName`, a full `code` lambda and `imports` (inside `input`, alongside its `sessionToken`) on every write. No `omlPath`. No `outPath`. No intermediate-file bookkeeping. No `eSpace.Save(...)` in the lambda.

## Things to remember

- **One `startOmlEditSession` per module, before the first write.** Then pass `input: { eSpaceName, code, imports }` (a full `eSpace => { ... }` lambda; `imports` may be `[]`) plus `sessionToken` on every `applyModelApiCode`; you never thread paths between calls.
- **A failed call doesn't break the session.** Populated `exceptionMessage` (or an MCP tool error) ⇒ no out file written ⇒ working copy unchanged ⇒ the next call still reads the last-good state. (Watch out for the silent-no-op trap, where the response is clean and the out file is written but the OML wasn't actually mutated - see [`02-recovery-from-compile-error.md`](02-recovery-from-compile-error.md).)
- **Only the latest `mutatedOmlPath` exists.** Each save deletes the previous file, so merge with the path from the last successful apply.
- **Composition is for ergonomic locality**, not atomicity - atomicity comes from the working copy.
- **Use `OfType<IServerEntity>()` before `.Named()`** when filtering by entity kind, and pass `OutSystems.Model.Data` in `imports` for that cast.
- **To discard unmerged work**, call `endOmlEditSession` and start again - see [`04-oml-reset.md`](04-oml-reset.md).
- **Finalising.** `omlMerge` with the last `mutatedOmlPath` (check `merged`), then `endOmlEditSession`. The merge leaves the session open, so a long task may merge checkpoints along the way. See [`../SKILL.md`](../SKILL.md) § 3.1.
