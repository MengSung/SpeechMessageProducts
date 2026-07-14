# F01C Runtime Validation Plan

Status: NO_RUNTIME_PENDING
Mode: DIAGNOSIS_ONLY

No retained F01C issue requires runtime execution to establish its current
diagnosis:

- F01C-SEC-001 is confirmed by executable persistent-environment writes,
  hard-coded profile resolution, and permission-bypassing argv.
- F01C-PERF-001 is confirmed by conflicting executable instructions in tracked
  documents and packaged tutorial copies.
- F01C-PERF-002 is confirmed by Git object inventory and artifact contents.
- F01C-EXT-001 is confirmed by exact duplicated helper lines, import-time
  execution, and a literal workstation drive path.

## Implementation-Time Acceptance Measurements

These checks belong to a separately approved optimization task. They are not
permission to execute commands in this diagnosis.

| Issue | Future measurement | Success threshold | Verdict effect |
|---|---|---|---|
| F01C-SEC-001 | Snapshot User/process PATH and fake-backend argv before/after the runner | User PATH unchanged; process state restored; no permission-bypass flag; portable role/tool resolution | validates implementation, not current KEEP |
| F01C-PERF-001 | Documentation lint plus fixture review invocation | no unapproved direct-wrapper/deprecated commands; one canonical fallback contract | validates remediation |
| F01C-PERF-002 | Git object size, clone transfer, search/index timing before and after migration | size budget passes; archived evidence remains hash-addressable; timings do not regress | quantifies benefit |
| F01C-EXT-001 | Import-side-effect fixture, temporary-directory CLI run, DOCX render comparison | import writes nothing; no fixed drive; expected styles/order and provenance manifest | validates extraction |

## Safety Restrictions

- Do not use real credentials or production reviewer prompts in fixtures.
- Use fake backends and temporary directories for path/argv checks.
- History rewrite requires an explicit F01A-owned plan and backup.
- DOCX visual acceptance must use disposable outputs outside tracked paths.
- No restore, build, test, generation, formatting, migration, or installer was
  run during this diagnosis.

## Runtime-Pending Count

Zero. No issue should receive `NEEDS_RUNTIME_VALIDATION` solely because its
future implementation has acceptance tests.
