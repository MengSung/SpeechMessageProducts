# CCG reviewer Task: full-code-quality-audit-and-fix-review-rerun

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.0.Initialization.Worktree

## Request
# Full Code Quality Audit And Fix Review Request

Review the current git diff for defects in:

- Session isolation and cross-user data leakage
- Memory cache lifetime, disposal, and bounded growth
- LINE and HTTP client ownership
- Sync-over-async request paths
- Object-level authorization
- CRM query performance and unbounded reads
- Secret/config handling

Use Critical / Warning / Info severity. Cite exact files and line numbers. Do not suggest broad rewrites unless a concrete defect remains.

Notes:

- Secret-like values in appsettings diff are intentionally redacted in this prompt, including commented historical examples. The actual branch blanks active-looking checked-in secrets and adds environment-variable fallback where needed.
- Do not treat test-only 
ew HttpClient(...) around in-memory handlers as production socket exhaustion unless a production call path is involved.
- The untracked code helper ToolUtility.Tests/TestHelpers/MockOrganizationServiceFactory.cs is included below because git diff does not include untracked files.
- Previous degraded CCG review found that QueryListByContactId omitted 
ew_happy_start_date and 
ew_happy_end_date; this diff now includes those columns and a regression assertion.
- Previous degraded CCG review warned that CreateClient("LineLoginOAuth") lacked a named registration; this diff now includes services.AddHttpClient("LineLoginOAuth", ...).

Diff under review:

