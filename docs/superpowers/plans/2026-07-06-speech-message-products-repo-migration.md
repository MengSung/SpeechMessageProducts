# SpeechMessageProducts Repository Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a new `SpeechMessageProducts` repository from `ChurchReport`, preserve the selected branch history, keep `ChurchReport` intact, and rename the solution in the new repository only.

**Architecture:** The migration is split into guarded phases: preflight, full mirror backup, new working clone, new remote setup, selected branch push, solution rename, and validation. The original checkout remains a rollback anchor and is not renamed in place.

**Tech Stack:** Git, PowerShell, .NET SDK, Visual Studio solution files, GitHub remote repository.

---

## File Structure

- Create: `docs/superpowers/plans/2026-07-06-speech-message-products-repo-migration.md`
  - Owner of the execution checklist for the migration.
- Modify: `.ccg/tasks/rename-repo-to-speech-message-products/task.json`
  - Tracks the task phase and next action.
- Create during execution outside this repo: `<system-platform-root>\ChurchReport-full-history.git`
  - Bare mirror backup containing all refs.
- Create during execution outside this repo: `<system-platform-root>\SpeechMessageProducts`
  - New working repository for the product line.
- Rename during execution inside the new repo: `ChurchReport.sln` -> `SpeechMessageProducts.sln`
  - Solution identity rename after repository split is stable.

## Execution Defaults

- Source checkout: current working directory, expected to be `<system-platform-root>\ChurchReport`.
- New working folder: sibling folder `<system-platform-root>\SpeechMessageProducts`.
- Mirror backup folder: sibling folder `<system-platform-root>\ChurchReport-full-history.git`.
- New GitHub remote: `https://github.com/MengSung/SpeechMessageProducts.git`.
- Source branch: `Jesus_5.1.8.FabelSecurityScan`.
- New primary branch: `main`.
- Branch import policy: push `main` first; defer old branch allowlist until after the new repo is verified.

---

### Task 1: Preflight Current Repository

**Files:**
- Read: `.git`
- Read: `ChurchReport.sln`
- Read: `.ccg/tasks/rename-repo-to-speech-message-products/task.json`
- Modify: none

- [ ] **Step 1: Verify source path and branch**

Run from the current `ChurchReport` checkout:

```powershell
$ErrorActionPreference = 'Stop'
$sourceRoot = (Get-Location).Path
$systemRoot = Split-Path -Parent $sourceRoot
$sourceBranch = git branch --show-current
$sourceHead = git rev-parse HEAD
Write-Host "sourceRoot=$sourceRoot"
Write-Host "systemRoot=$systemRoot"
Write-Host "sourceBranch=$sourceBranch"
Write-Host "sourceHead=$sourceHead"
if ((Split-Path -Leaf $sourceRoot) -ne 'ChurchReport') { throw "Expected to run from ChurchReport, got $sourceRoot" }
if ($sourceBranch -ne 'Jesus_5.1.8.FabelSecurityScan') { throw "Expected branch Jesus_5.1.8.FabelSecurityScan, got $sourceBranch" }
```

Expected: command prints the source path, system root, branch, and HEAD commit without throwing.

- [ ] **Step 2: Verify current Git status is understood**

```powershell
git status --short --branch
git log --oneline -5
git remote -v
```

Expected:

- Branch line shows `Jesus_5.1.8.FabelSecurityScan`.
- Remote still points to `https://github.com/MengSung/ChurchReport.git`.
- Existing unrelated modified or untracked files may appear, but they are not included in the migration commits unless explicitly listed in this plan.

- [ ] **Step 3: Verify target folders do not already exist**

```powershell
$sourceRoot = (Get-Location).Path
$systemRoot = Split-Path -Parent $sourceRoot
$backupPath = Join-Path $systemRoot 'ChurchReport-full-history.git'
$targetPath = Join-Path $systemRoot 'SpeechMessageProducts'
Write-Host "backupPath=$backupPath"
Write-Host "targetPath=$targetPath"
if (Test-Path -LiteralPath $backupPath) { throw "Backup path already exists: $backupPath" }
if (Test-Path -LiteralPath $targetPath) { throw "Target path already exists: $targetPath" }
```

Expected: both paths are printed and neither exists.

- [ ] **Step 4: Verify worktree risk before folder operations**

```powershell
git worktree list --porcelain
```

Expected: worktrees may be listed. Do not rename the original `ChurchReport` folder during this migration.

---

### Task 2: Verify New GitHub Repository Target

