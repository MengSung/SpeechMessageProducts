# CCG generated-artifact policy

`dual-model-runs/` is a local operational store. It can contain prompts, model
output, diagnostics, local paths, and toolchain metadata, so it is never a
durable Git source. The repository retains only compact, redacted task/review
records that state a run's result and evidence location without copying raw
payloads.

Before committing governance work, run:

```powershell
python .trellis/scripts/governance_artifacts.py --check-index
```

The check reports only artifact-class counts and never prints indexed paths or
artifact contents. Use `git rm -r --cached .ccg/dual-model-runs .serena/cache`
to remove accidental index enrollment while preserving local files.