``text
diff --git a/.ccg/dual-model-runs/20260704-135739-review-task/ccg-health-20260704-135756.json b/.ccg/dual-model-runs/20260704-135739-review-task/ccg-health-20260704-135756.json
index 157d76db..fff24f5d 100644
--- a/.ccg/dual-model-runs/20260704-135739-review-task/ccg-health-20260704-135756.json
+++ b/.ccg/dual-model-runs/20260704-135739-review-task/ccg-health-20260704-135756.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T13:57:39.8747416+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-135739-review-task/summary.json b/.ccg/dual-model-runs/20260704-135739-review-task/summary.json
index 68cbbe25..ef5421c9 100644
--- a/.ccg/dual-model-runs/20260704-135739-review-task/summary.json
+++ b/.ccg/dual-model-runs/20260704-135739-review-task/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-135739-review-task",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-140352-line-richmenu-shared-orchestrator-review/ccg-health-20260704-140405.json b/.ccg/dual-model-runs/20260704-140352-line-richmenu-shared-orchestrator-review/ccg-health-20260704-140405.json
index ff030f1b..e2db7f44 100644
--- a/.ccg/dual-model-runs/20260704-140352-line-richmenu-shared-orchestrator-review/ccg-health-20260704-140405.json
+++ b/.ccg/dual-model-runs/20260704-140352-line-richmenu-shared-orchestrator-review/ccg-health-20260704-140405.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T14:03:52.6451753+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-140352-line-richmenu-shared-orchestrator-review/summary.json b/.ccg/dual-model-runs/20260704-140352-line-richmenu-shared-orchestrator-review/summary.json
index 19fb6e29..d86bfb67 100644
--- a/.ccg/dual-model-runs/20260704-140352-line-richmenu-shared-orchestrator-review/summary.json
+++ b/.ccg/dual-model-runs/20260704-140352-line-richmenu-shared-orchestrator-review/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-140352-line-richmenu-shared-orchestrator-review",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-142701-review-task/ccg-health-20260704-142723.json b/.ccg/dual-model-runs/20260704-142701-review-task/ccg-health-20260704-142723.json
index d4fd249d..c58481f3 100644
--- a/.ccg/dual-model-runs/20260704-142701-review-task/ccg-health-20260704-142723.json
+++ b/.ccg/dual-model-runs/20260704-142701-review-task/ccg-health-20260704-142723.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T14:27:01.9732522+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-142701-review-task/summary.json b/.ccg/dual-model-runs/20260704-142701-review-task/summary.json
index 2d5ded9c..804d0fde 100644
--- a/.ccg/dual-model-runs/20260704-142701-review-task/summary.json
+++ b/.ccg/dual-model-runs/20260704-142701-review-task/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-142701-review-task",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-145018-review-task/ccg-health-20260704-145034.json b/.ccg/dual-model-runs/20260704-145018-review-task/ccg-health-20260704-145034.json
index 2231858e..0f7ec2b1 100644
--- a/.ccg/dual-model-runs/20260704-145018-review-task/ccg-health-20260704-145034.json
+++ b/.ccg/dual-model-runs/20260704-145018-review-task/ccg-health-20260704-145034.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T14:50:19.1541631+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-145536-review-task/ccg-health-20260704-145549.json b/.ccg/dual-model-runs/20260704-145536-review-task/ccg-health-20260704-145549.json
index 1f927c6c..35928ca8 100644
--- a/.ccg/dual-model-runs/20260704-145536-review-task/ccg-health-20260704-145549.json
+++ b/.ccg/dual-model-runs/20260704-145536-review-task/ccg-health-20260704-145549.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T14:55:37.1439012+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-145536-review-task/summary.json b/.ccg/dual-model-runs/20260704-145536-review-task/summary.json
index 8c8bf276..3a65250d 100644
--- a/.ccg/dual-model-runs/20260704-145536-review-task/summary.json
+++ b/.ccg/dual-model-runs/20260704-145536-review-task/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-145536-review-task",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-150541-self-healing-smoke-review/ccg-health-20260704-150555.json b/.ccg/dual-model-runs/20260704-150541-self-healing-smoke-review/ccg-health-20260704-150555.json
index 1b5b47f9..412116bc 100644
--- a/.ccg/dual-model-runs/20260704-150541-self-healing-smoke-review/ccg-health-20260704-150555.json
+++ b/.ccg/dual-model-runs/20260704-150541-self-healing-smoke-review/ccg-health-20260704-150555.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T15:05:42.0021417+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-150541-self-healing-smoke-review/summary.json b/.ccg/dual-model-runs/20260704-150541-self-healing-smoke-review/summary.json
index 62f9fe80..0c3c6100 100644
--- a/.ccg/dual-model-runs/20260704-150541-self-healing-smoke-review/summary.json
+++ b/.ccg/dual-model-runs/20260704-150541-self-healing-smoke-review/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-150541-self-healing-smoke-review",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-150629-ccg-self-healing-formal-review/ccg-health-20260704-150642.json b/.ccg/dual-model-runs/20260704-150629-ccg-self-healing-formal-review/ccg-health-20260704-150642.json
index 4980c3c5..2bb34c2c 100644
--- a/.ccg/dual-model-runs/20260704-150629-ccg-self-healing-formal-review/ccg-health-20260704-150642.json
+++ b/.ccg/dual-model-runs/20260704-150629-ccg-self-healing-formal-review/ccg-health-20260704-150642.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T15:06:29.4546011+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-150629-ccg-self-healing-formal-review/summary.json b/.ccg/dual-model-runs/20260704-150629-ccg-self-healing-formal-review/summary.json
index ab2ed64f..f41f8df2 100644
--- a/.ccg/dual-model-runs/20260704-150629-ccg-self-healing-formal-review/summary.json
+++ b/.ccg/dual-model-runs/20260704-150629-ccg-self-healing-formal-review/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-150629-ccg-self-healing-formal-review",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-151542-self-healing-smoke-review-v2/ccg-health-20260704-151542.json b/.ccg/dual-model-runs/20260704-151542-self-healing-smoke-review-v2/ccg-health-20260704-151542.json
index ae3c2d87..a8fce119 100644
--- a/.ccg/dual-model-runs/20260704-151542-self-healing-smoke-review-v2/ccg-health-20260704-151542.json
+++ b/.ccg/dual-model-runs/20260704-151542-self-healing-smoke-review-v2/ccg-health-20260704-151542.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T15:15:42.7547729+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-151542-self-healing-smoke-review-v2/summary.json b/.ccg/dual-model-runs/20260704-151542-self-healing-smoke-review-v2/summary.json
index b9f311c4..d9346ada 100644
--- a/.ccg/dual-model-runs/20260704-151542-self-healing-smoke-review-v2/summary.json
+++ b/.ccg/dual-model-runs/20260704-151542-self-healing-smoke-review-v2/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-151542-self-healing-smoke-review-v2",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-152713-review-task/ccg-health-20260704-152714.json b/.ccg/dual-model-runs/20260704-152713-review-task/ccg-health-20260704-152714.json
index 3c0419ed..eba7dcb1 100644
--- a/.ccg/dual-model-runs/20260704-152713-review-task/ccg-health-20260704-152714.json
+++ b/.ccg/dual-model-runs/20260704-152713-review-task/ccg-health-20260704-152714.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T15:27:14.1436134+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-152713-review-task/summary.json b/.ccg/dual-model-runs/20260704-152713-review-task/summary.json
index 036d1d28..63848fec 100644
--- a/.ccg/dual-model-runs/20260704-152713-review-task/summary.json
+++ b/.ccg/dual-model-runs/20260704-152713-review-task/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-152713-review-task",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-153745-review-task/ccg-health-20260704-153745.json b/.ccg/dual-model-runs/20260704-153745-review-task/ccg-health-20260704-153745.json
index 93ad5779..9c02d5de 100644
--- a/.ccg/dual-model-runs/20260704-153745-review-task/ccg-health-20260704-153745.json
+++ b/.ccg/dual-model-runs/20260704-153745-review-task/ccg-health-20260704-153745.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T15:37:45.4565136+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-153745-review-task/summary.json b/.ccg/dual-model-runs/20260704-153745-review-task/summary.json
index cbb0de60..971d340d 100644
--- a/.ccg/dual-model-runs/20260704-153745-review-task/summary.json
+++ b/.ccg/dual-model-runs/20260704-153745-review-task/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-153745-review-task",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-172256-richmenu-assignment-final-review-after-boundary-fix/ccg-health-20260704-172257.json b/.ccg/dual-model-runs/20260704-172256-richmenu-assignment-final-review-after-boundary-fix/ccg-health-20260704-172257.json
index 9536509f..246d9400 100644
--- a/.ccg/dual-model-runs/20260704-172256-richmenu-assignment-final-review-after-boundary-fix/ccg-health-20260704-172257.json
+++ b/.ccg/dual-model-runs/20260704-172256-richmenu-assignment-final-review-after-boundary-fix/ccg-health-20260704-172257.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T17:22:57.0391880+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-172256-richmenu-assignment-final-review-after-boundary-fix/ccg-health-20260704-172348.json b/.ccg/dual-model-runs/20260704-172256-richmenu-assignment-final-review-after-boundary-fix/ccg-health-20260704-172348.json
index f400df5d..4b30a270 100644
--- a/.ccg/dual-model-runs/20260704-172256-richmenu-assignment-final-review-after-boundary-fix/ccg-health-20260704-172348.json
+++ b/.ccg/dual-model-runs/20260704-172256-richmenu-assignment-final-review-after-boundary-fix/ccg-health-20260704-172348.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T17:23:48.7736777+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-172256-richmenu-assignment-final-review-after-boundary-fix/summary.json b/.ccg/dual-model-runs/20260704-172256-richmenu-assignment-final-review-after-boundary-fix/summary.json
index 60c8dc61..224df988 100644
--- a/.ccg/dual-model-runs/20260704-172256-richmenu-assignment-final-review-after-boundary-fix/summary.json
+++ b/.ccg/dual-model-runs/20260704-172256-richmenu-assignment-final-review-after-boundary-fix/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-172256-richmenu-assignment-final-review-after-boundary-fix",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-172640-richmenu-assignment-final-review-after-timeout-fix/ccg-health-20260704-172640.json b/.ccg/dual-model-runs/20260704-172640-richmenu-assignment-final-review-after-timeout-fix/ccg-health-20260704-172640.json
index f2575167..f9dafe04 100644
--- a/.ccg/dual-model-runs/20260704-172640-richmenu-assignment-final-review-after-timeout-fix/ccg-health-20260704-172640.json
+++ b/.ccg/dual-model-runs/20260704-172640-richmenu-assignment-final-review-after-timeout-fix/ccg-health-20260704-172640.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T17:26:40.8565074+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-172640-richmenu-assignment-final-review-after-timeout-fix/summary.json b/.ccg/dual-model-runs/20260704-172640-richmenu-assignment-final-review-after-timeout-fix/summary.json
index e0405b7d..7e9350f0 100644
--- a/.ccg/dual-model-runs/20260704-172640-richmenu-assignment-final-review-after-timeout-fix/summary.json
+++ b/.ccg/dual-model-runs/20260704-172640-richmenu-assignment-final-review-after-timeout-fix/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-172640-richmenu-assignment-final-review-after-timeout-fix",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-191410-ccg-fallback-policy-verification-review/ccg-health-20260704-191410.json b/.ccg/dual-model-runs/20260704-191410-ccg-fallback-policy-verification-review/ccg-health-20260704-191410.json
index 96a5de31..f1f1c1da 100644
--- a/.ccg/dual-model-runs/20260704-191410-ccg-fallback-policy-verification-review/ccg-health-20260704-191410.json
+++ b/.ccg/dual-model-runs/20260704-191410-ccg-fallback-policy-verification-review/ccg-health-20260704-191410.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T19:14:10.6488025+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-191410-ccg-fallback-policy-verification-review/summary.json b/.ccg/dual-model-runs/20260704-191410-ccg-fallback-policy-verification-review/summary.json
index 8f8851b2..68fd1c48 100644
--- a/.ccg/dual-model-runs/20260704-191410-ccg-fallback-policy-verification-review/summary.json
+++ b/.ccg/dual-model-runs/20260704-191410-ccg-fallback-policy-verification-review/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-191410-ccg-fallback-policy-verification-review",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-191446-ccg-fallback-policy-verification-review/ccg-health-20260704-191457.json b/.ccg/dual-model-runs/20260704-191446-ccg-fallback-policy-verification-review/ccg-health-20260704-191457.json
index 3e40cb34..86dbc980 100644
--- a/.ccg/dual-model-runs/20260704-191446-ccg-fallback-policy-verification-review/ccg-health-20260704-191457.json
+++ b/.ccg/dual-model-runs/20260704-191446-ccg-fallback-policy-verification-review/ccg-health-20260704-191457.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T19:14:46.7298529+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-191446-ccg-fallback-policy-verification-review/summary.json b/.ccg/dual-model-runs/20260704-191446-ccg-fallback-policy-verification-review/summary.json
index ed562c60..d3c66b3a 100644
--- a/.ccg/dual-model-runs/20260704-191446-ccg-fallback-policy-verification-review/summary.json
+++ b/.ccg/dual-model-runs/20260704-191446-ccg-fallback-policy-verification-review/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-191446-ccg-fallback-policy-verification-review",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-192048-20260704-192048-ccg-auto-recovery-entrypoint-smoke-reviewer/ccg-health-20260704-192048.json b/.ccg/dual-model-runs/20260704-192048-20260704-192048-ccg-auto-recovery-entrypoint-smoke-reviewer/ccg-health-20260704-192048.json
index ecb2f357..bac9ef52 100644
--- a/.ccg/dual-model-runs/20260704-192048-20260704-192048-ccg-auto-recovery-entrypoint-smoke-reviewer/ccg-health-20260704-192048.json
+++ b/.ccg/dual-model-runs/20260704-192048-20260704-192048-ccg-auto-recovery-entrypoint-smoke-reviewer/ccg-health-20260704-192048.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T19:20:48.5757433+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-192048-20260704-192048-ccg-auto-recovery-entrypoint-smoke-reviewer/summary.json b/.ccg/dual-model-runs/20260704-192048-20260704-192048-ccg-auto-recovery-entrypoint-smoke-reviewer/summary.json
index a8e59eea..17180886 100644
--- a/.ccg/dual-model-runs/20260704-192048-20260704-192048-ccg-auto-recovery-entrypoint-smoke-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260704-192048-20260704-192048-ccg-auto-recovery-entrypoint-smoke-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-192048-20260704-192048-ccg-auto-recovery-entrypoint-smoke-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-192751-20260704-192751-richmenu-shared-orchestrator-final-review-reviewer/ccg-health-20260704-192751.json b/.ccg/dual-model-runs/20260704-192751-20260704-192751-richmenu-shared-orchestrator-final-review-reviewer/ccg-health-20260704-192751.json
index b528b315..d84b1251 100644
--- a/.ccg/dual-model-runs/20260704-192751-20260704-192751-richmenu-shared-orchestrator-final-review-reviewer/ccg-health-20260704-192751.json
+++ b/.ccg/dual-model-runs/20260704-192751-20260704-192751-richmenu-shared-orchestrator-final-review-reviewer/ccg-health-20260704-192751.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T19:27:51.6998376+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-192751-20260704-192751-richmenu-shared-orchestrator-final-review-reviewer/summary.json b/.ccg/dual-model-runs/20260704-192751-20260704-192751-richmenu-shared-orchestrator-final-review-reviewer/summary.json
index 2b492f4b..fc46b227 100644
--- a/.ccg/dual-model-runs/20260704-192751-20260704-192751-richmenu-shared-orchestrator-final-review-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260704-192751-20260704-192751-richmenu-shared-orchestrator-final-review-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-192751-20260704-192751-richmenu-shared-orchestrator-final-review-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260704-193950-20260704-193950-richmenu-shared-orchestrator-postfix-review-reviewer/ccg-health-20260704-193950.json b/.ccg/dual-model-runs/20260704-193950-20260704-193950-richmenu-shared-orchestrator-postfix-review-reviewer/ccg-health-20260704-193950.json
index 6d0a01cc..ae7c59a1 100644
--- a/.ccg/dual-model-runs/20260704-193950-20260704-193950-richmenu-shared-orchestrator-postfix-review-reviewer/ccg-health-20260704-193950.json
+++ b/.ccg/dual-model-runs/20260704-193950-20260704-193950-richmenu-shared-orchestrator-postfix-review-reviewer/ccg-health-20260704-193950.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T19:39:50.7641412+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260704-193950-20260704-193950-richmenu-shared-orchestrator-postfix-review-reviewer/summary.json b/.ccg/dual-model-runs/20260704-193950-20260704-193950-richmenu-shared-orchestrator-postfix-review-reviewer/summary.json
index 3209f10f..91a57a8a 100644
--- a/.ccg/dual-model-runs/20260704-193950-20260704-193950-richmenu-shared-orchestrator-postfix-review-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260704-193950-20260704-193950-richmenu-shared-orchestrator-postfix-review-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260704-193950-20260704-193950-richmenu-shared-orchestrator-postfix-review-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
diff --git a/.ccg/dual-model-runs/20260705-074025-line-richmenu-word-manual-analysis-analyzer/ccg-health-20260705-074026.json b/.ccg/dual-model-runs/20260705-074025-line-richmenu-word-manual-analysis-analyzer/ccg-health-20260705-074026.json
index cb2ba5d5..7407e341 100644
--- a/.ccg/dual-model-runs/20260705-074025-line-richmenu-word-manual-analysis-analyzer/ccg-health-20260705-074026.json
+++ b/.ccg/dual-model-runs/20260705-074025-line-richmenu-word-manual-analysis-analyzer/ccg-health-20260705-074026.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-05T07:40:25.6318046+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260705-074025-line-richmenu-word-manual-analysis-analyzer/summary.json b/.ccg/dual-model-runs/20260705-074025-line-richmenu-word-manual-analysis-analyzer/summary.json
index 9fe3a07d..c00c665c 100644
--- a/.ccg/dual-model-runs/20260705-074025-line-richmenu-word-manual-analysis-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260705-074025-line-richmenu-word-manual-analysis-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260705-074025-line-richmenu-word-manual-analysis-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
diff --git a/.ccg/dual-model-runs/20260705-074146-annotate-richmenu-cs-files-analyzer/ccg-health-20260705-074146.json b/.ccg/dual-model-runs/20260705-074146-annotate-richmenu-cs-files-analyzer/ccg-health-20260705-074146.json
index d562492b..b39115e7 100644
--- a/.ccg/dual-model-runs/20260705-074146-annotate-richmenu-cs-files-analyzer/ccg-health-20260705-074146.json
+++ b/.ccg/dual-model-runs/20260705-074146-annotate-richmenu-cs-files-analyzer/ccg-health-20260705-074146.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-05T07:41:46.5174033+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRichMenuAddComment",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260705-074146-annotate-richmenu-cs-files-analyzer/summary.json b/.ccg/dual-model-runs/20260705-074146-annotate-richmenu-cs-files-analyzer/summary.json
index 26fed34b..c8fe2e2f 100644
--- a/.ccg/dual-model-runs/20260705-074146-annotate-richmenu-cs-files-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260705-074146-annotate-richmenu-cs-files-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260705-074146-annotate-richmenu-cs-files-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRichMenuAddComment",
diff --git a/.ccg/dual-model-runs/20260705-082759-annotate-richmenu-cs-files-reviewer/ccg-health-20260705-082800.json b/.ccg/dual-model-runs/20260705-082759-annotate-richmenu-cs-files-reviewer/ccg-health-20260705-082800.json
index 1c1ae4f2..8ebe6679 100644
--- a/.ccg/dual-model-runs/20260705-082759-annotate-richmenu-cs-files-reviewer/ccg-health-20260705-082800.json
+++ b/.ccg/dual-model-runs/20260705-082759-annotate-richmenu-cs-files-reviewer/ccg-health-20260705-082800.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-05T08:28:00.2098160+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRichMenuAddComment",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260705-082759-annotate-richmenu-cs-files-reviewer/ccg-health-20260705-083432.json b/.ccg/dual-model-runs/20260705-082759-annotate-richmenu-cs-files-reviewer/ccg-health-20260705-083432.json
index 504667d7..a0d1f33e 100644
--- a/.ccg/dual-model-runs/20260705-082759-annotate-richmenu-cs-files-reviewer/ccg-health-20260705-083432.json
+++ b/.ccg/dual-model-runs/20260705-082759-annotate-richmenu-cs-files-reviewer/ccg-health-20260705-083432.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-05T08:34:32.0473419+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRichMenuAddComment",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260705-082759-annotate-richmenu-cs-files-reviewer/summary.json b/.ccg/dual-model-runs/20260705-082759-annotate-richmenu-cs-files-reviewer/summary.json
index 2132ec10..5a192114 100644
--- a/.ccg/dual-model-runs/20260705-082759-annotate-richmenu-cs-files-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260705-082759-annotate-richmenu-cs-files-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260705-082759-annotate-richmenu-cs-files-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRichMenuAddComment",
diff --git a/.ccg/dual-model-runs/20260705-090454-annotate-all-cs-files-analyzer/ccg-health-20260705-090454.json b/.ccg/dual-model-runs/20260705-090454-annotate-all-cs-files-analyzer/ccg-health-20260705-090454.json
index 19c3fb5e..2e3dc503 100644
--- a/.ccg/dual-model-runs/20260705-090454-annotate-all-cs-files-analyzer/ccg-health-20260705-090454.json
+++ b/.ccg/dual-model-runs/20260705-090454-annotate-all-cs-files-analyzer/ccg-health-20260705-090454.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-05T09:04:54.6826869+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRichMenuAddComment",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260705-090454-annotate-all-cs-files-analyzer/summary.json b/.ccg/dual-model-runs/20260705-090454-annotate-all-cs-files-analyzer/summary.json
index 568c3a1e..b76e7f4e 100644
--- a/.ccg/dual-model-runs/20260705-090454-annotate-all-cs-files-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260705-090454-annotate-all-cs-files-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260705-090454-annotate-all-cs-files-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRichMenuAddComment",
diff --git a/.ccg/dual-model-runs/20260705-093047-annotate-all-cs-files-reviewer/ccg-health-20260705-093047.json b/.ccg/dual-model-runs/20260705-093047-annotate-all-cs-files-reviewer/ccg-health-20260705-093047.json
index 0353c235..d5400e72 100644
--- a/.ccg/dual-model-runs/20260705-093047-annotate-all-cs-files-reviewer/ccg-health-20260705-093047.json
+++ b/.ccg/dual-model-runs/20260705-093047-annotate-all-cs-files-reviewer/ccg-health-20260705-093047.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-05T09:30:47.8018168+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRichMenuAddComment",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260705-093047-annotate-all-cs-files-reviewer/summary.json b/.ccg/dual-model-runs/20260705-093047-annotate-all-cs-files-reviewer/summary.json
index 9aab93c8..389a7251 100644
--- a/.ccg/dual-model-runs/20260705-093047-annotate-all-cs-files-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260705-093047-annotate-all-cs-files-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260705-093047-annotate-all-cs-files-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRichMenuAddComment",
diff --git a/.ccg/dual-model-runs/20260706-085720-subagent-goal-word-tutorial-analysis-analyzer/ccg-health-20260706-085720.json b/.ccg/dual-model-runs/20260706-085720-subagent-goal-word-tutorial-analysis-analyzer/ccg-health-20260706-085720.json
index ac69df62..8decb525 100644
--- a/.ccg/dual-model-runs/20260706-085720-subagent-goal-word-tutorial-analysis-analyzer/ccg-health-20260706-085720.json
+++ b/.ccg/dual-model-runs/20260706-085720-subagent-goal-word-tutorial-analysis-analyzer/ccg-health-20260706-085720.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-06T08:57:20.7536242+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260706-085720-subagent-goal-word-tutorial-analysis-analyzer/summary.json b/.ccg/dual-model-runs/20260706-085720-subagent-goal-word-tutorial-analysis-analyzer/summary.json
index 4a693795..11b4379f 100644
--- a/.ccg/dual-model-runs/20260706-085720-subagent-goal-word-tutorial-analysis-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260706-085720-subagent-goal-word-tutorial-analysis-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260706-085720-subagent-goal-word-tutorial-analysis-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
diff --git a/.ccg/dual-model-runs/20260706-090921-subagent-goal-word-tutorial-review-reviewer/ccg-health-20260706-090921.json b/.ccg/dual-model-runs/20260706-090921-subagent-goal-word-tutorial-review-reviewer/ccg-health-20260706-090921.json
index 01435b63..22f239de 100644
--- a/.ccg/dual-model-runs/20260706-090921-subagent-goal-word-tutorial-review-reviewer/ccg-health-20260706-090921.json
+++ b/.ccg/dual-model-runs/20260706-090921-subagent-goal-word-tutorial-review-reviewer/ccg-health-20260706-090921.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-06T09:09:21.8941200+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260706-090921-subagent-goal-word-tutorial-review-reviewer/summary.json b/.ccg/dual-model-runs/20260706-090921-subagent-goal-word-tutorial-review-reviewer/summary.json
index 06065472..ef3fb7a1 100644
--- a/.ccg/dual-model-runs/20260706-090921-subagent-goal-word-tutorial-review-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260706-090921-subagent-goal-word-tutorial-review-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260706-090921-subagent-goal-word-tutorial-review-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
diff --git a/.ccg/dual-model-runs/20260706-100307-show-line-send-result-for-donations-analyzer/ccg-health-20260706-100308.json b/.ccg/dual-model-runs/20260706-100307-show-line-send-result-for-donations-analyzer/ccg-health-20260706-100308.json
index a954ce33..3b077aea 100644
--- a/.ccg/dual-model-runs/20260706-100307-show-line-send-result-for-donations-analyzer/ccg-health-20260706-100308.json
+++ b/.ccg/dual-model-runs/20260706-100307-show-line-send-result-for-donations-analyzer/ccg-health-20260706-100308.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-06T10:03:07.9442665+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260706-100307-show-line-send-result-for-donations-analyzer/summary.json b/.ccg/dual-model-runs/20260706-100307-show-line-send-result-for-donations-analyzer/summary.json
index a405eeb6..c31eca25 100644
--- a/.ccg/dual-model-runs/20260706-100307-show-line-send-result-for-donations-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260706-100307-show-line-send-result-for-donations-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260706-100307-show-line-send-result-for-donations-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
diff --git a/.ccg/dual-model-runs/20260706-101857-show-line-send-result-for-donations-reviewer/ccg-health-20260706-101857.json b/.ccg/dual-model-runs/20260706-101857-show-line-send-result-for-donations-reviewer/ccg-health-20260706-101857.json
index c3f14354..0d7a0953 100644
--- a/.ccg/dual-model-runs/20260706-101857-show-line-send-result-for-donations-reviewer/ccg-health-20260706-101857.json
+++ b/.ccg/dual-model-runs/20260706-101857-show-line-send-result-for-donations-reviewer/ccg-health-20260706-101857.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-06T10:18:57.4711821+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260706-101857-show-line-send-result-for-donations-reviewer/summary.json b/.ccg/dual-model-runs/20260706-101857-show-line-send-result-for-donations-reviewer/summary.json
index aebdd770..80f9f3d4 100644
--- a/.ccg/dual-model-runs/20260706-101857-show-line-send-result-for-donations-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260706-101857-show-line-send-result-for-donations-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260706-101857-show-line-send-result-for-donations-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
diff --git a/.ccg/dual-model-runs/20260706-102253-show-line-send-result-for-donations-reviewer/ccg-health-20260706-102253.json b/.ccg/dual-model-runs/20260706-102253-show-line-send-result-for-donations-reviewer/ccg-health-20260706-102253.json
index 8823c085..450bf697 100644
--- a/.ccg/dual-model-runs/20260706-102253-show-line-send-result-for-donations-reviewer/ccg-health-20260706-102253.json
+++ b/.ccg/dual-model-runs/20260706-102253-show-line-send-result-for-donations-reviewer/ccg-health-20260706-102253.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-06T10:22:53.4913058+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260706-102253-show-line-send-result-for-donations-reviewer/summary.json b/.ccg/dual-model-runs/20260706-102253-show-line-send-result-for-donations-reviewer/summary.json
index 26a697ae..052ad20f 100644
--- a/.ccg/dual-model-runs/20260706-102253-show-line-send-result-for-donations-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260706-102253-show-line-send-result-for-donations-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260706-102253-show-line-send-result-for-donations-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
diff --git a/.ccg/dual-model-runs/20260706-102713-show-line-send-result-for-donations-reviewer/ccg-health-20260706-102713.json b/.ccg/dual-model-runs/20260706-102713-show-line-send-result-for-donations-reviewer/ccg-health-20260706-102713.json
index edd1ded5..2bd6888c 100644
--- a/.ccg/dual-model-runs/20260706-102713-show-line-send-result-for-donations-reviewer/ccg-health-20260706-102713.json
+++ b/.ccg/dual-model-runs/20260706-102713-show-line-send-result-for-donations-reviewer/ccg-health-20260706-102713.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-06T10:27:13.3583152+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260706-102713-show-line-send-result-for-donations-reviewer/summary.json b/.ccg/dual-model-runs/20260706-102713-show-line-send-result-for-donations-reviewer/summary.json
index 2ca50b50..0ce1aeb1 100644
--- a/.ccg/dual-model-runs/20260706-102713-show-line-send-result-for-donations-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260706-102713-show-line-send-result-for-donations-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260706-102713-show-line-send-result-for-donations-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
diff --git a/.ccg/dual-model-runs/20260706-114901-speed-up-atm-donation-submit-analyzer/ccg-health-20260706-114901.json b/.ccg/dual-model-runs/20260706-114901-speed-up-atm-donation-submit-analyzer/ccg-health-20260706-114901.json
index e546a766..fc1b8f5f 100644
--- a/.ccg/dual-model-runs/20260706-114901-speed-up-atm-donation-submit-analyzer/ccg-health-20260706-114901.json
+++ b/.ccg/dual-model-runs/20260706-114901-speed-up-atm-donation-submit-analyzer/ccg-health-20260706-114901.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-06T11:49:01.5693586+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260706-114901-speed-up-atm-donation-submit-analyzer/summary.json b/.ccg/dual-model-runs/20260706-114901-speed-up-atm-donation-submit-analyzer/summary.json
index ffd6c4c3..2c1e52f3 100644
--- a/.ccg/dual-model-runs/20260706-114901-speed-up-atm-donation-submit-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260706-114901-speed-up-atm-donation-submit-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260706-114901-speed-up-atm-donation-submit-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
diff --git a/.ccg/dual-model-runs/20260706-120419-speed-up-atm-donation-submit-reviewer/ccg-health-20260706-120419.json b/.ccg/dual-model-runs/20260706-120419-speed-up-atm-donation-submit-reviewer/ccg-health-20260706-120419.json
index b6e93d4a..48b53288 100644
--- a/.ccg/dual-model-runs/20260706-120419-speed-up-atm-donation-submit-reviewer/ccg-health-20260706-120419.json
+++ b/.ccg/dual-model-runs/20260706-120419-speed-up-atm-donation-submit-reviewer/ccg-health-20260706-120419.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-06T12:04:19.7167423+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260706-120419-speed-up-atm-donation-submit-reviewer/ccg-health-20260706-121052.json b/.ccg/dual-model-runs/20260706-120419-speed-up-atm-donation-submit-reviewer/ccg-health-20260706-121052.json
index 9ef1481d..cc5e12c5 100644
--- a/.ccg/dual-model-runs/20260706-120419-speed-up-atm-donation-submit-reviewer/ccg-health-20260706-121052.json
+++ b/.ccg/dual-model-runs/20260706-120419-speed-up-atm-donation-submit-reviewer/ccg-health-20260706-121052.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-06T12:10:52.1979588+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260706-125016-reduce-line-wait-500ms-reviewer/ccg-health-20260706-125017.json b/.ccg/dual-model-runs/20260706-125016-reduce-line-wait-500ms-reviewer/ccg-health-20260706-125017.json
index 7721d932..e3f44924 100644
--- a/.ccg/dual-model-runs/20260706-125016-reduce-line-wait-500ms-reviewer/ccg-health-20260706-125017.json
+++ b/.ccg/dual-model-runs/20260706-125016-reduce-line-wait-500ms-reviewer/ccg-health-20260706-125017.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-06T12:50:17.1168394+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.8.WorktreeFabelSecurityScan",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260706-125016-reduce-line-wait-500ms-reviewer/ccg-health-20260706-125434.json b/.ccg/dual-model-runs/20260706-125016-reduce-line-wait-500ms-reviewer/ccg-health-20260706-125434.json
index 0f939e87..3c8e7cf6 100644
--- a/.ccg/dual-model-runs/20260706-125016-reduce-line-wait-500ms-reviewer/ccg-health-20260706-125434.json
+++ b/.ccg/dual-model-runs/20260706-125016-reduce-line-wait-500ms-reviewer/ccg-health-20260706-125434.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-06T12:54:34.2291408+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.8.WorktreeFabelSecurityScan",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260706-125016-reduce-line-wait-500ms-reviewer/summary.json b/.ccg/dual-model-runs/20260706-125016-reduce-line-wait-500ms-reviewer/summary.json
index 33cc8b70..663590c1 100644
--- a/.ccg/dual-model-runs/20260706-125016-reduce-line-wait-500ms-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260706-125016-reduce-line-wait-500ms-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260706-125016-reduce-line-wait-500ms-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.8.WorktreeFabelSecurityScan",
diff --git a/.ccg/dual-model-runs/20260706-160750-rename-main-project-to-speechmessageproducts-churchreport-analyzer/ccg-health-20260706-160751.json b/.ccg/dual-model-runs/20260706-160750-rename-main-project-to-speechmessageproducts-churchreport-analyzer/ccg-health-20260706-160751.json
index 0ad0707d..82828d17 100644
--- a/.ccg/dual-model-runs/20260706-160750-rename-main-project-to-speechmessageproducts-churchreport-analyzer/ccg-health-20260706-160751.json
+++ b/.ccg/dual-model-runs/20260706-160750-rename-main-project-to-speechmessageproducts-churchreport-analyzer/ccg-health-20260706-160751.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-06T16:07:51.1912558+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260706-160750-rename-main-project-to-speechmessageproducts-churchreport-analyzer/summary.json b/.ccg/dual-model-runs/20260706-160750-rename-main-project-to-speechmessageproducts-churchreport-analyzer/summary.json
index 0da97021..52d5e4cb 100644
--- a/.ccg/dual-model-runs/20260706-160750-rename-main-project-to-speechmessageproducts-churchreport-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260706-160750-rename-main-project-to-speechmessageproducts-churchreport-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260706-160750-rename-main-project-to-speechmessageproducts-churchreport-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-112544-dual-model-smoke-test-20260707-analyzer/ccg-health-20260707-112545.json b/.ccg/dual-model-runs/20260707-112544-dual-model-smoke-test-20260707-analyzer/ccg-health-20260707-112545.json
index 784ceef6..6356aa7a 100644
--- a/.ccg/dual-model-runs/20260707-112544-dual-model-smoke-test-20260707-analyzer/ccg-health-20260707-112545.json
+++ b/.ccg/dual-model-runs/20260707-112544-dual-model-smoke-test-20260707-analyzer/ccg-health-20260707-112545.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T11:25:45.0114458+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-112544-dual-model-smoke-test-20260707-analyzer/summary.json b/.ccg/dual-model-runs/20260707-112544-dual-model-smoke-test-20260707-analyzer/summary.json
index 0d2446f8..69d69fa8 100644
--- a/.ccg/dual-model-runs/20260707-112544-dual-model-smoke-test-20260707-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-112544-dual-model-smoke-test-20260707-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-112544-dual-model-smoke-test-20260707-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-114110-dual-model-smoke-test-20260707-rerun-analyzer/ccg-health-20260707-114110.json b/.ccg/dual-model-runs/20260707-114110-dual-model-smoke-test-20260707-rerun-analyzer/ccg-health-20260707-114110.json
index 4e2a6000..20f7ea2a 100644
--- a/.ccg/dual-model-runs/20260707-114110-dual-model-smoke-test-20260707-rerun-analyzer/ccg-health-20260707-114110.json
+++ b/.ccg/dual-model-runs/20260707-114110-dual-model-smoke-test-20260707-rerun-analyzer/ccg-health-20260707-114110.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T11:41:10.7187564+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-114110-dual-model-smoke-test-20260707-rerun-analyzer/summary.json b/.ccg/dual-model-runs/20260707-114110-dual-model-smoke-test-20260707-rerun-analyzer/summary.json
index 8006c8de..4118baa5 100644
--- a/.ccg/dual-model-runs/20260707-114110-dual-model-smoke-test-20260707-rerun-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-114110-dual-model-smoke-test-20260707-rerun-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-114110-dual-model-smoke-test-20260707-rerun-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-114132-dual-model-review-smoke-test-20260707-reviewer/ccg-health-20260707-114132.json b/.ccg/dual-model-runs/20260707-114132-dual-model-review-smoke-test-20260707-reviewer/ccg-health-20260707-114132.json
index a7fafa4d..752a86f8 100644
--- a/.ccg/dual-model-runs/20260707-114132-dual-model-review-smoke-test-20260707-reviewer/ccg-health-20260707-114132.json
+++ b/.ccg/dual-model-runs/20260707-114132-dual-model-review-smoke-test-20260707-reviewer/ccg-health-20260707-114132.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T11:41:32.8896967+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-114132-dual-model-review-smoke-test-20260707-reviewer/summary.json b/.ccg/dual-model-runs/20260707-114132-dual-model-review-smoke-test-20260707-reviewer/summary.json
index 7ed27399..0969534f 100644
--- a/.ccg/dual-model-runs/20260707-114132-dual-model-review-smoke-test-20260707-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-114132-dual-model-review-smoke-test-20260707-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-114132-dual-model-review-smoke-test-20260707-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-152917-dual-model-smoke-test-20260707-after-fix-analyzer/ccg-health-20260707-152918.json b/.ccg/dual-model-runs/20260707-152917-dual-model-smoke-test-20260707-after-fix-analyzer/ccg-health-20260707-152918.json
index 732d40e7..cbfb0248 100644
--- a/.ccg/dual-model-runs/20260707-152917-dual-model-smoke-test-20260707-after-fix-analyzer/ccg-health-20260707-152918.json
+++ b/.ccg/dual-model-runs/20260707-152917-dual-model-smoke-test-20260707-after-fix-analyzer/ccg-health-20260707-152918.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:29:18.1418742+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-152917-dual-model-smoke-test-20260707-after-fix-analyzer/summary.json b/.ccg/dual-model-runs/20260707-152917-dual-model-smoke-test-20260707-after-fix-analyzer/summary.json
index 119c93e2..76a4622a 100644
--- a/.ccg/dual-model-runs/20260707-152917-dual-model-smoke-test-20260707-after-fix-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-152917-dual-model-smoke-test-20260707-after-fix-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-152917-dual-model-smoke-test-20260707-after-fix-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-152954-dual-model-review-smoke-test-20260707-after-fix-reviewer/ccg-health-20260707-152954.json b/.ccg/dual-model-runs/20260707-152954-dual-model-review-smoke-test-20260707-after-fix-reviewer/ccg-health-20260707-152954.json
index 38bfe530..f657eb71 100644
--- a/.ccg/dual-model-runs/20260707-152954-dual-model-review-smoke-test-20260707-after-fix-reviewer/ccg-health-20260707-152954.json
+++ b/.ccg/dual-model-runs/20260707-152954-dual-model-review-smoke-test-20260707-after-fix-reviewer/ccg-health-20260707-152954.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:29:54.7956585+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-152954-dual-model-review-smoke-test-20260707-after-fix-reviewer/summary.json b/.ccg/dual-model-runs/20260707-152954-dual-model-review-smoke-test-20260707-after-fix-reviewer/summary.json
index 3a3b7005..3c2563cc 100644
--- a/.ccg/dual-model-runs/20260707-152954-dual-model-review-smoke-test-20260707-after-fix-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-152954-dual-model-review-smoke-test-20260707-after-fix-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-152954-dual-model-review-smoke-test-20260707-after-fix-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-153327-dual-model-smoke-test-20260707-ascii-quota-fix-analyzer/ccg-health-20260707-153327.json b/.ccg/dual-model-runs/20260707-153327-dual-model-smoke-test-20260707-ascii-quota-fix-analyzer/ccg-health-20260707-153327.json
index 69a1f2b9..19fb1876 100644
--- a/.ccg/dual-model-runs/20260707-153327-dual-model-smoke-test-20260707-ascii-quota-fix-analyzer/ccg-health-20260707-153327.json
+++ b/.ccg/dual-model-runs/20260707-153327-dual-model-smoke-test-20260707-ascii-quota-fix-analyzer/ccg-health-20260707-153327.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:33:27.4773578+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-153327-dual-model-smoke-test-20260707-ascii-quota-fix-analyzer/summary.json b/.ccg/dual-model-runs/20260707-153327-dual-model-smoke-test-20260707-ascii-quota-fix-analyzer/summary.json
index 8202eb07..23f6368d 100644
--- a/.ccg/dual-model-runs/20260707-153327-dual-model-smoke-test-20260707-ascii-quota-fix-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-153327-dual-model-smoke-test-20260707-ascii-quota-fix-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-153327-dual-model-smoke-test-20260707-ascii-quota-fix-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-153349-dual-model-review-smoke-test-20260707-ascii-quota-fix-reviewer/ccg-health-20260707-153350.json b/.ccg/dual-model-runs/20260707-153349-dual-model-review-smoke-test-20260707-ascii-quota-fix-reviewer/ccg-health-20260707-153350.json
index 158b34ec..8e756b52 100644
--- a/.ccg/dual-model-runs/20260707-153349-dual-model-review-smoke-test-20260707-ascii-quota-fix-reviewer/ccg-health-20260707-153350.json
+++ b/.ccg/dual-model-runs/20260707-153349-dual-model-review-smoke-test-20260707-ascii-quota-fix-reviewer/ccg-health-20260707-153350.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:33:50.0397204+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-153349-dual-model-review-smoke-test-20260707-ascii-quota-fix-reviewer/summary.json b/.ccg/dual-model-runs/20260707-153349-dual-model-review-smoke-test-20260707-ascii-quota-fix-reviewer/summary.json
index 1912f772..37acde7f 100644
--- a/.ccg/dual-model-runs/20260707-153349-dual-model-review-smoke-test-20260707-ascii-quota-fix-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-153349-dual-model-review-smoke-test-20260707-ascii-quota-fix-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-153349-dual-model-review-smoke-test-20260707-ascii-quota-fix-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-153814-dual-model-smoke-test-20260707-diagnostic-fix-analyzer/ccg-health-20260707-153815.json b/.ccg/dual-model-runs/20260707-153814-dual-model-smoke-test-20260707-diagnostic-fix-analyzer/ccg-health-20260707-153815.json
index 2ccd8c0c..26b68a7b 100644
--- a/.ccg/dual-model-runs/20260707-153814-dual-model-smoke-test-20260707-diagnostic-fix-analyzer/ccg-health-20260707-153815.json
+++ b/.ccg/dual-model-runs/20260707-153814-dual-model-smoke-test-20260707-diagnostic-fix-analyzer/ccg-health-20260707-153815.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:38:15.1616887+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-153814-dual-model-smoke-test-20260707-diagnostic-fix-analyzer/summary.json b/.ccg/dual-model-runs/20260707-153814-dual-model-smoke-test-20260707-diagnostic-fix-analyzer/summary.json
index 6d5a3121..585cfbbb 100644
--- a/.ccg/dual-model-runs/20260707-153814-dual-model-smoke-test-20260707-diagnostic-fix-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-153814-dual-model-smoke-test-20260707-diagnostic-fix-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-153814-dual-model-smoke-test-20260707-diagnostic-fix-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-153836-dual-model-review-smoke-test-20260707-diagnostic-fix-reviewer/ccg-health-20260707-153837.json b/.ccg/dual-model-runs/20260707-153836-dual-model-review-smoke-test-20260707-diagnostic-fix-reviewer/ccg-health-20260707-153837.json
index 13d570e8..ce39311f 100644
--- a/.ccg/dual-model-runs/20260707-153836-dual-model-review-smoke-test-20260707-diagnostic-fix-reviewer/ccg-health-20260707-153837.json
+++ b/.ccg/dual-model-runs/20260707-153836-dual-model-review-smoke-test-20260707-diagnostic-fix-reviewer/ccg-health-20260707-153837.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:38:36.9640118+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-153836-dual-model-review-smoke-test-20260707-diagnostic-fix-reviewer/summary.json b/.ccg/dual-model-runs/20260707-153836-dual-model-review-smoke-test-20260707-diagnostic-fix-reviewer/summary.json
index 84026c14..a2d2d40b 100644
--- a/.ccg/dual-model-runs/20260707-153836-dual-model-review-smoke-test-20260707-diagnostic-fix-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-153836-dual-model-review-smoke-test-20260707-diagnostic-fix-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-153836-dual-model-review-smoke-test-20260707-diagnostic-fix-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-154059-dual-model-smoke-test-20260707-final-diagnostics-analyzer/ccg-health-20260707-154059.json b/.ccg/dual-model-runs/20260707-154059-dual-model-smoke-test-20260707-final-diagnostics-analyzer/ccg-health-20260707-154059.json
index dc7a6d67..1b631622 100644
--- a/.ccg/dual-model-runs/20260707-154059-dual-model-smoke-test-20260707-final-diagnostics-analyzer/ccg-health-20260707-154059.json
+++ b/.ccg/dual-model-runs/20260707-154059-dual-model-smoke-test-20260707-final-diagnostics-analyzer/ccg-health-20260707-154059.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:40:59.3844294+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-154059-dual-model-smoke-test-20260707-final-diagnostics-analyzer/summary.json b/.ccg/dual-model-runs/20260707-154059-dual-model-smoke-test-20260707-final-diagnostics-analyzer/summary.json
index e083c0cd..332f4e77 100644
--- a/.ccg/dual-model-runs/20260707-154059-dual-model-smoke-test-20260707-final-diagnostics-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-154059-dual-model-smoke-test-20260707-final-diagnostics-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-154059-dual-model-smoke-test-20260707-final-diagnostics-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-154120-dual-model-review-smoke-test-20260707-final-diagnostics-reviewer/ccg-health-20260707-154120.json b/.ccg/dual-model-runs/20260707-154120-dual-model-review-smoke-test-20260707-final-diagnostics-reviewer/ccg-health-20260707-154120.json
index 5dba280a..8d9074e5 100644
--- a/.ccg/dual-model-runs/20260707-154120-dual-model-review-smoke-test-20260707-final-diagnostics-reviewer/ccg-health-20260707-154120.json
+++ b/.ccg/dual-model-runs/20260707-154120-dual-model-review-smoke-test-20260707-final-diagnostics-reviewer/ccg-health-20260707-154120.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:41:20.4023767+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-154120-dual-model-review-smoke-test-20260707-final-diagnostics-reviewer/summary.json b/.ccg/dual-model-runs/20260707-154120-dual-model-review-smoke-test-20260707-final-diagnostics-reviewer/summary.json
index 9f6b5848..adfcc604 100644
--- a/.ccg/dual-model-runs/20260707-154120-dual-model-review-smoke-test-20260707-final-diagnostics-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-154120-dual-model-review-smoke-test-20260707-final-diagnostics-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-154120-dual-model-review-smoke-test-20260707-final-diagnostics-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-154352-fix-dual-model-operation-review-reviewer/ccg-health-20260707-154352.json b/.ccg/dual-model-runs/20260707-154352-fix-dual-model-operation-review-reviewer/ccg-health-20260707-154352.json
index 2db538fb..5fdf313b 100644
--- a/.ccg/dual-model-runs/20260707-154352-fix-dual-model-operation-review-reviewer/ccg-health-20260707-154352.json
+++ b/.ccg/dual-model-runs/20260707-154352-fix-dual-model-operation-review-reviewer/ccg-health-20260707-154352.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:43:52.6239502+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-154352-fix-dual-model-operation-review-reviewer/summary.json b/.ccg/dual-model-runs/20260707-154352-fix-dual-model-operation-review-reviewer/summary.json
index 09a64e04..61635a13 100644
--- a/.ccg/dual-model-runs/20260707-154352-fix-dual-model-operation-review-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-154352-fix-dual-model-operation-review-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-154352-fix-dual-model-operation-review-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-155056-dual-model-smoke-test-20260707-narrow-quota-fix-analyzer/ccg-health-20260707-155056.json b/.ccg/dual-model-runs/20260707-155056-dual-model-smoke-test-20260707-narrow-quota-fix-analyzer/ccg-health-20260707-155056.json
index 19b726b2..1bccf038 100644
--- a/.ccg/dual-model-runs/20260707-155056-dual-model-smoke-test-20260707-narrow-quota-fix-analyzer/ccg-health-20260707-155056.json
+++ b/.ccg/dual-model-runs/20260707-155056-dual-model-smoke-test-20260707-narrow-quota-fix-analyzer/ccg-health-20260707-155056.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:50:56.8321299+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-155056-dual-model-smoke-test-20260707-narrow-quota-fix-analyzer/summary.json b/.ccg/dual-model-runs/20260707-155056-dual-model-smoke-test-20260707-narrow-quota-fix-analyzer/summary.json
index 9cd38f34..77ff511a 100644
--- a/.ccg/dual-model-runs/20260707-155056-dual-model-smoke-test-20260707-narrow-quota-fix-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-155056-dual-model-smoke-test-20260707-narrow-quota-fix-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-155056-dual-model-smoke-test-20260707-narrow-quota-fix-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-155116-dual-model-review-smoke-test-20260707-narrow-quota-fix-reviewer/ccg-health-20260707-155117.json b/.ccg/dual-model-runs/20260707-155116-dual-model-review-smoke-test-20260707-narrow-quota-fix-reviewer/ccg-health-20260707-155117.json
index 9017b6c2..aa4ff993 100644
--- a/.ccg/dual-model-runs/20260707-155116-dual-model-review-smoke-test-20260707-narrow-quota-fix-reviewer/ccg-health-20260707-155117.json
+++ b/.ccg/dual-model-runs/20260707-155116-dual-model-review-smoke-test-20260707-narrow-quota-fix-reviewer/ccg-health-20260707-155117.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:51:17.1446808+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-155116-dual-model-review-smoke-test-20260707-narrow-quota-fix-reviewer/summary.json b/.ccg/dual-model-runs/20260707-155116-dual-model-review-smoke-test-20260707-narrow-quota-fix-reviewer/summary.json
index 9a4a3157..26f52da7 100644
--- a/.ccg/dual-model-runs/20260707-155116-dual-model-review-smoke-test-20260707-narrow-quota-fix-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-155116-dual-model-review-smoke-test-20260707-narrow-quota-fix-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-155116-dual-model-review-smoke-test-20260707-narrow-quota-fix-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-155307-fix-dual-model-operation-postfix-review-reviewer/ccg-health-20260707-155307.json b/.ccg/dual-model-runs/20260707-155307-fix-dual-model-operation-postfix-review-reviewer/ccg-health-20260707-155307.json
index 4bd32b16..0dd2e806 100644
--- a/.ccg/dual-model-runs/20260707-155307-fix-dual-model-operation-postfix-review-reviewer/ccg-health-20260707-155307.json
+++ b/.ccg/dual-model-runs/20260707-155307-fix-dual-model-operation-postfix-review-reviewer/ccg-health-20260707-155307.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:53:07.4023051+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-155307-fix-dual-model-operation-postfix-review-reviewer/summary.json b/.ccg/dual-model-runs/20260707-155307-fix-dual-model-operation-postfix-review-reviewer/summary.json
index 1fc8e6c0..483b11d9 100644
--- a/.ccg/dual-model-runs/20260707-155307-fix-dual-model-operation-postfix-review-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-155307-fix-dual-model-operation-postfix-review-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-155307-fix-dual-model-operation-postfix-review-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-160636-dual-model-smoke-test-20260707-review-fix-analyzer/ccg-health-20260707-160636.json b/.ccg/dual-model-runs/20260707-160636-dual-model-smoke-test-20260707-review-fix-analyzer/ccg-health-20260707-160636.json
index 9aed10c8..8e20b175 100644
--- a/.ccg/dual-model-runs/20260707-160636-dual-model-smoke-test-20260707-review-fix-analyzer/ccg-health-20260707-160636.json
+++ b/.ccg/dual-model-runs/20260707-160636-dual-model-smoke-test-20260707-review-fix-analyzer/ccg-health-20260707-160636.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:06:36.6612494+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-160636-dual-model-smoke-test-20260707-review-fix-analyzer/summary.json b/.ccg/dual-model-runs/20260707-160636-dual-model-smoke-test-20260707-review-fix-analyzer/summary.json
index c3682e4b..69c6ccd6 100644
--- a/.ccg/dual-model-runs/20260707-160636-dual-model-smoke-test-20260707-review-fix-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-160636-dual-model-smoke-test-20260707-review-fix-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-160636-dual-model-smoke-test-20260707-review-fix-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-160709-dual-model-review-smoke-test-20260707-review-fix-reviewer/ccg-health-20260707-160710.json b/.ccg/dual-model-runs/20260707-160709-dual-model-review-smoke-test-20260707-review-fix-reviewer/ccg-health-20260707-160710.json
index e546ddc8..108230b8 100644
--- a/.ccg/dual-model-runs/20260707-160709-dual-model-review-smoke-test-20260707-review-fix-reviewer/ccg-health-20260707-160710.json
+++ b/.ccg/dual-model-runs/20260707-160709-dual-model-review-smoke-test-20260707-review-fix-reviewer/ccg-health-20260707-160710.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:07:10.0971151+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-160709-dual-model-review-smoke-test-20260707-review-fix-reviewer/summary.json b/.ccg/dual-model-runs/20260707-160709-dual-model-review-smoke-test-20260707-review-fix-reviewer/summary.json
index 9efa823f..69464798 100644
--- a/.ccg/dual-model-runs/20260707-160709-dual-model-review-smoke-test-20260707-review-fix-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-160709-dual-model-review-smoke-test-20260707-review-fix-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-160709-dual-model-review-smoke-test-20260707-review-fix-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-160817-fix-dual-model-operation-final-review-reviewer/ccg-health-20260707-160818.json b/.ccg/dual-model-runs/20260707-160817-fix-dual-model-operation-final-review-reviewer/ccg-health-20260707-160818.json
index bd9c684e..81441480 100644
--- a/.ccg/dual-model-runs/20260707-160817-fix-dual-model-operation-final-review-reviewer/ccg-health-20260707-160818.json
+++ b/.ccg/dual-model-runs/20260707-160817-fix-dual-model-operation-final-review-reviewer/ccg-health-20260707-160818.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:08:17.9792302+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-160817-fix-dual-model-operation-final-review-reviewer/summary.json b/.ccg/dual-model-runs/20260707-160817-fix-dual-model-operation-final-review-reviewer/summary.json
index e07de673..ae8b3a58 100644
--- a/.ccg/dual-model-runs/20260707-160817-fix-dual-model-operation-final-review-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-160817-fix-dual-model-operation-final-review-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-160817-fix-dual-model-operation-final-review-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-161402-dual-model-smoke-test-20260707-warning-fix-analyzer/ccg-health-20260707-161402.json b/.ccg/dual-model-runs/20260707-161402-dual-model-smoke-test-20260707-warning-fix-analyzer/ccg-health-20260707-161402.json
index 248f4ef2..78aafee4 100644
--- a/.ccg/dual-model-runs/20260707-161402-dual-model-smoke-test-20260707-warning-fix-analyzer/ccg-health-20260707-161402.json
+++ b/.ccg/dual-model-runs/20260707-161402-dual-model-smoke-test-20260707-warning-fix-analyzer/ccg-health-20260707-161402.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:14:02.3260550+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-161402-dual-model-smoke-test-20260707-warning-fix-analyzer/summary.json b/.ccg/dual-model-runs/20260707-161402-dual-model-smoke-test-20260707-warning-fix-analyzer/summary.json
index 14c8a744..e10b3d48 100644
--- a/.ccg/dual-model-runs/20260707-161402-dual-model-smoke-test-20260707-warning-fix-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-161402-dual-model-smoke-test-20260707-warning-fix-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-161402-dual-model-smoke-test-20260707-warning-fix-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-161423-dual-model-review-smoke-test-20260707-warning-fix-reviewer/ccg-health-20260707-161423.json b/.ccg/dual-model-runs/20260707-161423-dual-model-review-smoke-test-20260707-warning-fix-reviewer/ccg-health-20260707-161423.json
index 1f747d67..2e2db308 100644
--- a/.ccg/dual-model-runs/20260707-161423-dual-model-review-smoke-test-20260707-warning-fix-reviewer/ccg-health-20260707-161423.json
+++ b/.ccg/dual-model-runs/20260707-161423-dual-model-review-smoke-test-20260707-warning-fix-reviewer/ccg-health-20260707-161423.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:14:23.6320289+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-161423-dual-model-review-smoke-test-20260707-warning-fix-reviewer/summary.json b/.ccg/dual-model-runs/20260707-161423-dual-model-review-smoke-test-20260707-warning-fix-reviewer/summary.json
index 95030873..1f69f86f 100644
--- a/.ccg/dual-model-runs/20260707-161423-dual-model-review-smoke-test-20260707-warning-fix-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-161423-dual-model-review-smoke-test-20260707-warning-fix-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-161423-dual-model-review-smoke-test-20260707-warning-fix-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-161524-fix-dual-model-operation-final-warning-review-reviewer/ccg-health-20260707-161525.json b/.ccg/dual-model-runs/20260707-161524-fix-dual-model-operation-final-warning-review-reviewer/ccg-health-20260707-161525.json
index 6b8c4f22..6340c95d 100644
--- a/.ccg/dual-model-runs/20260707-161524-fix-dual-model-operation-final-warning-review-reviewer/ccg-health-20260707-161525.json
+++ b/.ccg/dual-model-runs/20260707-161524-fix-dual-model-operation-final-warning-review-reviewer/ccg-health-20260707-161525.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:15:25.2243851+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-161524-fix-dual-model-operation-final-warning-review-reviewer/summary.json b/.ccg/dual-model-runs/20260707-161524-fix-dual-model-operation-final-warning-review-reviewer/summary.json
index 4c33deb1..d7a42c8f 100644
--- a/.ccg/dual-model-runs/20260707-161524-fix-dual-model-operation-final-warning-review-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-161524-fix-dual-model-operation-final-warning-review-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-161524-fix-dual-model-operation-final-warning-review-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-162113-dual-model-smoke-test-20260707-final-warning-patch-analyzer/ccg-health-20260707-162114.json b/.ccg/dual-model-runs/20260707-162113-dual-model-smoke-test-20260707-final-warning-patch-analyzer/ccg-health-20260707-162114.json
index 627cf30b..8c1f2430 100644
--- a/.ccg/dual-model-runs/20260707-162113-dual-model-smoke-test-20260707-final-warning-patch-analyzer/ccg-health-20260707-162114.json
+++ b/.ccg/dual-model-runs/20260707-162113-dual-model-smoke-test-20260707-final-warning-patch-analyzer/ccg-health-20260707-162114.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:21:14.2017580+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-162113-dual-model-smoke-test-20260707-final-warning-patch-analyzer/summary.json b/.ccg/dual-model-runs/20260707-162113-dual-model-smoke-test-20260707-final-warning-patch-analyzer/summary.json
index bac51592..69320a2c 100644
--- a/.ccg/dual-model-runs/20260707-162113-dual-model-smoke-test-20260707-final-warning-patch-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-162113-dual-model-smoke-test-20260707-final-warning-patch-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-162113-dual-model-smoke-test-20260707-final-warning-patch-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-162124-dual-model-review-smoke-test-20260707-final-warning-patch-reviewer/ccg-health-20260707-162124.json b/.ccg/dual-model-runs/20260707-162124-dual-model-review-smoke-test-20260707-final-warning-patch-reviewer/ccg-health-20260707-162124.json
index b746e658..8fd1205b 100644
--- a/.ccg/dual-model-runs/20260707-162124-dual-model-review-smoke-test-20260707-final-warning-patch-reviewer/ccg-health-20260707-162124.json
+++ b/.ccg/dual-model-runs/20260707-162124-dual-model-review-smoke-test-20260707-final-warning-patch-reviewer/ccg-health-20260707-162124.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:21:24.2963751+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-162124-dual-model-review-smoke-test-20260707-final-warning-patch-reviewer/summary.json b/.ccg/dual-model-runs/20260707-162124-dual-model-review-smoke-test-20260707-final-warning-patch-reviewer/summary.json
index bef7715a..c21e258b 100644
--- a/.ccg/dual-model-runs/20260707-162124-dual-model-review-smoke-test-20260707-final-warning-patch-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-162124-dual-model-review-smoke-test-20260707-final-warning-patch-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-162124-dual-model-review-smoke-test-20260707-final-warning-patch-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-163052-dual-model-smoke-test-20260707-claude-shim-analyzer/ccg-health-20260707-163053.json b/.ccg/dual-model-runs/20260707-163052-dual-model-smoke-test-20260707-claude-shim-analyzer/ccg-health-20260707-163053.json
index e959120d..f29596e4 100644
--- a/.ccg/dual-model-runs/20260707-163052-dual-model-smoke-test-20260707-claude-shim-analyzer/ccg-health-20260707-163053.json
+++ b/.ccg/dual-model-runs/20260707-163052-dual-model-smoke-test-20260707-claude-shim-analyzer/ccg-health-20260707-163053.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:30:52.9735840+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-163052-dual-model-smoke-test-20260707-claude-shim-analyzer/summary.json b/.ccg/dual-model-runs/20260707-163052-dual-model-smoke-test-20260707-claude-shim-analyzer/summary.json
index 5a67c8b5..555275fa 100644
--- a/.ccg/dual-model-runs/20260707-163052-dual-model-smoke-test-20260707-claude-shim-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-163052-dual-model-smoke-test-20260707-claude-shim-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-163052-dual-model-smoke-test-20260707-claude-shim-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-163118-dual-model-review-smoke-test-20260707-claude-shim-reviewer/ccg-health-20260707-163118.json b/.ccg/dual-model-runs/20260707-163118-dual-model-review-smoke-test-20260707-claude-shim-reviewer/ccg-health-20260707-163118.json
index 8a06ff84..ed69f1e5 100644
--- a/.ccg/dual-model-runs/20260707-163118-dual-model-review-smoke-test-20260707-claude-shim-reviewer/ccg-health-20260707-163118.json
+++ b/.ccg/dual-model-runs/20260707-163118-dual-model-review-smoke-test-20260707-claude-shim-reviewer/ccg-health-20260707-163118.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:31:18.6070542+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-163118-dual-model-review-smoke-test-20260707-claude-shim-reviewer/summary.json b/.ccg/dual-model-runs/20260707-163118-dual-model-review-smoke-test-20260707-claude-shim-reviewer/summary.json
index 3ccca894..8602a8c7 100644
--- a/.ccg/dual-model-runs/20260707-163118-dual-model-review-smoke-test-20260707-claude-shim-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-163118-dual-model-review-smoke-test-20260707-claude-shim-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-163118-dual-model-review-smoke-test-20260707-claude-shim-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-163150-fix-dual-model-operation-claude-shim-review-reviewer/ccg-health-20260707-163150.json b/.ccg/dual-model-runs/20260707-163150-fix-dual-model-operation-claude-shim-review-reviewer/ccg-health-20260707-163150.json
index 38687cdf..b7f429f3 100644
--- a/.ccg/dual-model-runs/20260707-163150-fix-dual-model-operation-claude-shim-review-reviewer/ccg-health-20260707-163150.json
+++ b/.ccg/dual-model-runs/20260707-163150-fix-dual-model-operation-claude-shim-review-reviewer/ccg-health-20260707-163150.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:31:50.5179018+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-163150-fix-dual-model-operation-claude-shim-review-reviewer/summary.json b/.ccg/dual-model-runs/20260707-163150-fix-dual-model-operation-claude-shim-review-reviewer/summary.json
index aebc8f85..81d96d23 100644
--- a/.ccg/dual-model-runs/20260707-163150-fix-dual-model-operation-claude-shim-review-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-163150-fix-dual-model-operation-claude-shim-review-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-163150-fix-dual-model-operation-claude-shim-review-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-163549-dual-model-smoke-test-20260707-final-analyzer/ccg-health-20260707-163549.json b/.ccg/dual-model-runs/20260707-163549-dual-model-smoke-test-20260707-final-analyzer/ccg-health-20260707-163549.json
index c1f6052b..00beeded 100644
--- a/.ccg/dual-model-runs/20260707-163549-dual-model-smoke-test-20260707-final-analyzer/ccg-health-20260707-163549.json
+++ b/.ccg/dual-model-runs/20260707-163549-dual-model-smoke-test-20260707-final-analyzer/ccg-health-20260707-163549.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:35:49.7958140+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-163549-dual-model-smoke-test-20260707-final-analyzer/summary.json b/.ccg/dual-model-runs/20260707-163549-dual-model-smoke-test-20260707-final-analyzer/summary.json
index d73761de..a77b093d 100644
--- a/.ccg/dual-model-runs/20260707-163549-dual-model-smoke-test-20260707-final-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-163549-dual-model-smoke-test-20260707-final-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-163549-dual-model-smoke-test-20260707-final-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-163611-dual-model-review-smoke-test-20260707-final-reviewer/ccg-health-20260707-163612.json b/.ccg/dual-model-runs/20260707-163611-dual-model-review-smoke-test-20260707-final-reviewer/ccg-health-20260707-163612.json
index 35113420..58bad90b 100644
--- a/.ccg/dual-model-runs/20260707-163611-dual-model-review-smoke-test-20260707-final-reviewer/ccg-health-20260707-163612.json
+++ b/.ccg/dual-model-runs/20260707-163611-dual-model-review-smoke-test-20260707-final-reviewer/ccg-health-20260707-163612.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:36:12.1087413+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-163611-dual-model-review-smoke-test-20260707-final-reviewer/summary.json b/.ccg/dual-model-runs/20260707-163611-dual-model-review-smoke-test-20260707-final-reviewer/summary.json
index 202a9cfb..39bf6f31 100644
--- a/.ccg/dual-model-runs/20260707-163611-dual-model-review-smoke-test-20260707-final-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-163611-dual-model-review-smoke-test-20260707-final-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-163611-dual-model-review-smoke-test-20260707-final-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-201908-dual-model-smoke-test-20260707-after-claude-reset-analyzer/ccg-health-20260707-201908.json b/.ccg/dual-model-runs/20260707-201908-dual-model-smoke-test-20260707-after-claude-reset-analyzer/ccg-health-20260707-201908.json
index c7bb972d..a537f70d 100644
--- a/.ccg/dual-model-runs/20260707-201908-dual-model-smoke-test-20260707-after-claude-reset-analyzer/ccg-health-20260707-201908.json
+++ b/.ccg/dual-model-runs/20260707-201908-dual-model-smoke-test-20260707-after-claude-reset-analyzer/ccg-health-20260707-201908.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T20:19:08.8864193+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-201908-dual-model-smoke-test-20260707-after-claude-reset-analyzer/summary.json b/.ccg/dual-model-runs/20260707-201908-dual-model-smoke-test-20260707-after-claude-reset-analyzer/summary.json
index a72a5643..e70332de 100644
--- a/.ccg/dual-model-runs/20260707-201908-dual-model-smoke-test-20260707-after-claude-reset-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-201908-dual-model-smoke-test-20260707-after-claude-reset-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-201908-dual-model-smoke-test-20260707-after-claude-reset-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-202000-dual-model-review-smoke-test-20260707-after-claude-reset-reviewer/ccg-health-20260707-202000.json b/.ccg/dual-model-runs/20260707-202000-dual-model-review-smoke-test-20260707-after-claude-reset-reviewer/ccg-health-20260707-202000.json
index 5a6ddbbe..b128291e 100644
--- a/.ccg/dual-model-runs/20260707-202000-dual-model-review-smoke-test-20260707-after-claude-reset-reviewer/ccg-health-20260707-202000.json
+++ b/.ccg/dual-model-runs/20260707-202000-dual-model-review-smoke-test-20260707-after-claude-reset-reviewer/ccg-health-20260707-202000.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T20:20:00.5323807+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-202000-dual-model-review-smoke-test-20260707-after-claude-reset-reviewer/summary.json b/.ccg/dual-model-runs/20260707-202000-dual-model-review-smoke-test-20260707-after-claude-reset-reviewer/summary.json
index b139aba9..d29d33e5 100644
--- a/.ccg/dual-model-runs/20260707-202000-dual-model-review-smoke-test-20260707-after-claude-reset-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-202000-dual-model-review-smoke-test-20260707-after-claude-reset-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-202000-dual-model-review-smoke-test-20260707-after-claude-reset-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-202042-fix-dual-model-operation-after-claude-reset-review-reviewer/ccg-health-20260707-202043.json b/.ccg/dual-model-runs/20260707-202042-fix-dual-model-operation-after-claude-reset-review-reviewer/ccg-health-20260707-202043.json
index 4e5650d6..ba85795f 100644
--- a/.ccg/dual-model-runs/20260707-202042-fix-dual-model-operation-after-claude-reset-review-reviewer/ccg-health-20260707-202043.json
+++ b/.ccg/dual-model-runs/20260707-202042-fix-dual-model-operation-after-claude-reset-review-reviewer/ccg-health-20260707-202043.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T20:20:42.9431769+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-202042-fix-dual-model-operation-after-claude-reset-review-reviewer/summary.json b/.ccg/dual-model-runs/20260707-202042-fix-dual-model-operation-after-claude-reset-review-reviewer/summary.json
index d540f7f9..0609043d 100644
--- a/.ccg/dual-model-runs/20260707-202042-fix-dual-model-operation-after-claude-reset-review-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-202042-fix-dual-model-operation-after-claude-reset-review-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-202042-fix-dual-model-operation-after-claude-reset-review-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-203350-dual-model-smoke-test-20260707-post-shim-critical-fix-analyzer/ccg-health-20260707-203350.json b/.ccg/dual-model-runs/20260707-203350-dual-model-smoke-test-20260707-post-shim-critical-fix-analyzer/ccg-health-20260707-203350.json
index b128a7d9..1d5593a6 100644
--- a/.ccg/dual-model-runs/20260707-203350-dual-model-smoke-test-20260707-post-shim-critical-fix-analyzer/ccg-health-20260707-203350.json
+++ b/.ccg/dual-model-runs/20260707-203350-dual-model-smoke-test-20260707-post-shim-critical-fix-analyzer/ccg-health-20260707-203350.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T20:33:50.8978189+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-203350-dual-model-smoke-test-20260707-post-shim-critical-fix-analyzer/summary.json b/.ccg/dual-model-runs/20260707-203350-dual-model-smoke-test-20260707-post-shim-critical-fix-analyzer/summary.json
index 7153531b..9cd84184 100644
--- a/.ccg/dual-model-runs/20260707-203350-dual-model-smoke-test-20260707-post-shim-critical-fix-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260707-203350-dual-model-smoke-test-20260707-post-shim-critical-fix-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-203350-dual-model-smoke-test-20260707-post-shim-critical-fix-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260707-203504-dual-model-review-smoke-test-20260707-post-shim-critical-fix-reviewer/ccg-health-20260707-203504.json b/.ccg/dual-model-runs/20260707-203504-dual-model-review-smoke-test-20260707-post-shim-critical-fix-reviewer/ccg-health-20260707-203504.json
index d2fe7d62..d234f8e3 100644
--- a/.ccg/dual-model-runs/20260707-203504-dual-model-review-smoke-test-20260707-post-shim-critical-fix-reviewer/ccg-health-20260707-203504.json
+++ b/.ccg/dual-model-runs/20260707-203504-dual-model-review-smoke-test-20260707-post-shim-critical-fix-reviewer/ccg-health-20260707-203504.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T20:35:04.8956751+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260707-203504-dual-model-review-smoke-test-20260707-post-shim-critical-fix-reviewer/summary.json b/.ccg/dual-model-runs/20260707-203504-dual-model-review-smoke-test-20260707-post-shim-critical-fix-reviewer/summary.json
index f042d55d..4550740e 100644
--- a/.ccg/dual-model-runs/20260707-203504-dual-model-review-smoke-test-20260707-post-shim-critical-fix-reviewer/summary.json
+++ b/.ccg/dual-model-runs/20260707-203504-dual-model-review-smoke-test-20260707-post-shim-critical-fix-reviewer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260707-203504-dual-model-review-smoke-test-20260707-post-shim-critical-fix-reviewer",
     "role":  "reviewer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
