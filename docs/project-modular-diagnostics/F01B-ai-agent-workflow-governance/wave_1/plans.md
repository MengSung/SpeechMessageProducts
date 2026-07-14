# F01B Wave 1 Repair Contract

## Identity and scope

- Global wave: W1
- Local wave: `F01B-ai-agent-workflow-governance/wave_1`
- Exact issue subset: F01B-SEC-001, F01B-SEC-002, F01B-PERF-001,
  F01B-PERF-002, F01B-EXT-001
- Excluded issue IDs: none from F01B; F01D-EXT-001 and every non-F01B issue
  remain outside this local wave.

## Allowed repair paths

| Issue | Allowed paths | Repair boundary |
|---|---|---|
| SEC-001, PERF-001 | `.ccg/.gitignore`, `.ccg/ARTIFACTS.md`, `.ccg/dual-model-runs/**` (index removal only), `.serena/.gitignore`, `.serena/cache/**` (index removal only), `.trellis/scripts/governance_artifacts.py`, `.trellis/scripts/tests/test_governance_artifacts.py` | Keep raw run/cache files locally; retain only a redacted, path-minimized status record. |
| SEC-002, EXT-001 | `.trellis/scripts/common/active_task.py`, `.trellis/scripts/active_task_cli.py`, `.trellis/scripts/tests/test_active_task_contract.py`, `.opencode/lib/trellis-context.js`, `.opencode/plugins/inject-subagent-context.js`, `.opencode/tests/trellis-context.test.mjs` | Python owns resolution policy; OpenCode invokes its JSON CLI and does not infer session identity itself. The injector may use only an explicit dispatch hint after exact lookup. |
| PERF-002 | `.trellis/config.yaml`, `.trellis/scripts/common/config.py`, `.trellis/scripts/common/task_utils.py`, `.trellis/scripts/tests/test_task_hooks.py` | Normalize legacy/structured hook specifications and bound child process execution. |

## Explicitly excluded paths and work

- Root `.gitignore`, Git history rewriting, CI enrollment, and repository size
  enforcement (F01A).
- `docs/scripts/**` CCG writer changes, pre-write redaction, metadata
  minimization, and persistent PATH repair (F01C).
- `docs/project-modular-diagnostics/**/issue.md`, this wave's three frozen
  contract files, product/test-subject sources, and all deployment/data files.

## Repair sequence

1. Create executable fixture tests before each of the three repair groups and
   record their expected red failure.
2. Remove sole-session inference by default, add the explicit recovery flag,
   make OpenCode call the Python JSON CLI, and remove the injector's direct
   fallback; prove fixture parity and explicit dispatch-hint behavior.
3. Add hook timeout/process-group/policy behavior without logging command text;
   prove success, error, timeout, and child cleanup fixtures.
4. Add the durable-artifact policy and recurrence scanner, then untrack only
   raw CCG-run and Serena-cache paths while leaving the local working files in
   place.
5. Capture all result measurements, review the allowlisted diff, and commit
   only after review approval.

## Validation commands

```powershell
python -m unittest discover -s .trellis/scripts/tests -p "test_active_task_contract.py" -v
python -m unittest discover -s .trellis/scripts/tests -p "test_task_hooks.py" -v
python -m unittest discover -s .trellis/scripts/tests -p "test_governance_artifacts.py" -v
node --test .opencode/tests/trellis-context.test.mjs
python .trellis/scripts/governance_artifacts.py --check-index
git ls-files .ccg/dual-model-runs .serena/cache
git diff --check
```

## Rollback boundary

Revert this local-wave commit to restore the prior resolver, hook execution,
and index enrollment. F01A history changes and F01C writer changes remain
separate and are never rolled back with this wave.