**Files:**
- Modify: none

- [ ] **Step 1: Check whether the new remote exists**

```powershell
$newRemote = 'https://github.com/MengSung/SpeechMessageProducts.git'
git ls-remote $newRemote
```

Expected when the GitHub repository already exists: command exits `0` and may print no refs if the repo is empty.

Expected when the GitHub repository does not exist: command fails. Create an empty GitHub repository named `MengSung/SpeechMessageProducts` with no README, no `.gitignore`, and no license, then run the command again.

- [ ] **Step 2: Confirm the old remote remains unchanged**

```powershell
git remote -v
```

Expected: current `ChurchReport` checkout still uses `https://github.com/MengSung/ChurchReport.git`.

---

### Task 3: Create Full Mirror Backup

**Files:**
- Create outside repo: `<system-platform-root>\ChurchReport-full-history.git`
- Modify: none

- [ ] **Step 1: Create mirror backup from current repository**

```powershell
$ErrorActionPreference = 'Stop'
$sourceRoot = (Get-Location).Path
$systemRoot = Split-Path -Parent $sourceRoot
$backupPath = Join-Path $systemRoot 'ChurchReport-full-history.git'
if (Test-Path -LiteralPath $backupPath) { throw "Backup path already exists: $backupPath" }
git clone --mirror $sourceRoot $backupPath
```

Expected: Git creates a bare mirror repository at `ChurchReport-full-history.git`.

- [ ] **Step 2: Verify mirror backup has refs and current HEAD**

```powershell
$sourceRoot = (Get-Location).Path
$systemRoot = Split-Path -Parent $sourceRoot
$backupPath = Join-Path $systemRoot 'ChurchReport-full-history.git'
$sourceHead = git -C $sourceRoot rev-parse HEAD
$backupHasHead = git --git-dir="$backupPath" cat-file -t $sourceHead
$headCount = (git --git-dir="$backupPath" for-each-ref --format='%(refname)' refs/heads | Measure-Object).Count
$tagCount = (git --git-dir="$backupPath" for-each-ref --format='%(refname)' refs/tags | Measure-Object).Count
Write-Host "backupHasHead=$backupHasHead"
Write-Host "headCount=$headCount"
Write-Host "tagCount=$tagCount"
if ($backupHasHead -ne 'commit') { throw "Mirror backup does not contain source HEAD $sourceHead" }
if ($headCount -lt 1) { throw "Mirror backup has no heads" }
```

Expected: `backupHasHead=commit` and `headCount` is at least `1`.

---

### Task 4: Create SpeechMessageProducts Working Clone

**Files:**
- Create outside repo: `<system-platform-root>\SpeechMessageProducts`
- Modify: none in original repo

- [ ] **Step 1: Clone from the mirror backup into the new folder**

```powershell
$ErrorActionPreference = 'Stop'
$sourceRoot = (Get-Location).Path
$systemRoot = Split-Path -Parent $sourceRoot
$backupPath = Join-Path $systemRoot 'ChurchReport-full-history.git'
$targetPath = Join-Path $systemRoot 'SpeechMessageProducts'
if (-not (Test-Path -LiteralPath $backupPath)) { throw "Missing backup path: $backupPath" }
if (Test-Path -LiteralPath $targetPath) { throw "Target path already exists: $targetPath" }
git clone --no-local $backupPath $targetPath
```

Expected: a new working repository exists at `SpeechMessageProducts`.

- [ ] **Step 2: Verify new clone contains the selected source commit**

```powershell
$sourceRoot = (Get-Location).Path
$systemRoot = Split-Path -Parent $sourceRoot
$targetPath = Join-Path $systemRoot 'SpeechMessageProducts'
$sourceHead = git -C $sourceRoot rev-parse HEAD
$targetHasHead = git -C $targetPath cat-file -t $sourceHead
Write-Host "sourceHead=$sourceHead"
Write-Host "targetHasHead=$targetHasHead"
if ($targetHasHead -ne 'commit') { throw "New clone does not contain source HEAD $sourceHead" }
```

Expected: `targetHasHead=commit`.

---

### Task 5: Create Clean Main Branch and Set New Remote

**Files:**
- Modify outside repo: `<system-platform-root>\SpeechMessageProducts\.git\config`

- [ ] **Step 1: Create `main` in the new clone from the source HEAD**