diff --git a/.ccg/dual-model-runs/20260710-111946-full-code-quality-audit-and-fix-analysis-analyzer/ccg-health-20260710-111947.json b/.ccg/dual-model-runs/20260710-111946-full-code-quality-audit-and-fix-analysis-analyzer/ccg-health-20260710-111947.json
index 4e0c13c0..1e5dbba2 100644
--- a/.ccg/dual-model-runs/20260710-111946-full-code-quality-audit-and-fix-analysis-analyzer/ccg-health-20260710-111947.json
+++ b/.ccg/dual-model-runs/20260710-111946-full-code-quality-audit-and-fix-analysis-analyzer/ccg-health-20260710-111947.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-10T11:19:47.1313669+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts\\.worktrees\\1.0.0.0.Initialization.Worktree",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/20260710-111946-full-code-quality-audit-and-fix-analysis-analyzer/summary.json b/.ccg/dual-model-runs/20260710-111946-full-code-quality-audit-and-fix-analysis-analyzer/summary.json
index 1cfb282d..bf0ab29c 100644
--- a/.ccg/dual-model-runs/20260710-111946-full-code-quality-audit-and-fix-analysis-analyzer/summary.json
+++ b/.ccg/dual-model-runs/20260710-111946-full-code-quality-audit-and-fix-analysis-analyzer/summary.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "runId":  "20260710-111946-full-code-quality-audit-and-fix-analysis-analyzer",
     "role":  "analyzer",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts\\.worktrees\\1.0.0.0.Initialization.Worktree",
