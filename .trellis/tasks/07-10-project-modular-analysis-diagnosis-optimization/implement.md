# Thirty-Five Workspace Diagnostic Execution Plan

1. Parse the 35 fixed workspace names from the approved workflow and initialize
   the persistent run ledger.
2. Process modules in registered order using batches of at most two.
3. Before each batch, create the fixed workspace skeletons and record the Git
   status path baseline.
4. Dispatch one fresh Diagnostic Subagent per workspace with `fork_context=false`.
5. Prohibit nested agents and give each agent a disjoint workspace plus a unique
   CCG title prefix.
6. Wait for both agents in the batch. Do not duplicate their diagnostic work.
7. Lightly verify required files, non-placeholder content, issue status, CCG
   summary, zero nested agents, and write-scope compliance.
   CCG prompts must prohibit restore/build/test or any command that writes
   generated, ignored, cache, lock, or test-output files.
8. When correction is needed, send it to the same workspace agent and recheck.
9. Close completed agents, update the ledger, then start the next batch.
10. After all 35 modules, run a full coverage and status audit. Do not implement
    any optimization.

## Required Workspace Files

```text
issue.md
review-log.md
evidence/scope-manifest.md
evidence/security-analysis.md
evidence/performance-analysis.md
evidence/extraction-analysis.md
evidence/runtime-validation-plan.md
```

## Valid Issue States

```text
APPROVED
APPROVED_DEGRADED
NO_ACTION_REQUIRED
DEGRADED_REVIEW_PENDING
RUNTIME_VALIDATION_PENDING
HUMAN_DECISION_REQUIRED
INVALID_WRITE_SCOPE
INVALID_AGENT_TOPOLOGY
```
