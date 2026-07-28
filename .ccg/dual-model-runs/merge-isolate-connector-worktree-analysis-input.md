# Pre-Merge Analysis Request

Analyze the proposed local merge of branch `1.0.0.2.IsolateConnector.Worktree` into branch `1.0.0.2.IsolateConnector`.

## Repository State

- Source worktree: `D:/音訊科技產品/系統平台/SpeechMessageProducts/.worktrees/1.0.0.2.IsolateConnector.Worktree`
- Source tip: `c9dafdafa34541ae57753bfcc8db4c7338853cff`
- Target worktree: `D:/音訊科技產品/系統平台/SpeechMessageProducts`
- Target tip: `82df2440e17708172ee4706c5f54d2932e569e7a`
- Merge base: `18ef7b85a9b5055621fe8f731436d4f59679f293`
- Both worktrees were clean before creating this merge-task metadata.
- Target has one unique task-archive commit; source has seven unique commits.
- Proposed source diff: 406 files changed, 40,831 insertions, 407 deletions.
- No remote push is requested.

## Source Commit Set

1. `72cbf0e7c` docs: define global safety guardrails
2. `58657c0f9` Dynamics 365 no-SDK Gateway Phase 0 planning and architecture
3. `f90ef06c3` ChurchReport Package 1 controlled queries and capacity protection
4. `41f7e1eaa` Dynamics 365 IFD/ADFS OAuth layered enablement and diagnostics
5. `9978261c2` ADFS authorization-code/refresh-token support and local diagnostics
6. `0385e9aeb` Dynamics 365 9.1 IFD token-failure diagnosis and report
7. `c9dafdafa` D365 password-security hardening and review records

## Sensitive Areas

- OAuth/ADFS token acquisition and refresh
- Secret resolution and password handling
- HTTP transports and Dynamics Web API access
- Organization capacity/admission controls
- ChurchReport integration and configuration
- New projects, tests, diagnostic scripts, generated review artifacts, and task records

## Required Output

Provide a pre-merge readiness analysis with these sections:

1. `Critical` — conditions that must block the merge.
2. `Warning` — risks that should be verified or mitigated before/after merge.
3. `Info` — observations and suggested verification commands.
4. `Merge Strategy` — safe local merge sequence, conflict hotspots, and rollback points.
5. `Test Matrix` — concrete build/test/static checks appropriate for this repository and change set.

Do not modify repository files. Verify claims against the actual branch diff and repository configuration. Distinguish committed generated evidence from product code, and do not treat provider credentials as available unless explicitly configured.