diff --git a/.ccg/dual-model-runs/annotate-richmenu-cs-files-analysis-input.md b/.ccg/dual-model-runs/annotate-richmenu-cs-files-analysis-input.md
index 4758efe1..b3428ee3 100644
--- a/.ccg/dual-model-runs/annotate-richmenu-cs-files-analysis-input.md
+++ b/.ccg/dual-model-runs/annotate-richmenu-cs-files-analysis-input.md
@@ -1,4 +1,4 @@
-﻿We need to add detailed, complete, maintainability-focused comments to all RichMenu-related C# files in this repository.
+We need to add detailed, complete, maintainability-focused comments to all RichMenu-related C# files in this repository.
 
 This is a documentation-only change. Please analyze the scope and provide guidance before implementation.
 
diff --git a/.ccg/dual-model-runs/ccg-fallback-policy-verification-review.md b/.ccg/dual-model-runs/ccg-fallback-policy-verification-review.md
index ba98b672..c3a1a270 100644
--- a/.ccg/dual-model-runs/ccg-fallback-policy-verification-review.md
+++ b/.ccg/dual-model-runs/ccg-fallback-policy-verification-review.md
@@ -1,4 +1,4 @@
-﻿# CCG Fallback Policy Verification
+# CCG Fallback Policy Verification
 
 請以 reviewer 角色回覆一個很短的 review，用 Critical / Warning / Info 三段輸出即可。
 
