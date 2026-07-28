# Requirements

## Objective

Merge the complete history of `1.0.0.2.IsolateConnector.Worktree` into the local target branch `1.0.0.2.IsolateConnector`.

## Constraints

- Preserve both branches' existing commits; do not rewrite history.
- Do not push to `origin` unless separately requested.
- Do not merge while either involved worktree has unrelated local changes.
- Treat the integration as high risk because the source contains OAuth, secret-handling, Dynamics access, capacity, and cross-module changes.
- Run the available automated verification before the merge and again on the merged result.
- Use the project CCG self-healing dual-model entrypoint for pre-merge analysis and final review.
- Do not ignore Critical review findings.

## Acceptance Criteria

- `1.0.0.2.IsolateConnector` contains the source branch tip and its seven pre-existing feature commits.
- The merge completes without unresolved conflicts.
- Required tests and repository verification commands pass on the merged target.
- The resulting target worktree is clean after task archival is committed.
- No remote push is performed.
