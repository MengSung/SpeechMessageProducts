# SpeechMessageProducts Repository Migration Design

## Goal

Create a new product-line repository named `SpeechMessageProducts` from the existing `ChurchReport` repository while preserving useful Git history and keeping the existing `ChurchReport` repository available as a rollback and legacy-product anchor.

The migration must protect the current work on `Jesus_5.1.8.FabelSecurityScan`, which is ahead of `origin/Jesus_5.1.8.FabelSecurityScan` by 13 commits, and avoid risky in-place folder renames while multiple Git worktrees exist.

## Current State

- Current working directory: `<system-platform-root>\ChurchReport`
- Current remote: `https://github.com/MengSung/ChurchReport.git`
- Current branch: `Jesus_5.1.8.FabelSecurityScan`
- Current branch is ahead of its remote by 13 commits.
- Root solution file: `ChurchReport.sln`
- Main product project: `ChurchReport\ChurchReport.csproj`
- Shared product-line projects already exist under `SpeechMessage.*`, including payments, ASP.NET Core integration, workflows, and tests.
- The repository has many historical customer/version branches and multiple linked worktrees, including worktrees registered under a different root path.

## Chosen Approach

Use a history-preserving split:

1. Keep the existing `ChurchReport` repository intact.
2. Create a full mirror backup of the current repository for cold storage.
3. Create a new working repository at `<system-platform-root>\SpeechMessageProducts`.
4. Point the new working repository to a new remote, expected to be `https://github.com/MengSung/SpeechMessageProducts.git`.
5. Start the new product line from the current branch state, promoted to a clean primary branch such as `main`.
6. Rename solution and product identity in controlled phases after the new repository exists and builds.

This avoids turning the existing `ChurchReport` checkout into the only source of truth for the new product line.

## Git History Policy

The new repository should preserve reachable commit history from the selected starting branch so old fixes, authorship, and `git blame` remain useful.

The new repository should not import every historical branch by default. The existing branch set contains many old customer/version branches and automation worktree branches that would make `SpeechMessageProducts` noisy from day one.

The recommended policy is:

- Preserve full history in a local or private mirror backup named similar to `ChurchReport-full-history.git`.
- Push the current selected branch history to `SpeechMessageProducts`.
- Optionally push only a short allowlist of important architecture branches later, such as payment, LINE, and RichMenu refactor branches.
- Do not push old customer/version branches into the new repository unless there is a clear product-line reason.

## Migration Phases

### Phase 1: Backup and New Repository

- Confirm or stash unrelated working tree changes before migration.
- Create a full mirror backup of `ChurchReport`.
- Create or clone the new local folder `<system-platform-root>\SpeechMessageProducts`.
- Set `origin` in the new folder to the new GitHub repository.
- Create the new primary branch from `Jesus_5.1.8.FabelSecurityScan`.
- Push the new primary branch to `SpeechMessageProducts`.

### Phase 2: Solution Rename

- Rename `ChurchReport.sln` to `SpeechMessageProducts.sln`.
- Update solution-level references only as needed for the solution file rename.
- Run restore and build against `SpeechMessageProducts.sln`.

This phase should not rename every `ChurchReport` namespace or project folder.

### Phase 3: Main Product Project Rename

Evaluate whether the main product project should become:

- `SpeechMessageProducts\SpeechMessageProducts.csproj`, or
- a more specific product host name if the repository will hold multiple products.

If approved, rename the project folder and project file through `git mv`, update the solution project path, then rebuild.

### Phase 4: Product Identity Cleanup

Rename deeper product identity only after the repository and solution rename are stable:

- namespaces
- assembly names
- default root namespace
- cookie names
- deployment output names
- CI/CD and publish scripts
- README and operator documentation

Cookie and authentication names need specific review because renaming them can affect deployed sessions and login behavior.

## Worktree and Folder Rename Risks

Do not directly rename `<system-platform-root>\ChurchReport` as the first operation.

The repository has many registered worktrees. Some are under `.worktrees`, and others are registered under a different historical root path. Directly renaming the active root can leave Git worktree metadata, IDE caches, and scripts pointing at stale absolute paths.

Creating a new `SpeechMessageProducts` folder while leaving `ChurchReport` in place gives a clean rollback path and prevents existing worktrees from becoming accidental blockers.

## Safe Commands

These command classes are safe during planning and preparation:

- `git status --short --branch`
- `git log --oneline -n 20`
- `git remote -v`
- `git branch -vv`
- `git worktree list --porcelain`
- `git clone --mirror <source> <backup-path>`
- `git clone <source> <new-working-path>`

These command classes are safe only after the new repository target is confirmed:

- `git remote set-url origin <new-repo-url>` inside the new folder
- `git branch -M main` inside the new folder
- `git push -u origin main`
- `git mv ChurchReport.sln SpeechMessageProducts.sln`

## Destructive or High-Risk Operations

Avoid these during the first migration pass:

- Renaming the original `ChurchReport` root folder in place.
- Deleting or pruning worktrees before their status is reviewed.
- Force-pushing all branches to the new repository.
- Bulk replacing every `ChurchReport` string with `SpeechMessageProducts`.
- Changing auth cookie names without a deployment/session migration decision.
- Removing old branches from the original repository.

## Validation

Each phase needs a narrow validation checkpoint:

- After backup: confirm mirror exists and has refs.
- After new repo creation: confirm `git remote -v`, branch name, and `git log` history.
- After first push: confirm the new GitHub repository shows the expected branch and commits.
- After solution rename: run `dotnet restore SpeechMessageProducts.sln` and `dotnet build SpeechMessageProducts.sln`.
- After deeper project rename: run the relevant test projects, especially product-host and shared `SpeechMessage.*` tests.

## Open Decisions Before Implementation

- Confirm the new GitHub repository URL.
- Confirm whether the new primary branch should be `main`.
- Confirm where to store the full mirror backup.
- Confirm the allowlist of old branches, if any, to push into the new repository later.
- Confirm whether Phase 3 should rename the main project folder now or defer until the new product architecture is clearer.

## Non-Goals

- Do not rewrite Git history.
- Do not remove the old `ChurchReport` repository.
- Do not migrate every old customer/version branch into `SpeechMessageProducts`.
- Do not rename all namespaces, cookies, deployment identifiers, or documentation in one pass.
- Do not clean unrelated pending worktree changes as part of this migration design.