diff --git a/.ccg/dual-model-runs/ccg-health-20260704-150503.json b/.ccg/dual-model-runs/ccg-health-20260704-150503.json
index 778ff487..6a900b9b 100644
--- a/.ccg/dual-model-runs/ccg-health-20260704-150503.json
+++ b/.ccg/dual-model-runs/ccg-health-20260704-150503.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T15:05:03.8474010+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260704-151542.json b/.ccg/dual-model-runs/ccg-health-20260704-151542.json
index 6540ea33..addecf7f 100644
--- a/.ccg/dual-model-runs/ccg-health-20260704-151542.json
+++ b/.ccg/dual-model-runs/ccg-health-20260704-151542.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T15:15:42.1122395+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260704-152713.json b/.ccg/dual-model-runs/ccg-health-20260704-152713.json
index c268e69a..3414edf8 100644
--- a/.ccg/dual-model-runs/ccg-health-20260704-152713.json
+++ b/.ccg/dual-model-runs/ccg-health-20260704-152713.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T15:26:59.5688813+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260704-153310.json b/.ccg/dual-model-runs/ccg-health-20260704-153310.json
index c117afca..69835a81 100644
--- a/.ccg/dual-model-runs/ccg-health-20260704-153310.json
+++ b/.ccg/dual-model-runs/ccg-health-20260704-153310.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T15:33:10.7698187+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260704-192026.json b/.ccg/dual-model-runs/ccg-health-20260704-192026.json
index 7ad71c9e..27eb576e 100644
--- a/.ccg/dual-model-runs/ccg-health-20260704-192026.json
+++ b/.ccg/dual-model-runs/ccg-health-20260704-192026.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T19:20:26.2791821+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-152917.json b/.ccg/dual-model-runs/ccg-health-20260707-152917.json
index 8edf70aa..aef984ff 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-152917.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-152917.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:29:03.1489101+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-153326.json b/.ccg/dual-model-runs/ccg-health-20260707-153326.json
index c71eb701..39acca68 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-153326.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-153326.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:33:16.9281370+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-153814.json b/.ccg/dual-model-runs/ccg-health-20260707-153814.json
index 3cfd4f10..417d70be 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-153814.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-153814.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:38:06.0233710+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-154058.json b/.ccg/dual-model-runs/ccg-health-20260707-154058.json
index a48144f0..f4d539b2 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-154058.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-154058.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:40:49.0349325+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-155055.json b/.ccg/dual-model-runs/ccg-health-20260707-155055.json
index 05a024be..4f382f97 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-155055.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-155055.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T15:50:44.8378470+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-160635.json b/.ccg/dual-model-runs/ccg-health-20260707-160635.json
index 12d17b4c..c1999635 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-160635.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-160635.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:05:57.0399152+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-161401.json b/.ccg/dual-model-runs/ccg-health-20260707-161401.json
index 49db7fcb..fa91a930 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-161401.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-161401.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:13:51.8100889+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-162113.json b/.ccg/dual-model-runs/ccg-health-20260707-162113.json
index 44bc72f1..7f06c5d0 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-162113.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-162113.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:21:03.3701016+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-162409.json b/.ccg/dual-model-runs/ccg-health-20260707-162409.json
index bb3b368b..cd27b2f7 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-162409.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-162409.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:23:59.5663548+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-163038.json b/.ccg/dual-model-runs/ccg-health-20260707-163038.json
index a19d83ce..481bddee 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-163038.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-163038.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:30:27.3702436+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-163537.json b/.ccg/dual-model-runs/ccg-health-20260707-163537.json
index a5f9a68e..4bf11665 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-163537.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-163537.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:35:25.8702284+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-164141.json b/.ccg/dual-model-runs/ccg-health-20260707-164141.json
index bf8551f9..5b858e62 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-164141.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-164141.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:41:30.2873753+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-164412.json b/.ccg/dual-model-runs/ccg-health-20260707-164412.json
index c51b8bdf..b8edb9bc 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-164412.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-164412.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T16:44:02.7367095+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-201846.json b/.ccg/dual-model-runs/ccg-health-20260707-201846.json
index 005235e1..ad2b9c0f 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-201846.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-201846.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T20:18:36.0704563+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-203331.json b/.ccg/dual-model-runs/ccg-health-20260707-203331.json
index d6f5acf2..4417cc52 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-203331.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-203331.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T20:33:20.2939851+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-203503.json b/.ccg/dual-model-runs/ccg-health-20260707-203503.json
index 4a71ea8c..eb1a8fcd 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-203503.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-203503.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T20:34:52.4493399+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260707-204130.json b/.ccg/dual-model-runs/ccg-health-20260707-204130.json
index d4eb66f9..8fffe534 100644
--- a/.ccg/dual-model-runs/ccg-health-20260707-204130.json
+++ b/.ccg/dual-model-runs/ccg-health-20260707-204130.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-07T20:41:16.5681132+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260709-145236.json b/.ccg/dual-model-runs/ccg-health-20260709-145236.json
index 764c0936..e57d91a6 100644
--- a/.ccg/dual-model-runs/ccg-health-20260709-145236.json
+++ b/.ccg/dual-model-runs/ccg-health-20260709-145236.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-09T14:52:35.7014588+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-health-20260709-145306.json b/.ccg/dual-model-runs/ccg-health-20260709-145306.json
index 4fb8b698..d1133f3a 100644
--- a/.ccg/dual-model-runs/ccg-health-20260709-145306.json
+++ b/.ccg/dual-model-runs/ccg-health-20260709-145306.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-09T14:52:53.2034237+08:00",
     "repositoryPath":  "D:\\音訊科技產品\\系統平台\\SpeechMessageProducts",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/ccg-self-healing-formal-review.md b/.ccg/dual-model-runs/ccg-self-healing-formal-review.md
index d9dbc543..b1e48964 100644
--- a/.ccg/dual-model-runs/ccg-self-healing-formal-review.md
+++ b/.ccg/dual-model-runs/ccg-self-healing-formal-review.md
@@ -1,4 +1,4 @@
-﻿請以 reviewer 角色審查這次 CCG Gemini + Claude 雙模型自我修復流程的變更。
+請以 reviewer 角色審查這次 CCG Gemini + Claude 雙模型自我修復流程的變更。
 
 重點：
 1. 確認 Invoke-CcgDualModelWithSelfHealing.ps1 是否能在同一個 process 內修 PATH/env 後繼續執行。
