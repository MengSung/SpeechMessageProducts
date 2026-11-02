# Execution plan

1. Capture `HEAD^..HEAD` file list and baseline status; inspect relevant backend specs.
2. Run the CCG self-healing reviewer entrypoint and preserve its summary/artifacts. Treat Gemini-only output as degraded fallback if Claude has no usable output.
3. Add focused `MoneyToChinese` regression coverage in the existing ChurchReport test project (or a minimal executable test location already used by the repository), then repair the mapping/algorithm with full Traditional Chinese XML documentation.
4. Inspect and test SessionValidationMiddleware, GlobalAuthorizationFilter, static filters, Kestrel limits, OAuth client usage, BaseChurchController cache cleanup, and diagnostic resource ownership. Correct only verified defects.
5. Measure/inspect additional acceleration candidates. Do not change `ColumnSet(true)` without a complete consumer field inventory; document it as follow-up if evidence is insufficient.
6. Run `dotnet build -c Release`, focused tests, applicable existing suites, `verify_trace_invariants.py`, and byte-level encoding/CRLF checks.
7. Re-run review after fixes if any Critical issue changed; write `.trellis/tasks/.../review.md` with Critical/Warning/Info findings and evidence.
8. Update relevant specs only when this task discovers a reusable executable contract; then commit and archive the Trellis task.

Validation commands:

```powershell
git diff --check HEAD^ HEAD
dotnet build -c Release
python ./.trellis/scripts/verify_trace_invariants.py
```

Rollback points: before the money conversion edit, before any additional optimization, and before commit.
