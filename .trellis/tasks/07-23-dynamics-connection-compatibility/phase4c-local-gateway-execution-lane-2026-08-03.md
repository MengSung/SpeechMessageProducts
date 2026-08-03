# Phase 4C Local Gateway execution lane (2026-08-03)

## Decision

Phase 4C does not require a Central Gateway, IIS, or a separate production
server. Visual Studio 2026 may host the real local compatibility lane:

```text
Visual Studio / ChurchReport
  -> Local Gateway (localhost)
  -> pinned CE 8.2 or CE 9.1 net48 official Worker
  -> CE Organization Service
```

This is real compatibility evidence only when the Worker is the Microsoft
official `CrmServiceClient` process and the profile points to the approved CE
target. A fake Worker remains limited to boundary, lifecycle, fault, and
resource tests; it cannot prove CE compatibility.

## Execution order

1. Publish the pinned CE 8.2 and/or CE 9.1 Worker into one clean, stable local
   host directory. Generate a fresh manifest and verify executable hashes.
2. Materialize one approved local profile/overlay beside that Local Gateway
   generation. The profile supplies only approved non-secret identity fields
   and secret references; credentials never enter product JSON, command-line
   arguments, logs, IPC, cookies, or browser state.
3. Start Local Gateway from its owning project/content root in Visual Studio
   or with the equivalent project-root command. Verify `/health` and `/ready`
   before allowing the operation lane.
4. Start ChurchReport with `ExecutionMode=Gateway`, `ProfileAlias` fixed by
   deployment configuration, and `Package01FeeReadsEnabled=false`.
5. Use the browser only for the website-to-localhost route. Execute the fixed
   approved identity, read, paging, metadata/action, recycle, and isolation
   matrix through `ChurchReport -> Local Gateway -> official Worker -> CE`.
6. Stop the generation in reverse ownership order. Assert processes, named
   pipes, handles, timers, cancellation registrations, queues, credentials,
   sessions, and memory return to their declared baselines.

## Current evidence and remaining input

- The first Visual Studio Local Gateway start was intentionally fail-closed:
  `OfficialWorkerProfileExecutor` rejected the Development placeholder
  executable identity/hash before listener publication. The admission registry
  disposed cleanly and no Gateway listener was published. This confirms the
  local lane is present; it does not justify replacing the zero hash with a
  guessed value.
- Local Gateway lifecycle, boundary, LocalDB capacity/fault, publish, hash,
  and no-leak tests are complete.
- The website login lane was verified locally without a CRM call.
- The remaining input is an approved CE 8.2/9.1 profile and stable local host
  paths. This does not require D365APP01 administration or an IIS deployment.
- Until that profile exists, `Package01FeeReadsEnabled` remains `false` and
  Phase 5/6 remain locked.