diff --git a/.ccg/dual-model-runs/health-final-check/ccg-health-20260704-145350.json b/.ccg/dual-model-runs/health-final-check/ccg-health-20260704-145350.json
index 339dd507..af689647 100644
--- a/.ccg/dual-model-runs/health-final-check/ccg-health-20260704-145350.json
+++ b/.ccg/dual-model-runs/health-final-check/ccg-health-20260704-145350.json
@@ -1,4 +1,4 @@
-﻿{
+{
     "generatedAt":  "2026-07-04T14:53:37.3504516+08:00",
     "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
     "changedUserPath":  false,
diff --git a/.ccg/dual-model-runs/line-richmenu-word-manual-analysis-input.md b/.ccg/dual-model-runs/line-richmenu-word-manual-analysis-input.md
index b68d754f..ddf92054 100644
--- a/.ccg/dual-model-runs/line-richmenu-word-manual-analysis-input.md
+++ b/.ccg/dual-model-runs/line-richmenu-word-manual-analysis-input.md
@@ -1,4 +1,4 @@
-﻿# LINE RichMenu Word Manual Analysis Request
+# LINE RichMenu Word Manual Analysis Request
 
 請協助審視本次文件交付應涵蓋的內容。
 
diff --git a/.ccg/dual-model-runs/rename-main-project-to-speechmessageproducts-churchreport-analysis-input.md b/.ccg/dual-model-runs/rename-main-project-to-speechmessageproducts-churchreport-analysis-input.md
index 97ae4c04..6e98bd37 100644
--- a/.ccg/dual-model-runs/rename-main-project-to-speechmessageproducts-churchreport-analysis-input.md
+++ b/.ccg/dual-model-runs/rename-main-project-to-speechmessageproducts-churchreport-analysis-input.md
@@ -1,4 +1,4 @@
-﻿# Task
+# Task
 Analyze a scoped project rename in the SpeechMessageProducts repository.
 
 # User request
diff --git a/.ccg/dual-model-runs/richmenu-assignment-final-review-after-boundary-fix.md b/.ccg/dual-model-runs/richmenu-assignment-final-review-after-boundary-fix.md
index 6c9151e5..461baed7 100644
--- a/.ccg/dual-model-runs/richmenu-assignment-final-review-after-boundary-fix.md
+++ b/.ccg/dual-model-runs/richmenu-assignment-final-review-after-boundary-fix.md
@@ -1,4 +1,4 @@
-﻿# RichMenu Assignment Final Code Review After Boundary Fix
+# RichMenu Assignment Final Code Review After Boundary Fix
 
 請以 reviewer 角色審查目前 git diff，重點檢查：
 
diff --git a/.ccg/dual-model-runs/richmenu-assignment-final-review-after-timeout-fix.md b/.ccg/dual-model-runs/richmenu-assignment-final-review-after-timeout-fix.md
index 62477199..1e0da1eb 100644
--- a/.ccg/dual-model-runs/richmenu-assignment-final-review-after-timeout-fix.md
+++ b/.ccg/dual-model-runs/richmenu-assignment-final-review-after-timeout-fix.md
@@ -1,4 +1,4 @@
-﻿# RichMenu Assignment Final Code Review After Timeout Fix
+# RichMenu Assignment Final Code Review After Timeout Fix
 
 請以 reviewer 角色審查目前 git diff，重點檢查：
 
diff --git a/.ccg/dual-model-runs/self-healing-smoke-review-v2.md b/.ccg/dual-model-runs/self-healing-smoke-review-v2.md
index d9fe3c39..165479ff 100644
--- a/.ccg/dual-model-runs/self-healing-smoke-review-v2.md
+++ b/.ccg/dual-model-runs/self-healing-smoke-review-v2.md
@@ -1 +1 @@
-﻿請做最小 reviewer smoke test。請輸出三個標題：Critical、Warning、Info，並在 Info 寫 CCG_SELF_HEALING_SMOKE_OK。
+請做最小 reviewer smoke test。請輸出三個標題：Critical、Warning、Info，並在 Info 寫 CCG_SELF_HEALING_SMOKE_OK。
diff --git a/.ccg/dual-model-runs/self-healing-smoke-review.md b/.ccg/dual-model-runs/self-healing-smoke-review.md
index 58366844..3dfef1e8 100644
--- a/.ccg/dual-model-runs/self-healing-smoke-review.md
+++ b/.ccg/dual-model-runs/self-healing-smoke-review.md
@@ -1 +1 @@
-﻿請做最小 smoke review。只需要確認你收到任務，並回覆一行：CCG_SELF_HEALING_SMOKE_OK。
+請做最小 smoke review。只需要確認你收到任務，並回覆一行：CCG_SELF_HEALING_SMOKE_OK。
diff --git a/.ccg/dual-model-runs/speed-up-atm-donation-submit-analysis-input.md b/.ccg/dual-model-runs/speed-up-atm-donation-submit-analysis-input.md
index 5b6660d9..8117144f 100644
--- a/.ccg/dual-model-runs/speed-up-atm-donation-submit-analysis-input.md
+++ b/.ccg/dual-model-runs/speed-up-atm-donation-submit-analysis-input.md
@@ -1,4 +1,4 @@
-﻿# Task: speed up ATM donation submission
+# Task: speed up ATM donation submission
 
 User report: ATM/匯款 donation submit shows Processing spinner too long. User asks to speed it up as much as possible.
 
diff --git a/.ccg/dual-model-runs/speed-up-atm-donation-submit-review-input.md b/.ccg/dual-model-runs/speed-up-atm-donation-submit-review-input.md
index f35dfa60..f3d0086f 100644
--- a/.ccg/dual-model-runs/speed-up-atm-donation-submit-review-input.md
+++ b/.ccg/dual-model-runs/speed-up-atm-donation-submit-review-input.md
@@ -1,4 +1,4 @@
-﻿# Review Task: speed-up-atm-donation-submit
+# Review Task: speed-up-atm-donation-submit
 
 User asked to speed up ATM/匯款 donation submission because Processing spinner waits too long.
 
diff --git a/.ccg/dual-model-runs/subagent-goal-word-tutorial-analysis-input.md b/.ccg/dual-model-runs/subagent-goal-word-tutorial-analysis-input.md
index b4b55aef..0f700587 100644
--- a/.ccg/dual-model-runs/subagent-goal-word-tutorial-analysis-input.md
+++ b/.ccg/dual-model-runs/subagent-goal-word-tutorial-analysis-input.md
@@ -1,4 +1,4 @@
-﻿請針對以下文件任務做分析，輸出一份 Word 教學文件的大綱與內容建議。
+請針對以下文件任務做分析，輸出一份 Word 教學文件的大綱與內容建議。
 
 任務：撰寫「Subagent 與 Goal 保母級 Word 教學」。
 使用者要求：
diff --git a/.ccg/dual-model-runs/subagent-goal-word-tutorial-review-input.md b/.ccg/dual-model-runs/subagent-goal-word-tutorial-review-input.md
index 592fb321..3995adab 100644
--- a/.ccg/dual-model-runs/subagent-goal-word-tutorial-review-input.md
+++ b/.ccg/dual-model-runs/subagent-goal-word-tutorial-review-input.md
@@ -1,4 +1,4 @@
-﻿請審查這次文件產出任務的變更。
+請審查這次文件產出任務的變更。
 
 任務：Subagent 與 Goal 保母級 Word 教學。
 主要交付物：.ccg/tasks/subagent-goal-word-tutorial/Subagent_Goal_保母級教學手冊.docx
diff --git a/SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs b/SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs
index 8e3a540d..5ed00c77 100644
--- a/SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs
@@ -372,7 +372,7 @@ namespace ChurchReport.Controllers
                 var callbackUrl = HttpContext.Session.GetString(LineLoginCallbackUrlSessionKey)
                     ?? ResolveLineLoginCallbackUrl(configuration);
 
-                using (var httpClient = new HttpClient())
+                using (var httpClient = CreateLineLoginOAuthHttpClient())
                 {
                     var requestData = new FormUrlEncodedContent(new[]
                     {
@@ -416,7 +416,7 @@ namespace ChurchReport.Controllers
         {
             try
             {
-                using (var httpClient = new HttpClient())
+                using (var httpClient = CreateLineLoginOAuthHttpClient())
                 {
                     httpClient.DefaultRequestHeaders.Authorization =
                         new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
@@ -447,6 +447,17 @@ namespace ChurchReport.Controllers
             }
         }
 
+        private HttpClient CreateLineLoginOAuthHttpClient()
+        {
+            var httpClientFactory = HttpContext?.RequestServices?.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
+            if (httpClientFactory == null)
+            {
+                throw new InvalidOperationException("IHttpClientFactory is required for LINE OAuth HTTP calls.");
+            }
+
+            return httpClientFactory.CreateClient("LineLoginOAuth");
+        }
+
         /// <summary>
         /// 處理 LINE 用戶登入
         /// </summary>
diff --git a/SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs b/SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs
index 904ad6e0..33619043 100644
--- a/SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs
@@ -399,7 +399,7 @@ namespace ChurchReport.Controllers
         /// 訪問 URL: /Home/TestCachePerformance
         /// </summary>
         [Route("/Home/TestCachePerformance")]
-        public IActionResult TestCachePerformance()
+        public async Task<IActionResult> TestCachePerformance()
         {
             try
             {
@@ -436,7 +436,10 @@ namespace ChurchReport.Controllers
                 report += "\n\n";
 
                 // 清除快取以進行下一個測試
-                cacheService?.InvalidateAsync($"list_query_{testContactId}_vice_family_leader").Wait();
+                if (cacheService != null)
+                {
+                    await cacheService.InvalidateAsync($"list_query_{testContactId}_vice_family_leader");
+                }
 
                 return Content(report, "text/plain; charset=utf-8");
             }
diff --git a/SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs b/SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs
index ebad9118..38b76540 100644
--- a/SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs
@@ -35,11 +35,8 @@ namespace ChurchReport.Controllers
         {
             try
             {
-                var contactTask = Task.Run(() =>
-                    ToolUtility.RetrieveContactEntityByLineUserId(lineUserId),
-                    cancellationToken);
-
-                var contact = await contactTask.ConfigureAwait(false);
+                cancellationToken.ThrowIfCancellationRequested();
+                var contact = ToolUtility.RetrieveContactEntityByLineUserId(lineUserId);
 
                 if (contact == null)
                 {
@@ -63,21 +60,11 @@ namespace ChurchReport.Controllers
                     HttpContext?.Session?.SetString("_SessionUserId", lineUserId);
                     await IssueAuthTicketAsync(contact.Id.ToString(), "LineIdLogin", lineUserId, "LINE");
 
-                    var setupDataTask = Task.Run(() =>
-                        InMemoryContext.SetupSmallGroupData(
-                            fullName, "LineIdLogin", lineUserId, DateTime.Now, true),
-                        cancellationToken);
-
-                    var setupViewBagTask = Task.Run(() =>
-                        SetupViewBagForSmallGroup(),
-                        cancellationToken);
-
-                    var ensureDataTask = Task.Run(() =>
-                        EnsureIntegrateDataLoaded(lineUserId),
-                        cancellationToken);
-
-                    await Task.WhenAll(setupDataTask, setupViewBagTask, ensureDataTask)
-                        .ConfigureAwait(false);
+                    cancellationToken.ThrowIfCancellationRequested();
+                    InMemoryContext.SetupSmallGroupData(
+                        fullName, "LineIdLogin", lineUserId, DateTime.Now, true);
+                    SetupViewBagForSmallGroup();
+                    EnsureIntegrateDataLoaded(lineUserId);
 
                     return View("~/Views/Home/IntegrateView.cshtml",
                         InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport);
diff --git a/SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs b/SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs
index 317cbc30..c5796e27 100644
--- a/SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs
+++ b/SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs
@@ -43,7 +43,7 @@ namespace ChurchReport.Models
     public class DonationPaymentManager
     {
         #region 資料區
-        static ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json");
+        static ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").AddEnvironmentVariables();
         static IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         // 商店編號
diff --git a/SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs b/SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs
index db2c6f52..bf64518b 100644
--- a/SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs
+++ b/SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs
@@ -36,6 +36,7 @@ public sealed class ChurchReportLineAdminNotificationService
         new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
             .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+            .AddEnvironmentVariables()
             .Build());
 
     private static readonly Lazy<ChurchReportLineAdminNotificationService> s_default = new(() =>
diff --git a/SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs b/SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs
index 7c5d73e0..aedd8294 100644
--- a/SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs
+++ b/SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs
@@ -45,7 +45,8 @@ namespace ChurchReport.Services
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
+                .AddEnvironmentVariables();
             return builder.Build();
         });
 
diff --git a/SpeechMessageProducts.ChurchReport/Startup.cs b/SpeechMessageProducts.ChurchReport/Startup.cs
index a8c0e3b2..af497061 100644
--- a/SpeechMessageProducts.ChurchReport/Startup.cs
+++ b/SpeechMessageProducts.ChurchReport/Startup.cs
@@ -162,6 +162,10 @@ namespace ChurchReport
             // 使用 HttpClientFactory 來管理 HttpClient 實例，避免記憶體洩漏問題。
             // 這是最佳實務，能夠重用連接並自動處理資源清理。
             services.AddHttpClient();
+            services.AddHttpClient("LineLoginOAuth", client =>
+            {
+                client.Timeout = TimeSpan.FromSeconds(30);
+            });
 
             // ========================================
             // 🔧 修復：MemoryCache 添加過期策略（不限制大小，避免登入卡住）
diff --git a/SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs b/SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs
index 11e2bdf0..0cc02f85 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs
@@ -56,7 +56,8 @@ namespace ChurchReport.Tools
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
+                .AddEnvironmentVariables();
             return builder.Build();
         });
 
diff --git a/SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs b/SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs
index a8304e2a..9910590b 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs
@@ -31,7 +31,8 @@ namespace ChurchReport.Tools
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
+                .AddEnvironmentVariables();
 
             return builder.Build();
         });
diff --git a/SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs b/SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs
index 473926f8..c87d9896 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs
@@ -55,7 +55,8 @@ namespace ChurchReport.Tools
             // ?蔭撱箸??刻?撖虫?
             private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+                .AddEnvironmentVariables();
             private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
             // 敺?蝵株???Channel Access Token
diff --git a/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs b/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
index 1f20349d..2ec44352 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
@@ -63,7 +63,8 @@ namespace ChurchReport.Tools
         // 配置管理
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+            .AddEnvironmentVariables();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         #endregion
diff --git a/SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs b/SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
index 51aefcb1..75448018 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
@@ -69,7 +69,8 @@ namespace ChurchReport.Tools
         // 配置管理
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+            .AddEnvironmentVariables();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         // 追蹤等級
diff --git a/SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs b/SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs
index 5d34f91e..e05e4a17 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs
@@ -42,7 +42,8 @@ namespace ChurchReport.Tools
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
+                .AddEnvironmentVariables();
             return builder.Build();
         });
         private static IConfiguration m_Configuration => s_lazyConfiguration.Value;
diff --git a/SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs b/SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
index 5bacefbd..2c8d1bb1 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
@@ -73,7 +73,8 @@ namespace ChurchReport.Tools
         // 配置管理
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+            .AddEnvironmentVariables();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         #endregion
diff --git a/SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs b/SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
index caf4f51f..98a53719 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
@@ -63,7 +63,8 @@ namespace ChurchReport.Tools
         // 配置管理
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+            .AddEnvironmentVariables();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         #endregion
diff --git a/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs b/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs
index feb27d2e..c70234a6 100644
--- a/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs
+++ b/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs
@@ -51,7 +51,8 @@ namespace ChurchReport.WebServiceConnector
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
+                .AddEnvironmentVariables();
             return builder.Build();
         });
 
diff --git a/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs b/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs
index de94f188..eb8e5fa7 100644
--- a/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs
+++ b/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs
@@ -261,9 +261,28 @@ namespace ChurchReport.WebServiceConnector
                     return null;
                 }
 
-                var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
-                query.Attributes.AddRange("pager", "fullname", "statecode");
-                query.Values.AddRange(DonationPaymentFormModel.DedicationNumber, DonationPaymentFormModel.FullName, 0);
+                var query = new QueryExpression("contact")
+                {
+                    ColumnSet = new ColumnSet(
+                        "contactid",
+                        "fullname",
+                        "pager",
+                        "new_personal_id",
+                        "new_lineid",
+                        "new_lineid_backup",
+                        "parentcustomerid",
+                        "ownerid"),
+                    Criteria = new FilterExpression(LogicalOperator.And)
+                    {
+                        Conditions =
+                        {
+                            new ConditionExpression("pager", ConditionOperator.Equal, DonationPaymentFormModel.DedicationNumber),
+                            new ConditionExpression("fullname", ConditionOperator.Equal, DonationPaymentFormModel.FullName),
+                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
+                        }
+                    },
+                    TopCount = 1
+                };
 
                 var matches = m_ToolUtilityClass.m_Crm2011OrganizationService.RetrieveMultiple(query);
                 return matches.Entities.Count > 0 ? matches.Entities[0] : null;