```powershell
$ErrorActionPreference = 'Stop'
$sourceRoot = (Get-Location).Path
$systemRoot = Split-Path -Parent $sourceRoot
$targetPath = Join-Path $systemRoot 'SpeechMessageProducts'
$sourceHead = git -C $sourceRoot rev-parse HEAD
git -C $targetPath switch --detach $sourceHead
git -C $targetPath switch -c main
git -C $targetPath log --oneline -3
```

Expected: the new clone is on local branch `main` at the same commit as the source checkout.

- [ ] **Step 2: Point only the new clone to `SpeechMessageProducts` remote**

```powershell
$systemRoot = Split-Path -Parent (Get-Location).Path
$targetPath = Join-Path $systemRoot 'SpeechMessageProducts'
$newRemote = 'https://github.com/MengSung/SpeechMessageProducts.git'
git -C $targetPath remote set-url origin $newRemote
git -C $targetPath remote -v
git remote -v
```

Expected:

- In the new clone, `origin` points to `https://github.com/MengSung/SpeechMessageProducts.git`.
- In the original checkout, `origin` still points to `https://github.com/MengSung/ChurchReport.git`.

---

### Task 6: Push New Main Branch

**Files:**
- Modify remote repository: `MengSung/SpeechMessageProducts`

- [ ] **Step 1: Push `main` to the new repository**

```powershell
$systemRoot = Split-Path -Parent (Get-Location).Path
$targetPath = Join-Path $systemRoot 'SpeechMessageProducts'
git -C $targetPath push -u origin main
```

Expected: push succeeds and `main` tracks `origin/main`.

- [ ] **Step 2: Verify remote main points to the pushed commit**

```powershell
$systemRoot = Split-Path -Parent (Get-Location).Path
$targetPath = Join-Path $systemRoot 'SpeechMessageProducts'
$localMain = git -C $targetPath rev-parse main
$remoteMain = git -C $targetPath ls-remote origin refs/heads/main
Write-Host "localMain=$localMain"
Write-Host "remoteMain=$remoteMain"
if ($remoteMain -notmatch [regex]::Escape($localMain)) { throw "origin/main does not point to local main" }
```

Expected: `remoteMain` contains the same commit hash as `localMain`.

---

### Task 7: Rename Solution in New Repository Only

**Files:**
- Rename outside original repo: `<system-platform-root>\SpeechMessageProducts\ChurchReport.sln` -> `<system-platform-root>\SpeechMessageProducts\SpeechMessageProducts.sln`

- [ ] **Step 1: Rename solution file through Git**

```powershell
$systemRoot = Split-Path -Parent (Get-Location).Path
$targetPath = Join-Path $systemRoot 'SpeechMessageProducts'
git -C $targetPath mv ChurchReport.sln SpeechMessageProducts.sln
git -C $targetPath status --short
```

Expected: status shows `R  ChurchReport.sln -> SpeechMessageProducts.sln`.

- [ ] **Step 2: Restore the renamed solution**

```powershell
$systemRoot = Split-Path -Parent (Get-Location).Path
$targetPath = Join-Path $systemRoot 'SpeechMessageProducts'
dotnet restore (Join-Path $targetPath 'SpeechMessageProducts.sln')
```

Expected: restore completes without errors.

- [ ] **Step 3: Build the renamed solution**

```powershell
$systemRoot = Split-Path -Parent (Get-Location).Path
$targetPath = Join-Path $systemRoot 'SpeechMessageProducts'
dotnet build (Join-Path $targetPath 'SpeechMessageProducts.sln') --no-restore
```

Expected: build completes. Existing warnings can be recorded, but errors must be fixed before committing the solution rename.

- [ ] **Step 4: Commit the solution rename in the new repository**

```powershell
$systemRoot = Split-Path -Parent (Get-Location).Path
$targetPath = Join-Path $systemRoot 'SpeechMessageProducts'
git -C $targetPath add SpeechMessageProducts.sln
git -C $targetPath commit -m "chore: rename solution to SpeechMessageProducts"
git -C $targetPath push
```

Expected: new commit is pushed to `origin/main` in `SpeechMessageProducts`.

---

### Task 8: Validate Original Repository Was Not Converted

**Files:**
- Read: original `ChurchReport` checkout
- Modify: none

- [ ] **Step 1: Confirm original remote and solution remain unchanged**

Run from the original `ChurchReport` checkout:

```powershell
git remote -v
Test-Path .\ChurchReport.sln
Test-Path .\SpeechMessageProducts.sln
git status --short --branch
```

Expected:

- Original remote still points to `https://github.com/MengSung/ChurchReport.git`.
- `ChurchReport.sln` still exists in the original checkout.
- `SpeechMessageProducts.sln` does not exist in the original checkout.
- Existing unrelated pending files may remain.

- [ ] **Step 2: Confirm new repository identity**

```powershell
$systemRoot = Split-Path -Parent (Get-Location).Path
$targetPath = Join-Path $systemRoot 'SpeechMessageProducts'
git -C $targetPath remote -v
git -C $targetPath branch --show-current
Test-Path (Join-Path $targetPath 'SpeechMessageProducts.sln')
git -C $targetPath log --oneline -5
```

Expected:

- New remote points to `https://github.com/MengSung/SpeechMessageProducts.git`.
- Current branch is `main`.
- `SpeechMessageProducts.sln` exists.
- Recent log includes the solution rename commit and the preserved prior history.

---

### Task 9: Defer Optional Branch Allowlist

**Files:**
- Modify remote repository only if user approves branch import later

- [ ] **Step 1: List candidate architecture branches without pushing them**

```powershell
git branch --list 'Jesus_5.1.3*' 'Jesus_5.1.6*' 'Jesus_5.1.7*' 'Jesus_5.1.8*'
```

Expected: command lists candidate payment, LINE, RichMenu, and current security-scan branches.

- [ ] **Step 2: Record the branch import decision**

Default decision for first migration pass:

```text
Do not push old branches during the first pass. Keep full history in ChurchReport-full-history.git and only push main to SpeechMessageProducts.
```

Expected: no old customer or historical branches are pushed to `SpeechMessageProducts` during this first pass.

---

### Task 10: Close Out Migration Task

**Files:**
- Modify: `.ccg/tasks/rename-repo-to-speech-message-products/task.json`
- Create: `.ccg/tasks/rename-repo-to-speech-message-products/review.md`

- [ ] **Step 1: Write migration review notes**

Create `.ccg/tasks/rename-repo-to-speech-message-products/review.md` with this content after execution:

```markdown
# SpeechMessageProducts Repository Migration Review

## Result

- Mirror backup created: `<system-platform-root>\ChurchReport-full-history.git`
- New working repository created: `<system-platform-root>\SpeechMessageProducts`
- New remote: `https://github.com/MengSung/SpeechMessageProducts.git`
- New primary branch: `main`
- Solution renamed in new repository: `SpeechMessageProducts.sln`

## Validation

- Mirror backup contains source HEAD.
- New clone contains source HEAD.
- `origin/main` in `SpeechMessageProducts` points to the expected commit.
- `dotnet restore SpeechMessageProducts.sln` completed.
- `dotnet build SpeechMessageProducts.sln --no-restore` completed.
- Original `ChurchReport` checkout still points to `https://github.com/MengSung/ChurchReport.git`.

## Deferred Work

- Main project folder rename.
- Namespace and assembly cleanup.
- Cookie/auth/deployment identity rename.
- Optional branch allowlist import.
```

Expected: review file records concrete execution results and deferred work.

- [ ] **Step 2: Update task status**

Edit `.ccg/tasks/rename-repo-to-speech-message-products/task.json` so these fields match:

```json
{
  "status": "in_progress",
  "currentPhase": "review",
  "nextAction": "Review migration execution results and archive task"
}
```

Expected: task is ready for final review and archive.

- [ ] **Step 3: Commit the closeout notes**

```powershell
git add .ccg/tasks/rename-repo-to-speech-message-products/task.json .ccg/tasks/rename-repo-to-speech-message-products/review.md
git commit -m "docs: record speech message products migration results"
```

Expected: closeout notes are committed.

---

## Verification Checklist

- [ ] Original `ChurchReport` folder still exists.
- [ ] Original `ChurchReport` remote still points to `MengSung/ChurchReport`.
- [ ] Mirror backup exists and contains source HEAD.
- [ ] New `SpeechMessageProducts` folder exists.
- [ ] New repository `origin` points to `MengSung/SpeechMessageProducts`.
- [ ] New repository branch is `main`.
- [ ] New repository preserves prior commit history.
- [ ] `SpeechMessageProducts.sln` exists in the new repository.
- [ ] `dotnet restore SpeechMessageProducts.sln` succeeds.
- [ ] `dotnet build SpeechMessageProducts.sln --no-restore` succeeds.
- [ ] No bulk `ChurchReport` string replacement was performed.
- [ ] No old customer/version branches were pushed during the first pass.