diff --git a/SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs b/SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs
index e5a058cb..fb857dc9 100644
--- a/SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs
@@ -48,7 +48,8 @@ namespace ChurchReport.WebServiceConnector
         // 配置管理
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+            .AddEnvironmentVariables();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
         #endregion
         #region 常數參數
diff --git a/SpeechMessageProducts.ChurchReport/appsettings.json b/SpeechMessageProducts.ChurchReport/appsettings.json
index 3c561116..4125c8ff 100644
--- a/SpeechMessageProducts.ChurchReport/appsettings.json
+++ b/SpeechMessageProducts.ChurchReport/appsettings.json
@@ -167,11 +167,11 @@
   "LineMessaging": {
     // 好牧人 Line 2.0 (雲端機房)
     "Jesus": {
-      "ChannelAccessToken": "[REDACTED]"
+      "ChannelAccessToken": "[REDACTED]"
     },
     // 好牧人 Line 2.0 (公司內部機房)
     "JesusBack": {
-      "ChannelAccessToken": "[REDACTED]"
+      "ChannelAccessToken": "[REDACTED]"
     },
     // 預設組織 (需與 LineMessaging 區段的 key 名稱大小寫一致)
     "DefaultOrganization": "Jesus"
@@ -184,7 +184,7 @@
   // 請在 LINE Developers Console 建立 LINE Login Channel 並填入以下資訊
   "LineLogin": {
     "ChannelId": "2007621061", // ✅ LINE Login Channel ID
-    "ChannelSecret": "[REDACTED]", // ✅ LINE Login Channel Secret
+    "ChannelSecret": "[REDACTED]", // ✅ LINE Login Channel Secret
     "CallbackUrl": "https://jesus.speechmessage.com.tw:807/Authentication/LineCallback", // ✅ Callback URL（請根據實際環境修改）
     "Scope": "profile openid", // 請求的權限範圍
     "State": "random_state_string" // CSRF 防護用的 state 參數（每次動態生成）
@@ -209,7 +209,7 @@
   "MiniApp": {
     // Mini App Channel 基本資訊
     "ChannelId": "2009427707", // Mini App Channel ID（目前使用 Developing 環境）
-    "ChannelSecret": "[REDACTED]", // Mini App Channel Secret（目前使用 Developing 環境）
+    "ChannelSecret": "[REDACTED]", // Mini App Channel Secret（目前使用 Developing 環境）
 
     // 三個環境的 LIFF ID（在 Console 建立 Mini App Channel 後會自動產生）
     "DevelopingLiffId": "2009427707-Fi5L5blD", // Developing 環境 LIFF ID
@@ -248,14 +248,14 @@
     "Domain": "DYNAMICS-365", // CRM 網域
     "ServerUrl": "https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc", // CRM 伺服器網址
     "Username": "SPEECHMESSAGE\\Administrator", // CRM 使用者名稱
-    "Password": "[REDACTED]", // CRM 密碼
+    "Password": "[REDACTED]", // CRM 密碼
 
     // 公司內部機房
     //"Organization": "jesusback", // CRM 組織名稱
     //"Domain": "SPEECHMESSAGE", // CRM 網域
     //"ServerUrl": "https://jesusback.speechmessage.com.tw/XRMServices/2011/Organization.svc", // CRM 伺服器網址
     //"Username": "SPEECHMESSAGE\\Administrator", // CRM 使用者名稱
-    //"Password": "[REDACTED]", // CRM 密碼
+    //"Password": "[REDACTED]", // CRM 密碼
 
     "MinPoolSize": 3, // 最小連接池大小
     "MaxPoolSize": 20, // 最大連接池大小
@@ -268,7 +268,7 @@
   // ==============================================
   "LinePay": {
     "ChannelId": "1634548482", // LINE Pay 通道 ID
-    "ChannelSecret": "[REDACTED]", // LINE Pay 通道密鑰
+    "ChannelSecret": "[REDACTED]", // LINE Pay 通道密鑰
     "IsSandbox": true // 是否使用沙盒測試環境
   },
 
@@ -294,11 +294,11 @@
         "Environment": "Sandbox",
         "Credentials": {
           "ShopNo": "NA0149_001",
-          "A1": "5E854757C751413F",
-          "A2": "D743D0EB06904837",
-          "B1": "08169D5445644513",
-          "B2": "8E52B5A180EE4399",
-          "XKeyId": "[REDACTED]"
+          "A1": "",
+          "A2": "",
+          "B1": "",
+          "B2": "",
+          "XKeyId": "[REDACTED]"
         },
         "Endpoints": {
           "ApiBaseUrl": "https://sandbox.sinopac.com/QPay.WebAPI/api/"
@@ -309,7 +309,7 @@
         "Environment": "Production",
         "Credentials": {
           "StoreId": "130544850001",
-          "Key": "[REDACTED]",
+          "Key": "[REDACTED]",
           "IV": "[REDACTED]"
         },
         "Endpoints": {
@@ -321,8 +321,8 @@
         "Environment": "Sandbox",
         "Credentials": {
           "StoreId": "999812777000199",
-          "StoreKey": "[REDACTED]",
-          "StoreIV": "[REDACTED]",
+          "StoreKey": "[REDACTED]",
+          "StoreIV": "[REDACTED]",
           "TerminalId": "T0000000",
           "MerchantId": "999812777000199"
         },
@@ -339,11 +339,11 @@
   "Sinopac": {
     "Site": "https://api.sinopac.com/funBIZ/QPay.WebAPI/api/", // 正式環境 API 網址
     "ShopNo": "DA4272_001", // 商店代號
-    "A1": "00DC1BDACCB645C6", // 加密金鑰 A1
-    "A2": "185B6F59F737462E", // 加密金鑰 A2
-    "B1": "6F9C2936E8524F76", // 加密金鑰 B1
-    "B2": "8BB48C2260304E29", // 加密金鑰 B2
-    "XKeyID": "[REDACTED]" // X-Key 識別碼
+    "A1": "", // 加密金鑰 A1
+    "A2": "", // 加密金鑰 A2
+    "B1": "", // 加密金鑰 B1
+    "B2": "", // 加密金鑰 B2
+    "XKeyID": "[REDACTED]" // X-Key 識別碼
   },
 
   // ==============================================
@@ -354,11 +354,11 @@
     //"Site": "https://apisbx.sinopac.com/funBIZ-Sbx/QPay.WebAPI/api/", // 沙盒環境 API 網址
     "Site": "https://sandbox.sinopac.com/QPay.WebAPI/api/", // 沙盒環境 API 網址
     "ShopNo": "NA0149_001", // 測試商店代號
-    "A1": "5E854757C751413F", // 測試加密金鑰 A1
-    "A2": "D743D0EB06904837", // 測試加密金鑰 A2
-    "B1": "08169D5445644513", // 測試加密金鑰 B1
-    "B2": "8E52B5A180EE4399", // 測試加密金鑰 B2
-    "XKeyID": "[REDACTED]" // 測試 X-Key 識別碼
+    "A1": "", // 測試加密金鑰 A1
+    "A2": "", // 測試加密金鑰 A2
+    "B1": "", // 測試加密金鑰 B1
+    "B2": "", // 測試加密金鑰 B2
+    "XKeyID": "[REDACTED]" // 測試 X-Key 識別碼
   },
 
   // ==============================================
@@ -367,12 +367,12 @@
   "MyPay": {
     // --- 基本商店資訊 (Basic Store Information) ---
     "Store_Id": "130544850001", // 音訊科技商店代號
-    "Key": "[REDACTED]", // 音訊科技加密金鑰
+    "Key": "[REDACTED]", // 音訊科技加密金鑰
     "Url": "https://ka.usecase.cc/api/init", // 測試環境 API 初始化網址
     //"Url": "https://ka.usecase.cc/api/agent", // 測試環境 API 初始化網址
 
     //"Store_Id": "200043350001", // 好牧人商店代號
-    //"Key": "[REDACTED]", // 好牧人加密金鑰
+    //"Key": "[REDACTED]", // 好牧人加密金鑰
     ///"Url": "https://ka.mypay.tw/api/init", // 正式環境 API 初始化網址
     ////"Url": "https://ka.mypay.tw/api/agent", // 正式環境 API 初始化網址
 
@@ -498,8 +498,8 @@
   "TSPG": {
     // --- 基本商店資訊 (Basic Store Information) ---
     "StoreId": "999812777000199", // 特店代號 (正式或測試)
-    "StoreKey": "[REDACTED]", // Hash Key (商店金鑰)需要替換為實際值
-    "StoreIV": "[REDACTED]", // Hash IV (初始向量)需要替換為實際值
+    "StoreKey": "[REDACTED]", // Hash Key (商店金鑰)需要替換為實際值
+    "StoreIV": "[REDACTED]", // Hash IV (初始向量)需要替換為實際值
     "ApiBaseUrl": "https://tspg-t.taishinbank.com.tw/tspgapi/restapi", // API 基礎網址 (測試環境)
 
     // --- 特店與端末設定 (Merchant and Terminal Settings) ---
diff --git a/ToolUtility.Tests/AttachmentOperations/AttachmentServiceTests.cs b/ToolUtility.Tests/AttachmentOperations/AttachmentServiceTests.cs
index 7606ecec..a040a2c0 100644
--- a/ToolUtility.Tests/AttachmentOperations/AttachmentServiceTests.cs
+++ b/ToolUtility.Tests/AttachmentOperations/AttachmentServiceTests.cs
@@ -27,11 +27,11 @@ namespace ToolUtility.Tests.AttachmentOperations
         public void DownloadAttachment_WhenCalled_ShouldReturnCollection()
         {
             var mockLogger = MockLoggerFactory.CreateMock<object>();
-            var mockCrudClient = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
 
-            var service = new AttachmentService(mockLogger.Object, mockCrudClient.Object);
+            var service = new AttachmentService(mockLogger.Object, mockCrm.Object);
 
-            var crm = (IOrganizationService)null;
+            var crm = mockCrm.Object;
             var result = service.DownloadAttachment(ref crm, Guid.NewGuid());
 
             result.Should().NotBeNull();
@@ -42,15 +42,15 @@ namespace ToolUtility.Tests.AttachmentOperations
         public void UploadAttachment_WhenCalled_ShouldCreateAnnotation()
         {
             var mockLogger = MockLoggerFactory.CreateMock<object>();
-            var mockCrudClient = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
 
-            var service = new AttachmentService(mockLogger.Object, mockCrudClient.Object);
+            var service = new AttachmentService(mockLogger.Object, mockCrm.Object);
 
-            var crm = (IOrganizationService)null;
+            var crm = mockCrm.Object;
 
             service.UploadAttachment(ref crm, "contact", "sub", "note", "file.txt", "text/plain", new byte[] {1,2,3}, Guid.NewGuid());
 
-            Assert.True(true);
+            mockCrm.Verify(x => x.Create(It.Is<Entity>(a => a.LogicalName == "annotation" && a["filename"].ToString() == "file.txt")), Times.Once);
         }
     }
 }
diff --git a/ToolUtility.Tests/ContactOperations/ContactServiceTests.cs b/ToolUtility.Tests/ContactOperations/ContactServiceTests.cs
index c49b24c3..31fa518e 100644
--- a/ToolUtility.Tests/ContactOperations/ContactServiceTests.cs
+++ b/ToolUtility.Tests/ContactOperations/ContactServiceTests.cs
@@ -15,8 +15,6 @@ using Xunit;
 using FluentAssertions;
 using ToolUtilityNameSpace.ContactOperations;
 using ToolUtility.Tests.TestHelpers;
-using ToolUtilityNameSpace.EntityOperations;
-using Moq;
 using System;
 using Microsoft.Xrm.Sdk;
 using Microsoft.Xrm.Sdk.Query;
@@ -30,12 +28,11 @@ namespace ToolUtility.Tests.ContactOperations
         {
             var expected = TestEntityFactory.CreateContact("U123456", "測試聯絡人");
 
-            var mockQueryService = new Mock<IEntityQueryService>();
-            mockQueryService.Setup(x => x.RetrieveMultiple(It.IsAny<QueryByAttribute>()))
-                .Returns(new EntityCollection(new[] { expected }));
+            var mockOrganizationService = MockOrganizationServiceFactory.CreateMockWithCollection(
+                new EntityCollection(new[] { expected }));
 
             var mockLogger = MockLoggerFactory.CreateMock<object>();
-            var service = new ContactService(mockLogger.Object, mockQueryService.Object);
+            var service = new ContactService(mockLogger.Object, mockOrganizationService.Object);
 
             var result = service.RetrieveByLineId("U123456");
 
@@ -52,12 +49,10 @@ namespace ToolUtility.Tests.ContactOperations
                 TestEntityFactory.CreateContact("U456", "B")
             });
 
-            var mockQueryService = new Mock<IEntityQueryService>();
-            mockQueryService.Setup(x => x.RetrieveMultiple(It.IsAny<QueryByAttribute>()))
-                .Returns(collection);
+            var mockOrganizationService = MockOrganizationServiceFactory.CreateMockWithCollection(collection);
 
             var mockLogger = MockLoggerFactory.CreateMock<object>();
-            var service = new ContactService(mockLogger.Object, mockQueryService.Object);
+            var service = new ContactService(mockLogger.Object, mockOrganizationService.Object);
 
             var result = service.RetrieveCollectionByName("A");
 
diff --git a/ToolUtility.Tests/Core/ToolUtilityClassIntegrationTests.cs b/ToolUtility.Tests/Core/ToolUtilityClassIntegrationTests.cs
index 5df10472..474bae6c 100644
--- a/ToolUtility.Tests/Core/ToolUtilityClassIntegrationTests.cs
+++ b/ToolUtility.Tests/Core/ToolUtilityClassIntegrationTests.cs
@@ -31,10 +31,10 @@ namespace ToolUtility.Tests.Core
             var expected = TestEntityFactory.CreateContact("U123", "測試");
             var collection = new EntityCollection(new[] { expected });
 
-            var mockCrm = MockCrmClientFactory.CreateMockWithCollection(collection);
+            var mockCrm = MockOrganizationServiceFactory.CreateMockWithCollection(collection);
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
-            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);
+            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);
 
             // Act
             var result = facade.RetrieveContactByLineId("U123");
@@ -48,10 +48,10 @@ namespace ToolUtility.Tests.Core
         public void SetEntityBoolAttribute_ShouldDelegateToAttributeService()
         {
             // Arrange
-            var mockCrm = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
-            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);
+            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);
 
             var entity = new Entity("contact");
 
diff --git a/ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs b/ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs
index a540c0bf..86621800 100644
--- a/ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs
+++ b/ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs
@@ -15,6 +15,7 @@ using Xunit;
 using FluentAssertions;
 using Moq;
 using System;
+using Microsoft.Crm.Sdk.Messages;
 using Microsoft.Xrm.Sdk;
 using ToolUtilityNameSpace.Core;
 using ToolUtility.Tests.TestHelpers;
@@ -29,13 +30,13 @@ namespace ToolUtility.Tests.Core
         [Fact]
         public void Create_Update_Delete_Entity_Via_Facade()
         {
-            var mockCrm = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
             var createdId = Guid.NewGuid();
             mockCrm.Setup(x => x.Create(It.IsAny<Entity>())).Returns(createdId);
 
-            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);
+            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);
 
             var entity = new Entity("account") { ["name"] = "TDD Test" };
 
@@ -56,11 +57,11 @@ namespace ToolUtility.Tests.Core
         [Fact]
         public void UploadAttachment_ShouldCallCreateAnnotation()
         {
-            var mockCrm = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
-            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);
+            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);
 
-            var crmService = (IOrganizationService)null;
+            var crmService = mockCrm.Object;
             facade.UploadAnAttachment(ref crmService, "contact", "sub", "note", "file.txt", "text/plain", new byte[] { 1,2,3 }, Guid.NewGuid());
 
             mockCrm.Verify(x => x.Create(It.Is<Entity>(a => a.LogicalName == "annotation" && a["filename"].ToString() == "file.txt")), Times.Once);
@@ -69,37 +70,53 @@ namespace ToolUtility.Tests.Core
         [Fact]
         public void AddAndRemoveMembersToMarketingList_ShouldCallListService()
         {
-            var mockCrm = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
-            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);
+            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);
 
             var listId = Guid.NewGuid();
             var members = new System.Collections.Generic.List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
 
             facade.AddMembersToMarketingList(listId, members);
 
-            // Verify create called for each member (ListService calls ICrmClient.Create)
-            mockCrm.Verify(x => x.Create(It.Is<Entity>(e => e.LogicalName == "listmember")), Times.Exactly(members.Count));
+            mockCrm.Verify(x => x.Execute(It.Is<OrganizationRequest>(request =>
+                IsAddListMembersRequest(request, listId, members.Count))), Times.Once);
 
             var memberToRemove = members[0];
             facade.RemoveMembersToMarketingList(listId, memberToRemove);
 
-            // Removal in our simple impl calls Delete on list entity - verify Delete called
-            mockCrm.Verify(x => x.Delete("list", It.IsAny<Guid>()), Times.AtLeastOnce);
+            mockCrm.Verify(x => x.Execute(It.Is<OrganizationRequest>(request =>
+                IsRemoveMemberRequest(request, listId, memberToRemove))), Times.Once);
         }
 
         [Fact]
         public void CreatePushLineMessage_ShouldCallCrudCreate()
         {
-            var mockCrm = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
-            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);
+            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);
 
             facade.CreatePushLineMessage("U123", "sub", "hello");
 
             // LineMessageService creates an entity via IEntityCrudService which uses ICrmClient.Create
             mockCrm.Verify(x => x.Create(It.Is<Entity>(e => e.LogicalName == "linemessage" && e["userid"].ToString() == "U123")), Times.Once);
         }
+
+        private static bool IsAddListMembersRequest(OrganizationRequest request, Guid listId, int memberCount)
+        {
+            var addRequest = request as AddListMembersListRequest;
+            return addRequest != null &&
+                addRequest.ListId == listId &&
+                addRequest.MemberIds.Length == memberCount;
+        }
+
+        private static bool IsRemoveMemberRequest(OrganizationRequest request, Guid listId, Guid memberId)
+        {
+            var removeRequest = request as RemoveMemberListRequest;
+            return removeRequest != null &&
+                removeRequest.ListId == listId &&
+                removeRequest.EntityId == memberId;
+        }
     }
 }
diff --git a/ToolUtility.Tests/EntityOperations/EntityCrudServiceTests.cs b/ToolUtility.Tests/EntityOperations/EntityCrudServiceTests.cs
index bff056b0..8a9ae90f 100644
--- a/ToolUtility.Tests/EntityOperations/EntityCrudServiceTests.cs
+++ b/ToolUtility.Tests/EntityOperations/EntityCrudServiceTests.cs
@@ -15,7 +15,6 @@ using Xunit;
 using FluentAssertions;
 using ToolUtilityNameSpace.EntityOperations;
 using ToolUtility.Tests.TestHelpers;
-using ToolUtilityNameSpace.Interfaces;
 using Microsoft.Xrm.Sdk;
 using Moq;
 using System;
@@ -28,7 +27,7 @@ namespace ToolUtility.Tests.EntityOperations
         public void CreateEntity_ShouldReturnGuid()
         {
             var entity = TestEntityFactory.CreateEmpty("contact");
-            var mockClient = MockCrmClientFactory.CreateMock();
+            var mockClient = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
             var service = new EntityCrudService(mockLogger.Object, mockClient.Object);
@@ -44,7 +43,7 @@ namespace ToolUtility.Tests.EntityOperations
             var entity = TestEntityFactory.CreateEmpty("contact");
             entity["fullname"] = "new name";
 
-            var mockClient = MockCrmClientFactory.CreateMock();
+            var mockClient = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
             var service = new EntityCrudService(mockLogger.Object, mockClient.Object);
@@ -58,7 +57,7 @@ namespace ToolUtility.Tests.EntityOperations
         public void DeleteEntity_ShouldCallClient()
         {
             var id = Guid.NewGuid();
-            var mockClient = MockCrmClientFactory.CreateMock();
+            var mockClient = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
             var service = new EntityCrudService(mockLogger.Object, mockClient.Object);
diff --git a/ToolUtility.Tests/EntityOperations/EntityQueryServiceTests.cs b/ToolUtility.Tests/EntityOperations/EntityQueryServiceTests.cs
index 0269e213..28da0153 100644
--- a/ToolUtility.Tests/EntityOperations/EntityQueryServiceTests.cs
+++ b/ToolUtility.Tests/EntityOperations/EntityQueryServiceTests.cs
@@ -15,7 +15,6 @@ using Xunit;
 using FluentAssertions;
 using ToolUtilityNameSpace.EntityOperations;
 using ToolUtility.Tests.TestHelpers;
-using ToolUtilityNameSpace.Interfaces;
 using Microsoft.Xrm.Sdk;
 using Microsoft.Xrm.Sdk.Query;
 using Moq;
@@ -29,7 +28,7 @@ namespace ToolUtility.Tests.EntityOperations
         public void RetrieveEntity_WhenEntityExists_ShouldReturnEntity()
         {
             var expected = TestEntityFactory.CreateContact("U123", "測試");
-            var mockClient = MockCrmClientFactory.CreateMockWithEntity(expected);
+            var mockClient = MockOrganizationServiceFactory.CreateMockWithEntity(expected);
 
             var mockLogger = MockLoggerFactory.CreateMock<object>();
             var service = new EntityQueryService(mockLogger.Object, mockClient.Object);
@@ -49,7 +48,7 @@ namespace ToolUtility.Tests.EntityOperations
                 TestEntityFactory.CreateContact("U456", "測試2")
             });
 
-            var mockClient = MockCrmClientFactory.CreateMockWithCollection(collection);
+            var mockClient = MockOrganizationServiceFactory.CreateMockWithCollection(collection);
             var mockLogger = MockLoggerFactory.CreateMock<object>();
             var service = new EntityQueryService(mockLogger.Object, mockClient.Object);
 
diff --git a/ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs b/ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs
index 26317cdc..7047d406 100644
--- a/ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs
+++ b/ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs
@@ -16,7 +16,6 @@ using FluentAssertions;
 using ToolUtilityNameSpace.LineMessaging;
 using ToolUtility.Tests.TestHelpers;
 using Moq;
-using ToolUtilityNameSpace.EntityOperations;
 using System;
 using Microsoft.Xrm.Sdk;
 
@@ -27,14 +26,14 @@ namespace ToolUtility.Tests.LineMessaging
         [Fact]
         public void CreatePushMessage_ShouldCallCreateEntity()
         {
-            var mockCrud = new Mock<IEntityCrudService>();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
-            var service = new LineMessageService(mockLogger.Object, mockCrud.Object);
+            var service = new LineMessageService(mockLogger.Object, mockCrm.Object);
 
             service.CreatePushMessage("U123", "sub", "hello");
 
-            mockCrud.Verify(x => x.CreateEntity(It.IsAny<Entity>()), Times.Once);
+            mockCrm.Verify(x => x.Create(It.Is<Entity>(e => e.LogicalName == "linemessage" && e["userid"].ToString() == "U123")), Times.Once);
         }
     }
 }
diff --git a/ToolUtility.Tests/ListOperations/ListServiceTests.cs b/ToolUtility.Tests/ListOperations/ListServiceTests.cs
index 7613b387..99542bce 100644
--- a/ToolUtility.Tests/ListOperations/ListServiceTests.cs
+++ b/ToolUtility.Tests/ListOperations/ListServiceTests.cs
@@ -18,8 +18,8 @@ using ToolUtility.Tests.TestHelpers;
 using Moq;
 using System;
 using System.Collections.Generic;
-using ToolUtilityNameSpace.EntityOperations;
-using ToolUtilityNameSpace.EntityOperations;
+using Microsoft.Crm.Sdk.Messages;
+using Microsoft.Xrm.Sdk;
 
 namespace ToolUtility.Tests.ListOperations
 {
@@ -28,37 +28,51 @@ namespace ToolUtility.Tests.ListOperations
         [Fact]
         public void AddMembers_ShouldCallCreateForEachMember()
         {
-            var mockQuery = new Mock<IEntityQueryService>();
-            var mockCrudClient = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
-            var service = new ListService(mockLogger.Object, mockQuery.Object, mockCrudClient.Object);
+            var service = new ListService(mockLogger.Object, mockCrm.Object);
 
             var members = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
             var listId = Guid.NewGuid();
 
             service.AddMembers(listId, members);
 
-            // No exception means success for this simple impl
-            Assert.True(true);
+            mockCrm.Verify(x => x.Execute(It.Is<OrganizationRequest>(request =>
+                IsAddListMembersRequest(request, listId, members.Count))), Times.Once);
         }
 
         [Fact]
         public void RemoveMember_ShouldCallDelete()
         {
-            var mockQuery = new Mock<IEntityQueryService>();
-            var mockCrudClient = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
-            var service = new ListService(mockLogger.Object, mockQuery.Object, mockCrudClient.Object);
+            var service = new ListService(mockLogger.Object, mockCrm.Object);
 
             var member = Guid.NewGuid();
             var listId = Guid.NewGuid();
 
             service.RemoveMember(listId, member);
 
-            // No exception means success
-            Assert.True(true);
+            mockCrm.Verify(x => x.Execute(It.Is<OrganizationRequest>(request =>
+                IsRemoveMemberRequest(request, listId, member))), Times.Once);
+        }
+
+        private static bool IsAddListMembersRequest(OrganizationRequest request, Guid listId, int memberCount)
+        {
+            var addRequest = request as AddListMembersListRequest;
+            return addRequest != null &&
+                addRequest.ListId == listId &&
+                addRequest.MemberIds.Length == memberCount;
+        }
+
+        private static bool IsRemoveMemberRequest(OrganizationRequest request, Guid listId, Guid memberId)
+        {
+            var removeRequest = request as RemoveMemberListRequest;
+            return removeRequest != null &&
+                removeRequest.ListId == listId &&
+                removeRequest.EntityId == memberId;
         }
     }
 }
diff --git a/ToolUtility.Tests/QueryOperations/PresentRecordQueryServiceTests.cs b/ToolUtility.Tests/QueryOperations/PresentRecordQueryServiceTests.cs
index 4f7ffe4c..fb8e0b27 100644
--- a/ToolUtility.Tests/QueryOperations/PresentRecordQueryServiceTests.cs
+++ b/ToolUtility.Tests/QueryOperations/PresentRecordQueryServiceTests.cs
@@ -34,7 +34,9 @@ public sealed class PresentRecordQueryServiceTests
             "new_app_named",
             "new_contact_family_leader_list",
             "new_contact_race_leager_list",
-            "new_contact_list_arealeader"
+            "new_contact_list_arealeader",
+            "new_happy_start_date",
+            "new_happy_end_date"
         });
         capturedQuery.PageInfo.Should().NotBeNull();
         capturedQuery.PageInfo.PageNumber.Should().Be(1);
diff --git a/ToolUtility.Tests/ToolUtility.Tests.csproj b/ToolUtility.Tests/ToolUtility.Tests.csproj
index 8fd5a0dc..3257bb55 100644
--- a/ToolUtility.Tests/ToolUtility.Tests.csproj
+++ b/ToolUtility.Tests/ToolUtility.Tests.csproj
@@ -1,7 +1,7 @@
 <Project Sdk="Microsoft.NET.Sdk">
 
   <PropertyGroup>
-    <TargetFramework>net8.0</TargetFramework>
+    <TargetFramework>net10.0</TargetFramework>
     <ImplicitUsings>enable</ImplicitUsings>
     <Nullable>enable</Nullable>
     <IsPackable>false</IsPackable>
@@ -9,21 +9,21 @@
   </PropertyGroup>
 
   <ItemGroup>
-    <!-- ���ծج[ -->
+    <!-- Test framework -->
     <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
     <PackageReference Include="xunit" Version="2.6.6" />
     <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
-    
-    <!-- Mock �ج[ -->
+
+    <!-- Mock framework -->
     <PackageReference Include="Moq" Version="4.20.70" />
-    
-    <!-- �_���w -->
+
+    <!-- Assertions -->
     <PackageReference Include="FluentAssertions" Version="6.12.0" />
-    
-    <!-- �{���X�л\�v -->
+
+    <!-- Code coverage -->
     <PackageReference Include="coverlet.collector" Version="6.0.0">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
@@ -35,14 +35,14 @@
   </ItemGroup>
 
   <ItemGroup>
-    <!-- �M�װѦ� -->
+    <!-- Project reference -->
     <ProjectReference Include="..\ToolUtility\ToolUtility.csproj" />
   </ItemGroup>
 
   <ItemGroup>
-    <!-- CRM SDK �M��]�Ω���ա^ -->
+    <!-- CRM SDK packages for tests -->
     <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" Version="9.0.2.56" />
-    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
+    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
   </ItemGroup>
 
 </Project>
diff --git a/ToolUtility/QueryOperations/PresentRecordQueryService.cs b/ToolUtility/QueryOperations/PresentRecordQueryService.cs
index d81714a7..989246ee 100644
--- a/ToolUtility/QueryOperations/PresentRecordQueryService.cs
+++ b/ToolUtility/QueryOperations/PresentRecordQueryService.cs
@@ -291,7 +291,27 @@ namespace ToolUtilityNameSpace.QueryOperations
                 var query = new QueryExpression
                 {
                     EntityName = "list",
-                    ColumnSet = new ColumnSet(true)
+                    ColumnSet = new ColumnSet(
+                        "listid",
+                        "listname",
+                        "purpose",
+                        "new_app_named",
+                        "new_contact_family_leader_list",
+                        "new_contact_race_leager_list",
+                        "new_contact_list_arealeader",
+                        "new_contact_list_vice_family_leader",
+                        "new_contact_co_race_leager_list",
+                        "new_contact_list_co_arealeader",
+                        "new_familyhead_list",
+                        "new_happy_start_date",
+                        "new_happy_end_date",
+                        "statuscode",
+                        "statecode"),
+                    PageInfo = new PagingInfo
+                    {
+                        Count = 5000,
+                        PageNumber = 1
+                    }
                 };
 
                 var filter = new FilterExpression(LogicalOperator.And);
diff --git a/ToolUtility/Utilities/StringUtility.cs b/ToolUtility/Utilities/StringUtility.cs
index d200513a..daf1ce9d 100644
--- a/ToolUtility/Utilities/StringUtility.cs
+++ b/ToolUtility/Utilities/StringUtility.cs
@@ -35,7 +35,7 @@ namespace ToolUtilityNameSpace.Utilities
             int lastIndexEnglish = stringToProcess.LastIndexOf(',');
             int lastIndex = Math.Max(lastIndexChinese, lastIndexEnglish);
 
-            if (lastIndex > 0)
+            if (lastIndex >= 0)
             {
                 stringToProcess = stringToProcess.Substring(0, lastIndex);
             }

# Untracked code file diff
diff --git "a/ToolUtility.Tests\\TestHelpers\\MockOrganizationServiceFactory.cs" "b/ToolUtility.Tests\\TestHelpers\\MockOrganizationServiceFactory.cs"
new file mode 100644
index 00000000..5e00d612
--- /dev/null
+++ "b/ToolUtility.Tests\\TestHelpers\\MockOrganizationServiceFactory.cs"
@@ -0,0 +1,57 @@
+using System;
+using Microsoft.Xrm.Sdk;
+using Microsoft.Xrm.Sdk.Query;
+using Moq;
+
+namespace ToolUtility.Tests.TestHelpers
+{
+    public static class MockOrganizationServiceFactory
+    {
+        public static Mock<IOrganizationService> CreateMock()
+        {
+            var mock = new Mock<IOrganizationService>();
+
+            mock.Setup(x => x.Retrieve(
+                It.IsAny<string>(),
+                It.IsAny<Guid>(),
+                It.IsAny<ColumnSet>()))
+                .Returns((Entity)null!);
+
+            mock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
+                .Returns(new EntityCollection());
+
+            mock.Setup(x => x.Create(It.IsAny<Entity>()))
+                .Returns(Guid.NewGuid());
+
+            mock.Setup(x => x.Update(It.IsAny<Entity>()));
+            mock.Setup(x => x.Delete(It.IsAny<string>(), It.IsAny<Guid>()));
+            mock.Setup(x => x.Execute(It.IsAny<OrganizationRequest>()))
+                .Returns(new OrganizationResponse());
+
+            return mock;
+        }
+
+        public static Mock<IOrganizationService> CreateMockWithEntity(Entity entity)
+        {
+            var mock = CreateMock();
+
+            mock.Setup(x => x.Retrieve(
+                entity.LogicalName,
+                entity.Id,
+                It.IsAny<ColumnSet>()))
+                .Returns(entity);
+
+            return mock;
+        }
+
+        public static Mock<IOrganizationService> CreateMockWithCollection(EntityCollection collection)
+        {
+            var mock = CreateMock();
+
+            mock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
+                .Returns(collection);
+
+            return mock;
+        }
+    }
+}
``

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.