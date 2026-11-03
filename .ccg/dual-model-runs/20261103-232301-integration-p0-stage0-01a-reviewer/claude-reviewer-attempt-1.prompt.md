ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: integration-p0-stage0-01a

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# 階段0 第1批 第1/3份：守門工具與 CI

你是唯讀外部審查者。本輪為「單模型（使用者指定 ClaudeOnly）」，不是雙模型或配額降級。只審查，不改檔、不提交、不執行產品／工具／測試，不建立任何網路或 D365／LINE 連線。
審查重點：跨使用者／跨教會隔離、資源釋放、機密外洩、例外責任（設計10.5／10.9）、net48/net10行為一致、守門假綠燈。所有真實連線驗收仍由使用者負責。
機密欄位已遮罩；遮罩值不可用來推斷實際設定正確性。不得讀取 appsettings*、User Secrets、環境變數、Logs、金鑰／授權檔，或以 git show 取未遮罩版本。只以所附安全差異審查；如需上下文，可只查明確的非機密程式來源。不要開啟 .ccg/dual-model-runs 的其他歷史輸出或 research 原始紀錄。
輸出只列可驗證的 Critical／High／Medium／Low，包含檔案、行號、觸發條件、具體影響及最小修法；測試不足須說明可重現的缺口，不把純猜測當發現。沒有問題請明寫。不得聲稱實跑測試。
已核定例外：嚴格 .Tests 測試專案僅 CA1707 放寬；未實作 API 的 CA1716 可改語意近名；Core 不通知，Plugin 僅追蹤，最終告警在 net10 重試用盡／階段5；正式金鑰只程序環境且不讀內容；本階段不存在業務 Rejected。小組頁面由使用者確認恢復，圖表未逐項驗證，之前審查兩次為逾時。

範圍：73b6e6cf409daa7559fd3c11d482e3efc542c0d7..ae29cb40aa5c395f83c169068d7ca40e7094c509
安全差異 SHA256：09965e58dc8f9096624b2306e75ae017775e2968ea6006b9f4dd4324bc4a8a3a
同批其他分份須一併審查後才算完成，本份不可代表全批。
專項：檔案掃描邊界只限本 worktree／方案；編碼、重複、分析器、NuGetAudit、覆蓋率、生成檔與 CI 是否可能假綠燈；一般金鑰缺失失敗及 runner 清理；簽章只能明確要求。P0-01 的既有測試修復不得削弱斷言。

```diff
diff --git a/.github/workflows/solution-ci.yml b/.github/workflows/solution-ci.yml
new file mode 100644
index 000000000..8a3789f69
--- /dev/null
+++ b/.github/workflows/solution-ci.yml
@@ -0,0 +1,218 @@
+name: Solution CI
+
+on:
+  push:
+  pull_request:
+  workflow_dispatch:
+
+permissions:
+  contents: read
+
+jobs:
+  solution:
+    runs-on: windows-latest
+    timeout-minutes: 45
+    defaults:
+      run:
+        shell: powershell
+    env:
+      DOTNET_CLI_UI_LANGUAGE: en-US
+      VSLANG: '1033'
+      BASE_COMMIT: 73b6e6cf409daa7559fd3c11d482e3efc542c0d7
+      SixLaborsLicenseFile: ''
+    steps:
+      - name: Checkout full history
+        uses: actions/checkout@v4
+        with:
+          fetch-depth: 0
+      - name: Select SDK from global.json
+        uses: actions/setup-dotnet@v4
+        with:
+          global-json-file: global.json
+      - name: Select Node for progress page tests
+        uses: actions/setup-node@v4
+        with:
+          node-version: '22'
+      - name: Prepare Six Labors license
+        env:
+          SIXLABORS_LICENSE: ${{ secrets.SIXLABORS_LICENSE }}
+        run: |
+          $ErrorActionPreference = 'Stop'
+          # 使用者核定的一般金鑰只經步驟環境傳入，不插入腳本文字，也不輸出內容。
+          if ([string]::IsNullOrWhiteSpace($env:SIXLABORS_LICENSE)) {
+            $env:SIXLABORS_LICENSE = $null
+            throw 'SIXLABORS_LICENSE is required for Solution CI.'
+          }
+          $created = $false
+          $licensePath = $null
+          try {
+            if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP) -or [string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) {
+              throw 'Runner environment is unavailable.'
+            }
+            $licensePath = Join-Path $env:RUNNER_TEMP ('speechmessage-sixlabors-' + [Guid]::NewGuid().ToString('N') + '.lic')
+            # CreateNew 不覆寫既有檔。單一步驟擁有 stream，寫入故障與成功均先關閉才進行清理。
+            $stream = [IO.File]::Open($licensePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
+            $created = $true
+            try {
+              $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false), 1024, $true)
+              try { $writer.Write($env:SIXLABORS_LICENSE); $writer.Flush() }
+              finally { $writer.Dispose() }
+            } finally { $stream.Dispose() }
+            # 後續建置只取得 runner 路徑；不把內容寫到 GITHUB_ENV、摘要或 artifact。
+            [IO.File]::AppendAllText($env:GITHUB_ENV, "SixLaborsLicenseFile=$licensePath`n", [Text.UTF8Encoding]::new($false))
+          } catch {
+            if ($created -and [IO.File]::Exists($licensePath)) { Remove-Item -LiteralPath $licensePath -Force }
+            throw 'Six Labors license preparation failed.'
+          } finally { $env:SIXLABORS_LICENSE = $null }
+      - name: Restore
+        run: |
+          dotnet restore SpeechMessageProducts.sln
+          if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }
+          foreach ($project in @('tools/I18nCheck.Tests', 'tools/PluginPackaging.Tests')) {
+            dotnet restore $project
+            if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
+          }
+      - name: Build without signing key
+        run: |
+          # 一般 Release 建置不讀金鑰、不打包；只有獨立正式打包步驟可以取得 secret。
+          dotnet build SpeechMessageProducts.sln -c Release --no-restore -warnaserror:NU1605
+          if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
+          foreach ($project in @('tools/I18nCheck.Tests', 'tools/PluginPackaging.Tests')) {
+            dotnet build $project -c Release --no-restore -warnaserror:NU1605
+            if ($LASTEXITCODE -ne 0) { throw 'Tool build failed.' }
+          }
+      - name: Release solution tests and coverage
+        run: |
+          dotnet test SpeechMessageProducts.sln -c Release --no-build --collect:"XPlat Code Coverage" --results-directory artifacts/ci/solution-tests
+          if ($LASTEXITCODE -ne 0) { throw 'Release solution tests failed.' }
+      - name: Debug tracer contracts
+        run: |
+          dotnet test ToolUtility.Dataverse.Tests -c Debug --filter "FullyQualifiedName~FileToolUtilityTracerTests"
+          if ($LASTEXITCODE -ne 0) { throw 'Debug tracer tests failed.' }
+      - name: Coverage for each new module and target
+        run: |
+          # 各目標分開收集，禁止 net10 報告或其他測試的相依模組掩蓋 net48 的缺口。
+          $cases = @(
+            @{ Project='SpeechMessage.Localization.Tests'; Framework='net48'; Module='SpeechMessage.Localization' },
+            @{ Project='SpeechMessage.Localization.Tests'; Framework='net10.0'; Module='SpeechMessage.Localization' },
+            @{ Project='LineMessagingProcessor.Tests'; Framework='net48'; Module='LineMessagingProcessor.Core' },
+            @{ Project='LineMessagingProcessor.Tests'; Framework='net10.0'; Module='LineMessagingProcessor.Core' },
+            @{ Project='SpeechMessage.Dynamics.Plugins.Tests'; Framework='net48'; Module='SpeechMessage.Dynamics.Plugins' },
+            @{ Project='SpeechMessage.Dynamics.Deployment.Tests'; Framework='net10.0'; Module='SpeechMessage.Dynamics.Deployment' },
+            @{ Project='tools/I18nCheck.Tests'; Framework='net10.0'; Module='I18nCheck' },
+            @{ Project='tools/PluginPackaging.Tests'; Framework='net10.0'; Module='PluginPackaging' }
+          )
+          foreach ($case in $cases) {
+            $results = Join-Path 'artifacts/ci/coverage' ($case.Module + '/' + $case.Framework)
+            if (Test-Path -LiteralPath $results) { throw 'Coverage output must be fresh for every run.' }
+            $include = 'DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include=[' + $case.Module + ']*'
+            dotnet test $case.Project -c Release -f $case.Framework --no-build --collect:"XPlat Code Coverage" --results-directory $results -- $include
+            if ($LASTEXITCODE -ne 0) { throw 'Module coverage tests failed.' }
+            $reports = @(Get-ChildItem -LiteralPath $results -Filter coverage.cobertura.xml -File -Recurse)
+            if ($reports.Count -ne 1) { throw 'Expected exactly one fresh report for this module and target.' }
+            $coverage = & powershell -NoProfile -File tools/RepoQuality/Test-Coverage.ps1 -ReportPath $reports[0].FullName -Module $case.Module
+            if ($LASTEXITCODE -ne 0) { throw 'Module coverage is below the required threshold or invalid.' }
+            $coverage | Write-Output
+            ($case.Framework + ' ' + ($coverage -join ' ')) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
+          }
+      - name: Repository quality gates
+        run: |
+          powershell -NoProfile -File tools/RepoQuality/Test-TextEncoding.ps1 -Scope Changed -Base $env:BASE_COMMIT
+          if ($LASTEXITCODE -ne 0) { throw 'Text encoding failed.' }
+          powershell -NoProfile -File tools/RepoQuality/Find-DuplicateCode.ps1 -Baseline tools/RepoQuality/duplicate-baseline.json
+          if ($LASTEXITCODE -ne 0) { throw 'Duplicate source ratchet failed.' }
+          powershell -NoProfile -File tools/RepoQuality/Test-PackageVersions.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'Package version guard failed.' }
+          powershell -NoProfile -File tools/RepoQuality/Test-AnalyzerProbe.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'Analyzer probes failed.' }
+          dotnet run --project tools/I18nCheck -- verify
+          if ($LASTEXITCODE -ne 0) { throw 'I18n verification failed.' }
+      - name: Repository quality tests
+        run: |
+          Import-Module Pester -RequiredVersion 3.4.0 -ErrorAction Stop
+          $result = Invoke-Pester -Script @('tools/RepoQuality/Tests/Test-TextEncoding.Tests.ps1', 'tools/RepoQuality/Tests/Find-DuplicateCode.Tests.ps1') -PassThru
+          if ($result.TotalCount -lt 35 -or $result.FailedCount -ne 0) { throw 'Repository quality tests failed or were not discovered.' }
+          powershell -NoProfile -File tools/RepoQuality/Tests/Test-P002Settings.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'SDK and ignore settings failed.' }
+          powershell -NoProfile -File tools/RepoQuality/Tests/Test-Coverage.Tests.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'Coverage gate tests failed.' }
+          powershell -NoProfile -File tools/RepoQuality/Tests/Test-SixLaborsCiLicense.Tests.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'Six Labors CI license contracts failed.' }
+          node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs
+          if ($LASTEXITCODE -ne 0) { throw 'Progress page tests failed.' }
+          # 直接驗證實際 Razor adapter 與既有 jQuery Deferred，避免只跑伺服器測試卻漏掉表格初始化錯誤。
+          node --test ChurchReport.MemberInfo.Tests/JavaScript/collection-load-coordinator.test.js ChurchReport.MemberInfo.Tests/JavaScript/weekly-report-load-adapter.test.js
+          if ($LASTEXITCODE -ne 0) { throw 'Weekly report JavaScript contracts failed.' }
+          powershell -NoProfile -File docs/scripts/Tests/Test-CcgReviewSummary.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'CCG summary contracts failed.' }
+      - name: Offline plugin packaging tests
+        run: |
+          # 此處只建立與銷毀一次性測試金鑰，從不需要 GitHub 正式 secret。
+          powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
+          if ($LASTEXITCODE -ne 0) { throw 'Plugin metadata samples failed.' }
+          powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1 -UseEphemeralTestKey
+          if ($LASTEXITCODE -ne 0) { throw 'Plugin build samples failed.' }
+      - name: Prepare optional signing key
+        id: signing
+        env:
+          PLUGIN_SNK_BASE64: ${{ secrets.PLUGIN_SNK_BASE64 }}
+        run: |
+          # 無 secret 時只標示跳過，不能影響一般建置；本計畫不要求建立 GitHub secret。
+          if ([string]::IsNullOrWhiteSpace($env:PLUGIN_SNK_BASE64)) {
+            'Plugin packaging: skipped (PLUGIN_SNK_BASE64 is not configured).' | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
+            'enabled=false' | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
+          } else {
+            # 僅在 runner 暫存目錄還原；內容不輸出、不上傳。清理步驟以固定路徑獨立執行。
+            $keyPath = Join-Path $env:RUNNER_TEMP 'speechmessage-plugin-signing.snk'
+            if (Test-Path -LiteralPath $keyPath) { throw 'Signing key destination is unexpectedly occupied.' }
+            $bytes = $null
+            try {
+              $bytes = [Convert]::FromBase64String($env:PLUGIN_SNK_BASE64)
+              [IO.File]::WriteAllBytes($keyPath, $bytes)
+            } finally {
+              if ($null -ne $bytes) { [Array]::Clear($bytes, 0, $bytes.Length) }
+              $env:PLUGIN_SNK_BASE64 = $null
+            }
+            'enabled=true' | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
+          }
+      - name: Explicit signed plugin packaging
+        if: steps.signing.outputs.enabled == 'true'
+        run: |
+          $env:SPEECHMESSAGE_PLUGIN_KEYFILE = Join-Path $env:RUNNER_TEMP 'speechmessage-plugin-signing.snk'
+          try {
+            dotnet build SpeechMessage.Dynamics.Plugins -c Release -p:SpeechMessagePackagePlugin=true
+            if ($LASTEXITCODE -ne 0) { throw 'Explicit plugin packaging failed.' }
+            powershell -NoProfile -File tools/PluginPackaging/Test-PluginAssembly.ps1
+            if ($LASTEXITCODE -ne 0) { throw 'Signed plugin assembly validation failed.' }
+            'Plugin packaging: signed assembly validation passed.' | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
+          } finally { $env:SPEECHMESSAGE_PLUGIN_KEYFILE = $null }
+      - name: Delete optional signing key
+        if: always()
+        run: |
+          # 單一固定暫存檔，禁止遞迴刪除，也不接收外部指定路徑。
+          $keyPath = Join-Path $env:RUNNER_TEMP 'speechmessage-plugin-signing.snk'
+          if (Test-Path -LiteralPath $keyPath) { Remove-Item -LiteralPath $keyPath -Force }
+      - name: Delete Six Labors license
+        if: always()
+        run: |
+          $ErrorActionPreference = 'Stop'
+          try {
+            if (-not [string]::IsNullOrWhiteSpace($env:SixLaborsLicenseFile)) {
+              # 只清除本步驟發布於 runner 根目錄的特定格式檔名；不得依任意路徑刪檔。
+              $runnerRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\', '/')
+              $licensePath = [IO.Path]::GetFullPath($env:SixLaborsLicenseFile)
+              if (-not [string]::Equals([IO.Path]::GetDirectoryName($licensePath), $runnerRoot, [StringComparison]::OrdinalIgnoreCase) -or
+                  [IO.Path]::GetFileName($licensePath) -cnotmatch '^speechmessage-sixlabors-[0-9a-f]{32}\.lic$') {
+                throw 'Refusing to delete an unowned license path.'
+              }
+              if ([IO.File]::Exists($licensePath)) { Remove-Item -LiteralPath $licensePath -Force }
+            }
+          } finally { $env:SixLaborsLicenseFile = $null }
+      - name: Upload coverage reports
+        if: always()
+        uses: actions/upload-artifact@v4
+        with:
+          name: coverage-reports
+          path: artifacts/ci/**/coverage.cobertura.xml
+          if-no-files-found: warn
+          retention-days: 14

diff --git a/.github/workflows/toolutility-tests.yml b/.github/workflows/toolutility-tests.yml
deleted file mode 100644
index c1d01b210..000000000
--- a/.github/workflows/toolutility-tests.yml
+++ /dev/null
@@ -1,69 +0,0 @@
-name: ToolUtility Tests CI
-
-on:
-  push:
-    branches: [ "Sunny_MyPay_2.7_Utility_.Net10", "main" ]
-    paths:
-      - 'ToolUtility/**'
-      - 'ToolUtility.Tests/**'
-  pull_request:
-    branches: [ "Sunny_MyPay_2.7_Utility_.Net10", "main" ]
-    paths:
-      - 'ToolUtility/**'
-      - 'ToolUtility.Tests/**'
-
-jobs:
-  test:
-    runs-on: windows-latest
-    
-    steps:
-    - name: Checkout code
-      uses: actions/checkout@v4
-      
-    - name: Setup .NET
-      uses: actions/setup-dotnet@v4
-      with:
-        dotnet-version: '8.0.x'
-        
-    - name: Restore dependencies
-      run: dotnet restore ToolUtility.Tests/ToolUtility.Tests.csproj
-      
-    - name: Build
-      run: dotnet build ToolUtility.Tests/ToolUtility.Tests.csproj --no-restore --configuration Release
-      
-    - name: Run Tests
-      run: dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj --no-build --configuration Release --verbosity normal --collect:"XPlat Code Coverage" --results-directory ./coverage
-      
-    - name: Generate Coverage Report
-      if: always()
-      run: |
-        dotnet tool install --global dotnet-reportgenerator-globaltool
-        reportgenerator -reports:./coverage/**/coverage.cobertura.xml -targetdir:./coverage/report -reporttypes:Html
-      
-    - name: Upload Coverage to Codecov
-      if: always()
-      uses: codecov/codecov-action@v3
-      with:
-        files: ./coverage/**/coverage.cobertura.xml
-        flags: unittests
-        name: codecov-umbrella
-        fail_ci_if_error: false
-        
-    - name: Upload Test Results
-      if: always()
-      uses: actions/upload-artifact@v3
-      with:
-        name: test-results
-        path: ./coverage
-        
-    - name: Check Code Coverage Threshold
-      if: always()
-      run: |
-        $coverage = Select-String -Path ./coverage/**/coverage.cobertura.xml -Pattern 'line-rate="([0-9.]+)"' | ForEach-Object { [double]$_.Matches.Groups[1].Value * 100 }
-        Write-Host "Code Coverage: $coverage%"
-        if ($coverage -lt 85) {
-          Write-Error "? Code coverage ($coverage%) is below threshold (85%)"
-          exit 1
-        } else {
-          Write-Host "? Code coverage ($coverage%) meets threshold (85%)"
-        }

diff --git a/.gitignore b/.gitignore
index 93ca95ba..2309d535 100644
--- a/.gitignore
+++ b/.gitignore
@@ -1,4 +1,4 @@
-﻿## Ignore Visual Studio temporary files, build results, and
+## Ignore Visual Studio temporary files, build results, and
 ## files generated by popular Visual Studio add-ons.
 
 # User-specific files
@@ -43,6 +43,7 @@ dlldata.c
 # DNX
 project.lock.json
 project.fragment.lock.json
+# 封裝與建置產物可由來源重建，避免將輸出檔及暫存資產納入版控。
 artifacts/
 
 *_i.c
@@ -267,3 +268,9 @@ __pycache__/
 .worktrees/
 .ccg/tmp/
 
+# 參考用舊方案容量大且不是本次正式來源，避免將整份副本納入版控。
+.其他解決方案/
+# 強式名稱私鑰必須保存在 repo 外，禁止將簽章金鑰提交到版控。
+*.snk
+# 原始測試結果含機器名稱與本機路徑，只提交整理後的文字摘要。
+*.trx

diff --git a/.trellis/spec/backend/ccg-review-summary-contract.md b/.trellis/spec/backend/ccg-review-summary-contract.md
new file mode 100644
index 00000000..41471bde
--- /dev/null
+++ b/.trellis/spec/backend/ccg-review-summary-contract.md
@@ -0,0 +1,50 @@
+# CCG Review Summary Contract
+
+## 1. Scope / Trigger
+
+Applies when a consumer interprets the output of the CCG self-healing runner. A successful requested single-model run must never be presented as a completed dual-model review.
+
+## 2. Signatures
+
+- Entry: docs/scripts/Start-CcgDualModelRun.ps1; optional -ClaudeOnly selects Claude explicitly.
+- Metadata: Get-CcgReviewCompletion -ClaudeOnly:<bool> -CompletedBackends <string[]>.
+- Rendering: ConvertTo-CcgRunMarkdown -Summary <record>.
+
+## 3. Contracts
+
+- reviewMode is dual or claude-only.
+- ok means every requested backend produced verified usable output. It does not identify dual-model completion.
+- dualModelComplete is true only when both gemini and claude completed with usable output. Consumers must use this field to claim dual-model completion.
+- summary.json and summary.md must both expose reviewMode and dualModelComplete.
+- diagnostic retains the failing wrapper's own diagnostic; probeDiagnostic and probe separately identify the independent probe result and its artifact path.
+- Pure metadata functions retain no prompts, identity, credentials, tasks or process handles. All state belongs to the current call.
+- A user-delivered manual review is recorded as Claude 單模型人工審查（使用者交付） with the reviewed diff SHA256. It must not change a failed automatic run into a successful one.
+
+## 4. Validation / Error Matrix
+
+| Requested / completed | ok | dualModelComplete | Result |
+|---|---|---|---|
+| dual / gemini and claude | true | true | Completed dual review |
+| claude-only / claude | true | false | 單模型（使用者指定 ClaudeOnly） |
+| dual / claude only | false | false | Incomplete or explicit quota fallback |
+| claude-only / none | false | false | Incomplete review |
+
+Exit code 0 can also represent accepted quota fallback; it is never a substitute for dualModelComplete. A successful probe does not make the original failed review successful.
+
+## 5. Good / Base / Bad Cases
+
+- Good: display the selected reviewMode and inspect dualModelComplete independently.
+- Base: preserve a usable Claude-only result without invoking Gemini when the user selected ClaudeOnly.
+- Bad: claim both models completed because ok or the process exit code indicates success.
+
+## 6. Required Tests
+
+Run powershell -NoProfile -File docs/scripts/Tests/Test-CcgReviewSummary.ps1. Its six cases verify complete dual output, complete explicit single output, partial output, missing output, JSON metadata and Markdown metadata without invoking external models.
+
+Phase 0 runs external review once after P0-11 and before P0-12. Between work packages, use local validation. If the stage-end entrypoint fails or times out, preserve the review packet and stop for the user's review result.
+
+## 7. Wrong / Correct
+
+Wrong: treat summary.ok or exit code 0 as proof that Gemini and Claude both completed.
+
+Correct: claim dual completion only when summary.dualModelComplete is true; otherwise describe the actual reviewMode or fallback state explicitly.

diff --git a/.trellis/spec/backend/central-package-management.md b/.trellis/spec/backend/central-package-management.md
new file mode 100644
index 00000000..2c980cc1
--- /dev/null
+++ b/.trellis/spec/backend/central-package-management.md
@@ -0,0 +1,67 @@
+# Central Package Management
+
+## 1. Scope / Trigger
+
+Use this contract when adding or changing an actual program project or package.
+It includes projects outside the solution and existing orphan projects. The user
+approved excluding `docs/` reference snapshots and `.ccg/tasks/` task archives.
+
+## 2. Signatures
+
+```powershell
+powershell.exe -NoProfile -File tools/RepoQuality/Test-PackageVersions.ps1
+powershell.exe -NoProfile -File tools/RepoQuality/Test-PackageVersions.ps1 -RepositoryPath <worktree-root>
+dotnet restore SpeechMessageProducts.sln
+dotnet build SpeechMessageProducts.sln -c Release -warnaserror:NU1605
+```
+
+## 3. Contracts
+
+- `Directory.Packages.props` owns active package versions. Program projects use
+  versionless `PackageReference` items; the guard checks attributes and children.
+- All `Microsoft.Extensions.*` versions are `10.0.0`; Newtonsoft.Json is `13.0.4`.
+  Other active versions stay unchanged unless a later plan explicitly changes them.
+- Inherited CPM is explicitly false beneath the two snapshot roots. Their historic
+  inline versions remain untouched. Normalize directory boundaries before testing
+  prefixes so a project in `docs-like/` remains a program project.
+- Parse XML nodes, not source lines. Commented ASP.NET Core 2.2 dependencies are
+  disabled and must not become active central package entries.
+- Preserve `PrivateAssets`, `IncludeAssets`, conditions, and unrelated warnings,
+  including the existing `System.Text.Encoding.CodePages` `NoWarn="NU1510"`.
+- No credentials or network access are needed for the guard. Git enumeration is
+  bounded to 30 seconds and its process/redirected streams are disposed per run.
+
+## 4. Validation & Error Matrix
+
+| Condition | Result |
+| --- | --- |
+| Active PackageReference has Version attribute/child | Exit 1, InlineVersion |
+| Active NoWarn element/attribute contains NU1605 | Exit 1, SuppressedNU1605 |
+| Only XML comments contain these constructs | Ignore comments |
+| A project is beneath either snapshot root | Exclude from guard; CPM false |
+| No program projects or invalid project XML | Exit 1; no empty success |
+| Legacy CP950 project comments | Read-only strict CP950 fallback; encoding remains owned by the encoding gate |
+| NU1605/NU1608 during restore | Gate fails; never add NoWarn to make it pass |
+
+## 5. Good / Base / Bad Cases
+
+- Good: `ToolUtility.Tests` stays outside the solution during P0-03 but uses CPM.
+- Base: a new tool project uses a versionless package reference already in the table.
+- Bad: grepping XML comments invents active legacy dependencies, or a solution-only
+  scan overlooks an actual project outside the solution.
+
+## 6. Tests Required
+
+Run the guard before and after migration and retain the real failure diagnostics.
+Verify evaluated MSBuild properties: actual project CPM true, both snapshots false.
+Restore without NU1605/NU1608, build without increasing baseline warnings, run the
+complete Release suite and the Debug FileToolUtilityTracerTests contract.
+Compare package metadata and excluded snapshot bytes to their pre-change values.
+
+## 7. Wrong vs Correct
+
+Wrong: `<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />` in a
+program project, or suppressing NU1605 after dependency changes.
+
+Correct: `<PackageReference Include="Newtonsoft.Json" />`, with version `13.0.4`
+in the central table, while leaving the two excluded snapshots unchanged.

diff --git a/.trellis/spec/backend/duplicate-row-publication-contract.md b/.trellis/spec/backend/duplicate-row-publication-contract.md
index 8588db77..9e00809d 100644
--- a/.trellis/spec/backend/duplicate-row-publication-contract.md
+++ b/.trellis/spec/backend/duplicate-row-publication-contract.md
@@ -6,6 +6,8 @@ Apply this contract whenever code creates, caches, transforms, serializes, rende
 exports, or mutates a repeatable data collection. It covers ASP.NET Core actions,
 DevExtreme grids, Razor tables, trees, reports, JSON DTOs, background results,
 Dataverse projections, and any future list-like UI.
+Client adapters must preserve the actual consumer's asynchronous completion
+contract while enforcing the publication generation.
 
 The protected failure is not merely two equal display names. The failure is two
 rows representing one authoritative record/business event, a partially published
@@ -26,6 +28,11 @@ GetOrBuildDetachedRead(
     -> DetachedReadSnapshot
 ```
 
+```javascript
+coordinator.runLoad(loadFactory);              // Native Promise for date navigation.
+coordinator.runLoad(loadFactory, $.Deferred);  // jQuery promise for DevExtreme Grid.
+```
+
 Each repeatable row exposes one server-owned stable identity such as
 `PresentRecordId`, `ContactId` only when the contact itself is the row, or a
 server-generated business-event key. UI components configure that same field as
@@ -63,6 +70,17 @@ their row key.
   the mutation inside the bounded request, or hand off only detached immutable
   input to a separately owned, cancellable, observable worker with deterministic
   drain and disposal.
+- DevExtreme's existing jQuery store adapter must return a read-only jQuery
+  promise with `done`, `fail`, and `always`. Forward every original completion
+  argument and context, including `(data, extra)` where `extra` carries
+  `totalCount`, `summary`, and `groupCount`. Do not assimilate that transport
+  through a native Promise, which discards extra arguments.
+- Both completion paths validate generation. Clear the active transport before
+  invoking jQuery callbacks because jQuery 2.x invokes them synchronously and
+  consumers may reenter or throw. Preserve the native Promise default for
+  existing `.then().catch()` date navigation.
+- Version the coordinator script URL when the layout serves immutable static
+  assets. A new Razor adapter must not silently run against cached old JS.
 
 ## 4. Validation & Error Matrix
 
@@ -76,6 +94,9 @@ their row key.
 | Caller mutates returned list | Published snapshot remains unchanged |
 | Concurrent same-scope requests | At most one build; all successful readers see one complete generation |
 | Concurrent different-scope requests | No mutable state, credential, row, or authorization decision crosses scopes |
+| jQuery store resolves `(rows, extra)` | Deliver both arguments through the guarded promise; preserve paging metadata |
+| Deferred consumer throws or synchronously starts another load | Old active transport is already released; keep the new generation active |
+| Old owner completes after disposal | Reject with stale cancellation; never publish the old rows |
 | Existing database/Dataverse conflict count > 1 | Stop creating; report conflict for auditable remediation |
 
 ## 5. Good / Base / Bad Cases
@@ -90,6 +111,8 @@ their row key.
   while silently deleting a legitimate person.
 - Bad: an in-process semaphore is treated as the uniqueness guarantee for two IIS
   workers writing the same Dataverse business event.
+- Bad: returning a native Promise from a jQuery-based store adapter makes
+  DevExtreme's `.load(...).done(...)` throw before the grid can render.
 
 ## 6. Tests Required
 
@@ -110,6 +133,14 @@ their row key.
   retained memory does not trend upward under repeated generations.
 - Cross-process writer test where applicable: repeated idempotency key from two
   application instances yields one canonical active business record.
+- Execute the actual Razor adapter with the repository's real jQuery
+  Callbacks/Deferred implementation. Assert consumer-visible completion,
+  metadata, original errors, synchronous reentry, late success/error,
+  independent owners, and repeated dispose/drain. CI runs:
+
+```text
+node --test ChurchReport.MemberInfo.Tests/JavaScript/collection-load-coordinator.test.js ChurchReport.MemberInfo.Tests/JavaScript/weekly-report-load-adapter.test.js
+```
 
 ## 7. Wrong vs Correct
 
@@ -135,3 +166,8 @@ return DataSourceLoader.Load(candidate.CreateDetachedRows(), options);
 The real implementation may use a scoped holder, immutable snapshot, or another
 equivalent mechanism, but it must preserve the same isolation, atomic publication,
 stable identity, retry, cleanup, and performance contracts.
+
+For the existing jQuery Grid, return
+`coordinator.runLoad(() => originalLoad(options), $.Deferred)`, rather than
+wrapping the native result in `$.when(...)`: a late wrapper restores `.done`
+but cannot restore metadata already lost during native Promise assimilation.

diff --git a/.trellis/spec/backend/duplicate-source-ratchet.md b/.trellis/spec/backend/duplicate-source-ratchet.md
new file mode 100644
index 00000000..d57b1355
--- /dev/null
+++ b/.trellis/spec/backend/duplicate-source-ratchet.md
@@ -0,0 +1,9 @@
+# Duplicate Source Ratchet
+
+Run `powershell -NoProfile -File tools/RepoQuality/Find-DuplicateCode.ps1 -Baseline tools/RepoQuality/duplicate-baseline.json` from the current worktree after P0-06.
+
+- Enumerate only C# projects declared in the selected solution. Validate absolute repository boundaries before reading; never search parent worktrees or traverse reparse points. Exclude build output, generated C#, documentation/reference snapshots and other worktrees.
+- Content identity is normalized SHA256. Type identity is namespace plus class/record/struct declaration, with comment/string masking and block/file-scoped namespace tracking. Only cross-project groups count.
+- Baseline entries are stable kind/key identities with unique source locations. Reject repeated baseline group identities and locations before using the dictionary. New groups or additional locations fail; removed locations produce BaselineStale reminders.
+- CLI invocation owns all scan state. Keep only one source text at a time (16 MiB limit), then retain metadata only. Existing legacy encodings may be decoded strictly for read-only inventory; the encoding gate owns publishing rules.
+- Tests use isolated synthetic solutions and both PowerShell engines. Assert specific diagnostics, not merely any nonzero exit. Never change products to satisfy the scanner or rewrite existing source as part of baseline creation.

diff --git a/.trellis/spec/backend/index.md b/.trellis/spec/backend/index.md
index 9b724862..5515a713 100644
--- a/.trellis/spec/backend/index.md
+++ b/.trellis/spec/backend/index.md
@@ -18,7 +18,17 @@ This directory contains guidelines for backend development. Fill in each file wi
 | [Database Guidelines](./database-guidelines.md) | ORM patterns, queries, migrations | To fill |
 | [Error Handling](./error-handling.md) | Independent Exception.log / LINE switches; both enabled flush before LINE in Debug and Release | Established |
 | [Quality Guidelines](./quality-guidelines.md) | Code standards, forbidden patterns | To fill |
+| [CCG Review Summary Contract](./ccg-review-summary-contract.md) | Requested-backend success, explicit review mode, dual completion and independent probe diagnostics | Established |
+| [Central Package Management](./central-package-management.md) | Actual project scope, snapshot opt-out, XML-aware version guard, and restore validation | Established |
+| [Strict Analyzer Contract](./strict-analyzer-contract.md) | Late opt-in evaluation, exact diagnostic probes, legacy isolation, and net48 polyfills | Established |
+| [Solution CI Contract](./solution-ci-contract.md) | Offline Windows checks, per-target coverage, optional signing and temp-key cleanup | Established |
+| [Deployment CLI Contract](./deployment-contract.md) | User Secrets-only isolated commands, metadata plans, safe diagnostics and offline verification | Established |
+| [Plugin Packaging Contract](./plugin-packaging-contract.md) | Separate four-input merge, process-environment signing, offline metadata checks and sandbox exception ownership | Established |
+| [Text Encoding Contract](./text-encoding-contract.md) | Tracked-file boundaries, byte/token checks, lossless conversion, and safe output ownership | Established |
+| [Development Configuration Safety](./development-configuration-safety.md) | Development-only providers, baseline token rejection, test organization guard and Production compatibility | Established |
+| [Duplicate Source Ratchet](./duplicate-source-ratchet.md) | Solution-scoped content/type duplicates, baseline identity validation and no parent-worktree traversal | Established |
 | [Logging Guidelines](./logging-guidelines.md) | Structured logging, log levels, and unified diagnostic trace contract | Established |
+| [Image Input Contract](./image-input-contract.md) | Restricted image formats, pre-decode size limits, safe rejection and bounded ownership | Established |
 | [MemberInfo Tree And Grid Contract](./member-info-tree-contract.md) | Cross-layer MemberInfo authorization, DTO, paging, sorting, cache, and CRM batching rules | Established |
 | [Small Group Background Upload Isolation](./small-group-background-upload-isolation.md) | SaveIntegrate background snapshot, refresh response, resource ownership, and regression contract | Established |
 | [Duplicate Row Publication Contract](./duplicate-row-publication-contract.md) | Stable row identity, atomic snapshot publication, same-name preservation, idempotent writes, isolation, and lifecycle tests | Established |

diff --git a/.trellis/spec/backend/solution-ci-contract.md b/.trellis/spec/backend/solution-ci-contract.md
new file mode 100644
index 00000000..ce6365e6
--- /dev/null
+++ b/.trellis/spec/backend/solution-ci-contract.md
@@ -0,0 +1,91 @@
+# Solution CI Contract
+
+## Scope
+
+P0-11 runs on Windows with the SDK from global.json and full Git history.
+All builds and tests are offline with respect to D365 and LINE. Hosted Actions
+are not executed by local verification and must never be reported as such.
+
+## Commands and evidence
+
+The authoritative command sequence is .github/workflows/solution-ci.yml.
+Run every applicable run block locally before committing a change to that
+sequence. Preserve sanitized summaries in the active WP evidence directory;
+raw coverage, build logs, temporary scripts and DLLs belong under ignored
+artifacts only. Never commit TRX or signing keys.
+
+## Coverage gate
+
+Test-Coverage.ps1 -ReportPath <coverage.cobertura.xml> -Module <assembly>
+requires exactly one ordinal matching package and a finite invariant decimal
+line-rate in [0,1], at least 0.8 without rounding. Missing reports, missing or
+duplicate modules, malformed XML and external entities fail closed. The XML
+reader is bounded to 32 MiB characters, has no resolver, and is disposed before
+returning. The tool does not load product assemblies or contact any service.
+
+Collect each new module and each target framework separately into a fresh
+directory; require exactly one report there. Do not select the best result from
+multiple runs or let a net10 report satisfy net48. The complete solution test
+run remains mandatory; targeted reruns provide isolated coverage for new code.
+Include I18nCheck.Tests and PluginPackaging.Tests, which are outside the solution.
+Add new modules to the explicit coverage matrix as they enter future WPs.
+
+## Signing
+
+Ordinary Release builds receive no key and do not package. The workflow reads
+PLUGIN_SNK_BASE64 only in its optional preparation step. With no secret, it writes
+an explicit skipped summary and does not execute the package target. Do not create
+a GitHub secret or request an upload without separate user authorization.
+
+A configured secret is decoded only into runner temp; clear the byte buffer,
+never print its content, and always delete the fixed temporary key in a separate
+if: always() step. Only the signed packaging step sets the process variable
+SPEECHMESSAGE_PLUGIN_KEYFILE and explicitly opts into SpeechMessagePackagePlugin.
+Only coverage XML is uploaded, never runner temp, DLLs or keys.
+
+If `PLUGIN_SNK_BASE64` is configured in the future, it must be a GitHub
+Environment secret restricted to protected branches and requiring approval.
+Never place it in repository secrets: this workflow runs on pushes to all
+branches, so a repository secret would be available to any branch workflow.
+Whether to create that Environment secret remains a user decision.
+
+Offline build contract samples use -UseEphemeralTestKey explicitly. This mode
+creates a disposable RSA fixture in a unique temp directory, never reads the
+formal key contents, restores the caller environment, and deletes the fixture.
+Without the switch, P0-10's formal process-environment requirement remains intact.
+
+## Tests
+
+Keep coverage rejection cases and both signed/unsigned metadata samples.
+Run Pester 3.4.0 for the existing encoding and duplicate tests, the relocated
+Test-P002Settings.ps1, progress rendering, CCG summary semantics, analyzer probes,
+central package guard, Debug tracer, I18n verification and plugin fixture tests.
+Do not treat zero discovered tests or an arbitrary failure as a successful guard.
+
+## Six Labors general license (user approved 2026-11-03)
+
+SIXLABORS_LICENSE is a required, user-configured repository secret for this
+workflow. This specific authorization is separate from the future Environment
+restriction on the Plugin signing key. Pass it only through the preparation
+step environment, never by interpolating its value into run script text.
+Missing or whitespace content fails with a clear message before building.
+
+The preparation block creates a unique speechmessage-sixlabors-<guid>.lic in
+RUNNER_TEMP with CreateNew and UTF-8 without BOM. Dispose the writer and stream
+before publishing only its path through GITHUB_ENV as SixLaborsLicenseFile.
+Clear the preparation process secret in finally; do not print its content or
+write it to summaries, GITHUB_ENV, or artifacts. A preparation/publication fault
+removes the file it created and emits a fixed diagnostic without raw messages.
+
+A separate if: always() block deletes the published file after success or
+failure. Validate the direct runner directory and workflow-owned filename
+before deleting; never follow an arbitrary environment path or delete another
+file. Cleanup is idempotent and clears the local process path. Only coverage
+XML is uploaded. Local tests execute these actual workflow blocks with synthetic
+license text, including missing secret, publication failure and unowned-path
+rejection. They do not read or copy the real local license.
+
+Local product builds may use the user-level SixLaborsLicenseFile path only in
+the current process, after Test-Path confirms existence. Do not commit that
+path, inspect license contents, alter persistent environment settings, or claim
+hosted Actions execution from local verification.

diff --git a/.trellis/spec/backend/strict-analyzer-contract.md b/.trellis/spec/backend/strict-analyzer-contract.md
new file mode 100644
index 00000000..7285353f
--- /dev/null
+++ b/.trellis/spec/backend/strict-analyzer-contract.md
@@ -0,0 +1,113 @@
+# Strict Analyzer Contract
+
+## 1. Scope / Trigger
+
+New projects opt in with `SpeechMessageStrictCode=true` in their project body.
+Legacy projects keep their existing settings. The user approved placing the
+conditional configuration in `Directory.Build.targets`, because props evaluate
+before the project-body opt-in exists.
+
+## 2. Signatures
+
+```powershell
+powershell.exe -NoProfile -File tools/RepoQuality/Test-AnalyzerProbe.ps1
+```
+
+Optional parameters: `-RepositoryPath <worktree-root>` and
+`-EvidenceDirectory <diagnostic-directory>`. Exit 0 means every contract passed;
+the intentionally invalid probe builds themselves must fail.
+
+## 3. Contracts
+
+- Strict projects enable NET analyzers, Recommended analysis, nullable, latest
+  language version, and warnings as errors in Directory.Build.targets.
+- Strict projects set `NuGetAuditMode=all` in `Directory.Build.targets`, including
+  net48 restore evaluation. The SDK default for net48 checks only direct
+  dependencies, which leaves transitive vulnerabilities outside the strict gate.
+  Probe both net48 and net10.0 evaluated values and perform a real restore to
+  prove the property takes effect before package auditing.
+- Explicitly default an empty AnalysisLevel to latest inside the strict group.
+  The SDK otherwise leaves this empty for net48, so AnalysisMode=Recommended
+  alone never loads that framework's Recommended rules. This was reproduced and
+  repaired on 2026-11-03: before repair net48 naming violations compiled, while
+  net10 rejected them. Check nonempty EffectiveAnalysisLevel for both targets
+  and real net48 CA1707/CA1716 diagnostics, never merely the property label.
+- The user approved one permanent naming exception on 2026-11-03: only projects
+  with SpeechMessageStrictCode=true, IsTestProject=true, and an ordinal .Tests
+  project-name suffix receive NoWarn CA1707. Directory.Build.targets is the only
+  configuration source for that exception. Product projects and every other
+  diagnostic remain unchanged; do not repeat the setting in test csproj files.
+- Every strict csproj must set both SpeechMessageStrictCode=true and
+  GenerateDocumentationFile=true in its project body. The SDK derives
+  DocumentationFile and XML output copying before Directory.Build.targets is
+  imported; setting only the flag there gives a false success. Never set SDK
+  private properties such as _DocumentationFileProduced. The
+  ValidateSpeechMessageDocumentation target fails with SMQ0001 before
+  CoreCompile when a strict project has an empty DocumentationFile.
+- `build/StrictCode.globalconfig` actually enables CA1307, CA1309, CA1310 and
+  RS0030. WarningsAsErrors alone does not enable a disabled analyzer rule.
+- BannedApiAnalyzers is a private development dependency. Root BannedSymbols
+  blocks host-local time and process-default culture state. Plugin/Workflow
+  project names additionally load `build/Plugin/BannedSymbols.txt` for GDI.
+- Use property documentation IDs for CultureInfo default-culture properties.
+  Accessor method IDs did not match real assignments in version 5.6.0. The
+  property ban intentionally rejects both reads and writes of global defaults.
+- IsExternalInit and NotNullWhenAttribute are linked only for net48. Their
+  source files are shared; they introduce no state or runtime dependency.
+- Probe projects never execute. Per-run plugin fixtures are deleted after their
+  build process drains; the cleanup target must remain beneath the owned root.
+
+## 4. Validation & Error Matrix
+
+| Condition | Expected result |
+| --- | --- |
+| Default DateTime.Now probe, net10.0 or net48 | Only RS0030 errors |
+| Today / ToLocalTime / TimeZoneInfo.Local / default culture assignment | RS0030 |
+| Culture-sensitive comparison | CA1309 error |
+| IndexOf without explicit comparison | CA1310 error |
+| Ordinal Contains without explicit comparison overload | CA1307 error |
+| Plugin namespace probe uses System.Drawing | RS0030 |
+| Strict opt-in supplied only in project body | All strict properties enabled |
+| Existing ToolUtility project without opt-in | Original settings preserved |
+| Public member intentionally missing XML comments | CS1591 error |
+| Strict project missing the project-body documentation setting | SMQ0001 before compilation |
+| Documented positive build after deleting stale output XML | Successful build; output bin XML contains the documented type |
+| Strict product has a public method with underscores | Only CA1707 errors |
+| Strict test with IsTestProject=true and .Tests name uses underscores | Successful build |
+| IsTestProject=true but project name has no .Tests suffix | Only CA1707 errors |
+| .Tests suffix but IsTestProject=false | Only CA1707 errors |
+| net48 strict product uses underscores | Only CA1707 errors |
+| net48 public interface member is named Get | Only CA1716 errors |
+| net48 or net10.0 effective analysis level | Both AnalysisLevel and EffectiveAnalysisLevel are nonempty |
+| Restore, framework or unexpected compiler failure | Fail the guard; never count it as the expected rejection |
+
+## 5. Good / Base / Bad Cases
+
+- Good: the guard returns success because its intentional compile failures have
+  the exact required diagnostics, and legacy property checks pass.
+- Base: a new project without forbidden APIs builds under the strict settings.
+- Bad: setting StrictCode on the command line masks a broken import order, or
+  accepting any build failure mistakes missing net48 assets for analyzer proof.
+
+## 6. Tests Required
+
+Run Test-AnalyzerProbe's 26 checks, retaining all previous checks and adding
+two net48 naming probes, two effective-analysis-level checks, and the
+net48/net10.0 NuGetAuditMode checks.
+Check the actual nonempty DocumentationFile and
+the newly emitted XML in the output directory, rather than trusting only the
+GenerateDocumentationFile flag. Preserve the initial missing-analyzer red
+evidence and the final summary, then run the complete Release solution build,
+Release tests, Debug FileToolUtilityTracerTests, and the package-version guard.
+The probe is deliberately outside the solution and is not production code.
+
+## 7. Wrong vs Correct
+
+Wrong: a condition in Directory.Build.props reads a value first assigned later
+in the csproj, or a net10-only project is built with a net48 override and its
+NETSDK1005 failure is accepted as proof of the banned API rule.
+
+Correct: evaluate the opt-in in Directory.Build.targets while setting the XML
+documentation flag earlier in each csproj body. Declare both probe frameworks
+and build each explicitly. Reject unexpected diagnostic codes and verify both
+missing-comment rejection and a fresh successful XML output.

diff --git a/.trellis/spec/backend/text-encoding-contract.md b/.trellis/spec/backend/text-encoding-contract.md
new file mode 100644
index 00000000..d4f413f8
--- /dev/null
+++ b/.trellis/spec/backend/text-encoding-contract.md
@@ -0,0 +1,68 @@
+# Text Encoding Contract
+
+## 1. Scope / Trigger
+
+Use this contract before publishing source, configuration, Markdown, or script
+files, or when converting legacy text. P0-05 owns the guard and converter; later
+WP validation invokes the Changed scope against the phase base.
+
+## 2. Signatures
+
+```powershell
+powershell -NoProfile -File tools/RepoQuality/Test-TextEncoding.ps1 -Scope Changed -Base <ref>
+powershell -NoProfile -File tools/RepoQuality/Test-TextEncoding.ps1 -Scope All -ReportPath tools/RepoQuality/reports/encoding-baseline.md
+powershell -NoProfile -File tools/RepoQuality/Convert-TextEncoding.ps1 -Path <file> -ManifestPath <manifest> [-Apply]
+```
+
+## 3. Contracts
+
+- Published text is strict UTF-8 without BOM, CRLF, and final CRLF. U+FFFD,
+  invalid UTF-8, mixed/LF-only endings, and BOM are failures in Changed scope.
+- PowerShell executable tokens are ASCII; non-ASCII is allowed only in comment
+  tokens. Parse errors are failures. The check uses Git tracked paths for All,
+  never parent worktrees, build output, or external content reached through a junction/reparse point.
+- Timestamped `.ccg/dual-model-runs/<timestamp>-*/` raw CCG prompt/stdout/stderr is excluded from the
+  encoding gate and preserved as machine evidence; its exclusion is reported in
+  the count and does not alter its bytes.
+- Conversion defaults to dry-run. Apply requires an explicit switch, validates
+  encoding, literal segments, candidate bytes, and SHA-256 before atomic replace.
+  Each rejected file stays unchanged; each accepted file is replaced atomically. The manifest records metadata without source text.
+- Duplicate source paths and a manifest path equal to a source fail before any
+  write. Temporary files are owned by one invocation and removed after replacement
+  or failure.
+
+## 4. Validation & Error Matrix
+
+| Condition | Result |
+| --- | --- |
+| Valid UTF-8 no BOM CRLF | Changed check passes |
+| BOM/LF-only/invalid UTF-8/no final CRLF/U+FFFD | Changed check fails with named issue |
+| Non-ASCII PowerShell string/token | Fails; comment token is allowed |
+| All scope | Reads only `git ls-files --cached`; emits report and exit 0 for old issues |
+| CP950 or UTF BOM conversion | Dry-run by default; Apply writes UTF-8 only after literal checks |
+| Literal count/content changes, invalid bytes, duplicate source, or manifest collision | Exit 1; rejected source hashes remain unchanged |
+
+## 5. Good / Base / Bad Cases
+
+- Good: CP950 Chinese source converts to exact UTF-8 bytes and the manifest records
+  before/after hashes plus unchanged literal status.
+- Base: All inventory reports existing Trace CP950, Line.Messaging BOM, and other
+  legacy issues without changing them.
+- Bad: recursively scanning `.worktrees/`, treating an untracked outside file as
+  a repository source, or writing the first converted file before validating a
+  later duplicate path.
+
+## 6. Tests Required
+
+The Pester suite covers valid input, each byte failure, PowerShell token rules,
+tracked-only All scope, CP950/UTF BOM conversion, dry-run, literal mutation,
+invalid input, duplicate paths, manifest collision, and source immutability.
+Run the suite when changing the encoding tools or their tests; run the Changed gate before every remaining phase-0 package.
+
+## 7. Wrong vs Correct
+
+Wrong: use `Get-ChildItem -Recurse` from the worktree or decode CP950 with a
+replacement fallback and continue.
+
+Correct: enumerate Git tracked paths, use strict decoders, preserve raw CCG
+machine evidence by explicit path exclusion, and fail closed before writing.

diff --git a/.trellis/spec/guides/ccg-external-review-thinking-guide.md b/.trellis/spec/guides/ccg-external-review-thinking-guide.md
index a70ba3cb..9a7b000b 100644
--- a/.trellis/spec/guides/ccg-external-review-thinking-guide.md
+++ b/.trellis/spec/guides/ccg-external-review-thinking-guide.md
@@ -31,7 +31,7 @@ When a dual-model analysis/review fails:
    was typed directly.
 2. Run `Start-CcgDualModelRun.ps1` with the same role and repository path.
 3. Read the generated `summary.json`.
-4. If `ok=true`, continue the task from Gemini + Claude outputs.
+4. If `ok=true`, every requested backend completed. Only `dualModelComplete=true` confirms both Gemini and Claude completed; never infer this from `ok` or exit code 0. For `reviewMode=claude-only`, report 「單模型（使用者指定 ClaudeOnly）」 and use only the Claude output.
 5. If exit code is `2`, repair the local issue shown in the run folder and rerun the same runner.
 6. If `quotaBlocked=true`, stop calling it a tool failure; this is provider/session state.
    In this project, the owner has approved `-AllowSingleModelWhenQuotaBlocked` when one backend
@@ -92,7 +92,11 @@ Do not use Gemini with `--progress` on Windows unless the wrapper/Gemini crash p
 
 ## Failure Classification
 
-- `ok=true`: both backends completed.
+The executable metadata contract and regression command are in [CCG Review Summary Contract](../backend/ccg-review-summary-contract.md).
+
+- `ok=true`: all requested backends completed; this does not imply dual-model completion.
+- `dualModelComplete=true`: both Gemini and Claude completed with usable output; this is the sole dual-model completion indicator.
+- `reviewMode`: `dual` or `claude-only`. Report the latter as 「單模型（使用者指定 ClaudeOnly）」; it is not quota fallback or dual-model success.
 - `quotaBlocked=true`: external provider quota/session limit; not locally repairable.
 - `degradedFallback=true`: one backend completed, another backend was quota/session blocked,
   and `-AllowSingleModelWhenQuotaBlocked` was enabled. Continue the task, but report this as

diff --git a/BannedSymbols.txt b/BannedSymbols.txt
new file mode 100644
index 00000000..26e584d4
--- /dev/null
+++ b/BannedSymbols.txt
@@ -0,0 +1,7 @@
+P:System.DateTime.Now;改用 IChurchClock 或 IClock
+P:System.DateTime.Today;改用 IChurchClock.TodayInChurch
+M:System.DateTime.ToLocalTime;改用 IChurchClock.ToChurchTime
+P:System.TimeZoneInfo.Local;不得依賴伺服器時區
+// 分析器使用屬性的文件識別碼；setter 的方法識別碼不會命中屬性存取，故同時禁止全域預設文化讀寫。
+P:System.Globalization.CultureInfo.DefaultThreadCurrentCulture;不得讀寫程序全域預設文化
+P:System.Globalization.CultureInfo.DefaultThreadCurrentUICulture;不得讀寫程序全域預設介面文化

diff --git a/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj b/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
index aa2fcf2d..4842ab7e 100644
--- a/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
+++ b/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
@@ -5,17 +5,16 @@
     <Nullable>enable</Nullable>
     <IsPackable>false</IsPackable>
     <IsTestProject>true</IsTestProject>
-    <!-- 與主專案一致：允許 Newtonsoft.Json 等套件降級（NU1605）不擋建置 -->
-    <NoWarn>NU1605</NoWarn>
+    <!-- 套件版本由根目錄中央版本表統一；套件降版警告不得抑制。 -->
   </PropertyGroup>
   <ItemGroup>
-    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
-    <PackageReference Include="xunit" Version="2.6.6" />
-    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="xunit" />
+    <PackageReference Include="xunit.runner.visualstudio">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
-    <PackageReference Include="FluentAssertions" Version="6.12.0" />
+    <PackageReference Include="FluentAssertions" />
   </ItemGroup>
   <ItemGroup>
     <ProjectReference Include="..\SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj" />

diff --git a/ChurchReport.MemberInfo.Tests/Contracts/PublicationContractManifestTests.cs b/ChurchReport.MemberInfo.Tests/Contracts/PublicationContractManifestTests.cs
index ae1590b1..f437721e 100644
--- a/ChurchReport.MemberInfo.Tests/Contracts/PublicationContractManifestTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Contracts/PublicationContractManifestTests.cs
@@ -63,23 +63,33 @@ public sealed class PublicationContractManifestTests
     }
 
     /// <summary>
-    /// 從測試輸出目錄向上尋找 Solution root；方法只讀檔案系統且不保存路徑或檔案內容，
-    /// 搜尋完成後不留下 watcher、handle 或其他資源，因此不會造成測試生命週期洩漏。
+    /// 先從測試輸出定位最近的 SpeechMessageProducts.sln，再限定於該 worktree 查詢指定檔案。
+    /// 缺少方案或檔案必須立即失敗，不得讀取父 worktree 的舊副本或退回輸出目錄使缺檔測試假通過。
+    /// 方法僅查詢檔案資訊，路徑只屬於當次測試，不保存內容、watcher、handle 或跨案例狀態。
     /// </summary>
+    /// <param name="relativeSegments">可信任 manifest 或測試定義提供的方案相對路徑。</param>
+    /// <returns>最近方案根目錄內已確認存在的檔案路徑。</returns>
+    /// <exception cref="DirectoryNotFoundException">無法定位目前 worktree 的方案。</exception>
+    /// <exception cref="FileNotFoundException">指定檔案不在目前 worktree 中。</exception>
     private static string FindRepositoryFile(params string[] relativeSegments)
     {
         var directory = new DirectoryInfo(AppContext.BaseDirectory);
-        while (directory != null)
+        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
         {
-            var candidate = relativeSegments.Aggregate(directory.FullName, Path.Combine);
-            if (File.Exists(candidate))
-            {
-                return candidate;
-            }
-
             directory = directory.Parent;
         }
 
-        return Path.Combine(AppContext.BaseDirectory, Path.Combine(relativeSegments));
+        if (directory == null)
+        {
+            throw new DirectoryNotFoundException("找不到目前 worktree 的 SpeechMessageProducts.sln。");
+        }
+
+        var candidate = Path.Combine(directory.FullName, Path.Combine(relativeSegments));
+        if (!File.Exists(candidate))
+        {
+            throw new FileNotFoundException("目前 worktree 缺少 publication contract 檔案。", candidate);
+        }
+
+        return candidate;
     }
 }

diff --git a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelNamingTests.cs b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelNamingTests.cs
index 313b0de7..e142499d 100644
--- a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelNamingTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelNamingTests.cs
@@ -39,11 +39,15 @@ public sealed class DonationPaymentFormModelNamingTests
     private static readonly string LegacyModelToken = "Q" + "pay";
     private static readonly string LowerProviderBrandToken = "q" + "pay";
 
+    /// <summary>
+    /// 保護中性表單模型是唯一產品狀態型別。
+    /// 在實際產品組件同時查詢新名稱及舊供應商名稱，偵測新型別遺失或舊別名回流；斷言新型別存在而舊型別不存在。
+    /// </summary>
     [Fact]
     public void Donation_payment_form_model_is_the_primary_churchreport_form_state_type()
     {
-        var formModelType = Type.GetType("ChurchReport.Models.DonationPaymentFormModel, ChurchReport");
-        var legacyModelType = Type.GetType($"ChurchReport.Models.{LegacyModelToken}Model, ChurchReport");
+        var formModelType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Models.DonationPaymentFormModel");
+        var legacyModelType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType($"ChurchReport.Models.{LegacyModelToken}Model");
 
         formModelType.Should().NotBeNull(
             "奉獻付款表單是 ChurchReport 的產品層狀態，應使用 DonationPaymentFormModel 這種中性名稱，" +
@@ -54,14 +58,26 @@ public sealed class DonationPaymentFormModelNamingTests
             "同一個 solution 內可以直接更新呼叫端，不需要保留產品層 alias");
     }
 
+    /// <summary>
+    /// 保護產品付款狀態僅使用中性 manager。
+    /// 於真實產品組件查詢舊供應商名稱，模擬相容包裝被重建；斷言不存在，避免後續產品誤用供應商入口。
+    /// </summary>
+    /// <remarks>
+    /// 與 <see cref="DonationPaymentManagerNamingTests.Legacy_qpay_manager_does_not_remain_as_compatibility_alias"/>
+    /// 共同保護相同別名移除契約；此例仍歸屬表單命名套件，依審查指示保留原有案例。
+    /// </remarks>
     [Fact]
     public void Donation_payment_manager_is_the_only_churchreport_payment_state_manager()
     {
-        Type.GetType($"ChurchReport.Models.{LegacyModelToken}Manager, ChurchReport").Should().BeNull(
+        typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType($"ChurchReport.Models.{LegacyModelToken}Manager").Should().BeNull(
             "DonationPaymentManager 已經是 ChurchReport 奉獻付款 UI 狀態的主要 manager；" +
             "不應再保留舊 provider 形狀的 manager alias，否則後續新增高鉅、台新或其他產品時會誤用");
     }
 
+    /// <summary>
+    /// 保護產品與共用金流的檔案命名邊界。
+    /// 從目前方案根目錄掃描兩個確實存在的來源目錄，偵測供應商形狀的產品檔名；斷言例外範圍外沒有命中，不能漏掃改名後的產品目錄。
+    /// </summary>
     [Fact]
     public void Product_layer_file_names_should_not_contain_provider_brand_names()
     {
@@ -70,15 +86,17 @@ public sealed class DonationPaymentFormModelNamingTests
         {
             Path.Combine("SpeechMessage.Payments", "Providers", "Sinopac"),
             Path.Combine("SpeechMessage.Payments.Tests", "Providers", "Sinopac"),
-            Path.Combine("ChurchReport", "文件")
+            Path.Combine("SpeechMessageProducts.ChurchReport", "文件")
         };
 
         var runtimeRoots = new[]
         {
-            Path.Combine(repositoryRoot, "ChurchReport"),
+            Path.Combine(repositoryRoot, "SpeechMessageProducts.ChurchReport"),
             Path.Combine(repositoryRoot, "SpeechMessage.Payments")
         };
 
+        runtimeRoots.Should().OnlyContain(path => Directory.Exists(path), "架構檢查必須掃描目前方案的全部來源目錄");
+
         var offenders = runtimeRoots
             .Where(Directory.Exists)
             .SelectMany(root => Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories))
@@ -102,6 +120,10 @@ public sealed class DonationPaymentFormModelNamingTests
             "舊 URL 必須靠 Route attribute 保留，不應靠舊檔名或舊類別名保留");
     }
 
+    /// <summary>
+    /// 保護供應商字樣僅留在協定實作或已核准的既有路由。
+    /// 掃描真實方案的執行程式來源，偵測供應商命名回流；斷言所有命中皆位於限定例外，註解與建置產物不作為產品行為。
+    /// </summary>
     [Fact]
     public void Provider_specific_names_are_confined_to_provider_code_or_legacy_route_templates()
     {
@@ -114,10 +136,12 @@ public sealed class DonationPaymentFormModelNamingTests
 
         var runtimeRoots = new[]
         {
-            Path.Combine(repositoryRoot, "ChurchReport"),
+            Path.Combine(repositoryRoot, "SpeechMessageProducts.ChurchReport"),
             Path.Combine(repositoryRoot, "SpeechMessage.Payments")
         };
 
+        runtimeRoots.Should().OnlyContain(path => Directory.Exists(path), "產品改名後仍須完整執行命名邊界檢查");
+
         var scannedFiles = runtimeRoots
             .Where(Directory.Exists)
             .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
@@ -148,15 +172,22 @@ public sealed class DonationPaymentFormModelNamingTests
             "或為了不中斷既有瀏覽器網址與銀行 callback 而保留的 Route template 字串");
     }
 
+    /// <summary>
+    /// 只允許列名的既有路由擁有者保留相容 URL 字串，避免以一般字串或不同檔案繞過命名邊界。
+    /// 路徑與單行文字來自本次來源掃描；回傳是否屬於固定的路由例外，不保留任何掃描狀態。
+    /// </summary>
+    /// <param name="relativePath">目前方案內的相對來源路徑。</param>
+    /// <param name="line">本次檢查的來源文字行。</param>
+    /// <returns>僅在檔案、路由語法與相容 URL 皆符合時回傳 true。</returns>
     private static bool IsAllowedLegacyRouteTemplate(string relativePath, string line)
     {
         var isLegacyRouteOwner =
-            relativePath.Equals(Path.Combine("ChurchReport", "Controllers", "DedicationController.cs"), StringComparison.OrdinalIgnoreCase) ||
-            relativePath.Equals(Path.Combine("ChurchReport", "Controllers", "DonationPaymentLoginController.cs"), StringComparison.OrdinalIgnoreCase) ||
-            relativePath.Equals(Path.Combine("ChurchReport", "Controllers", "HomeController.cs"), StringComparison.OrdinalIgnoreCase) ||
-            relativePath.Equals(Path.Combine("ChurchReport", "Controllers", "PaymentReturnController.cs"), StringComparison.OrdinalIgnoreCase) ||
-            relativePath.Equals(Path.Combine("ChurchReport", "WebServiceConnector", "DonationPaymentProcessor", "DonationPaymentProcessor.Core.cs"), StringComparison.OrdinalIgnoreCase) ||
-            relativePath.Equals(Path.Combine("ChurchReport", "Startup.cs"), StringComparison.OrdinalIgnoreCase);
+            relativePath.Equals(Path.Combine("SpeechMessageProducts.ChurchReport", "Controllers", "DedicationController.cs"), StringComparison.OrdinalIgnoreCase) ||
+            relativePath.Equals(Path.Combine("SpeechMessageProducts.ChurchReport", "Controllers", "DonationPaymentLoginController.cs"), StringComparison.OrdinalIgnoreCase) ||
+            relativePath.Equals(Path.Combine("SpeechMessageProducts.ChurchReport", "Controllers", "HomeController.cs"), StringComparison.OrdinalIgnoreCase) ||
+            relativePath.Equals(Path.Combine("SpeechMessageProducts.ChurchReport", "Controllers", "PaymentReturnController.cs"), StringComparison.OrdinalIgnoreCase) ||
+            relativePath.Equals(Path.Combine("SpeechMessageProducts.ChurchReport", "WebServiceConnector", "DonationPaymentProcessor", "DonationPaymentProcessor.Core.cs"), StringComparison.OrdinalIgnoreCase) ||
+            relativePath.Equals(Path.Combine("SpeechMessageProducts.ChurchReport", "Startup.cs"), StringComparison.OrdinalIgnoreCase);
 
         if (!isLegacyRouteOwner)
         {
@@ -186,6 +217,12 @@ public sealed class DonationPaymentFormModelNamingTests
         return legacyRouteTokens.Any(token => line.Contains(token, StringComparison.Ordinal));
     }
 
+    /// <summary>
+    /// 將既有純註解行排除於執行程式命名檢查之外；保留實際程式行以偵測供應商名稱回流。
+    /// 判斷只依當次文字，不改寫來源檔，也不保存掃描結果。
+    /// </summary>
+    /// <param name="line">本次掃描的來源行。</param>
+    /// <returns>不是既有純註解形式時回傳 true。</returns>
     private static bool IsRuntimeCodeLine(string line)
     {
         var trimmed = line.TrimStart();
@@ -195,6 +232,13 @@ public sealed class DonationPaymentFormModelNamingTests
                !trimmed.StartsWith("/*", StringComparison.Ordinal);
     }
 
+    /// <summary>
+    /// 排除產物與其他 worktree，確保此架構測試只檢查目前版本的正式來源。
+    /// 使用完整目錄片段比較而非模糊字串，以免漏掃名稱包含相同文字的正式檔案。
+    /// </summary>
+    /// <param name="repositoryRoot">目前方案的根目錄。</param>
+    /// <param name="path">本次列舉的檔案或目錄。</param>
+    /// <returns>屬於固定排除目錄時回傳 true。</returns>
     private static bool ShouldIgnorePath(string repositoryRoot, string path)
     {
         var relative = Path.GetRelativePath(repositoryRoot, path);
@@ -207,12 +251,17 @@ public sealed class DonationPaymentFormModelNamingTests
             segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase));
     }
 
+    /// <summary>
+    /// 從測試輸出目錄定位最近的目前方案，避免將上層 worktree 的其他版本當作驗證依據。
+    /// 找不到明確方案標記時即失敗，不使用目前工作目錄或空集合讓架構檢查假通過。
+    /// </summary>
+    /// <returns>含 SpeechMessageProducts.sln 的最近上層目錄。</returns>
     private static string FindRepositoryRoot()
     {
         var directory = new DirectoryInfo(AppContext.BaseDirectory);
         while (directory != null)
         {
-            if (File.Exists(Path.Combine(directory.FullName, "ChurchReport.sln")))
+            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
             {
                 return directory.FullName;
             }
@@ -220,6 +269,6 @@ public sealed class DonationPaymentFormModelNamingTests
             directory = directory.Parent;
         }
 
-        throw new DirectoryNotFoundException("找不到 ChurchReport.sln，無法判斷 repository root。");
+        throw new DirectoryNotFoundException("找不到 SpeechMessageProducts.sln，無法判斷 repository root。");
     }
 }

diff --git a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentManagerNamingTests.cs b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentManagerNamingTests.cs
index ea21b2a5..8269ca62 100644
--- a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentManagerNamingTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentManagerNamingTests.cs
@@ -4,7 +4,7 @@
 // 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
 // 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
 // 主要型別：class DonationPaymentManagerNamingTests
-// 主要成員：New_donation_payment_manager_exists_as_primary_ui_payment_state_manager、Legacy_qpay_manager_remains_as_compatibility_alias
+// 主要成員：New_donation_payment_manager_exists_as_primary_ui_payment_state_manager、Legacy_qpay_manager_does_not_remain_as_compatibility_alias
 // 引用命名空間：ChurchReport.Models、FluentAssertions、Microsoft.AspNetCore.Mvc、Xunit
 // 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
 // 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
@@ -20,28 +20,40 @@ namespace ChurchReport.MemberInfo.Tests.Payments;
 
 /// <summary>
 /// 驗證 ChurchReport 的奉獻付款 UI 狀態管理器已改用中性名稱。
-/// 這組測試不觸發 CRM 或 LINE，只鎖定型別邊界，確保舊 DonationPaymentManager 不再是主要實作。
+/// 這組測試不觸發 CRM 或 LINE，只鎖定型別邊界，確認中性 manager 存在，且舊供應商別名不再形成第二個入口。
 /// </summary>
 public sealed class DonationPaymentManagerNamingTests
 {
+    /// <summary>
+    /// 保護付款 UI 的中性入口仍為 MVC Controller。
+    /// 以完整型別名稱查詢真實產品組件，模擬型別被刪除或移到錯誤組件；斷言型別存在且可作為 Controller，不建立任何 Session 或外部連線。
+    /// </summary>
     [Fact]
     public void New_donation_payment_manager_exists_as_primary_ui_payment_state_manager()
     {
-        var managerType = Type.GetType("ChurchReport.Models.DonationPaymentManager, ChurchReport");
+        var managerType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Models.DonationPaymentManager");
 
         managerType.Should().NotBeNull(
             "ChurchReport 奉獻付款的 UI 狀態管理應以 DonationPaymentManager 作為主要名稱");
         managerType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
     }
 
+    /// <summary>
+    /// 保護舊供應商名稱的 manager 不再成為可誤用的入口。
+    /// 依既定移除相容包裝決策，在真實產品組件查詢舊型別；斷言舊型別不存在，避免因錯誤組件名稱使負向測試假通過。
+    /// </summary>
+    /// <remarks>
+    /// 與 <see cref="DonationPaymentFormModelNamingTests.Donation_payment_manager_is_the_only_churchreport_payment_state_manager"/>
+    /// 重複保護舊 manager 不存在的契約；依本次審查指示保留兩個歷史案例，未合併或刪除測試。
+    /// </remarks>
     [Fact]
-    public void Legacy_qpay_manager_remains_as_compatibility_alias()
+    public void Legacy_qpay_manager_does_not_remain_as_compatibility_alias()
     {
-        var managerType = Type.GetType("ChurchReport.Models.DonationPaymentManager, ChurchReport");
-        var legacyType = typeof(DonationPaymentManager);
+        var managerType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Models.DonationPaymentManager");
+        var legacyType = typeof(DonationPaymentManager).Assembly.GetType("ChurchReport.Models.QpayManager");
 
         managerType.Should().NotBeNull();
-        legacyType.Should().BeAssignableTo(managerType!,
-            "DonationPaymentManager 只能保留為舊 Controller/View 的相容入口，實際管理流程應由 DonationPaymentManager 承擔");
+        legacyType.Should().BeNull(
+            "中性 manager 已是唯一入口，不能重新加入舊供應商相容別名");
     }
 }

diff --git a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentNamingCompatibilityTests.cs b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentNamingCompatibilityTests.cs
index 12ed4fde..f08fb656 100644
--- a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentNamingCompatibilityTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentNamingCompatibilityTests.cs
@@ -17,24 +17,37 @@ using Xunit;
 
 namespace ChurchReport.MemberInfo.Tests.Payments;
 
+/// <summary>
+/// 驗證中性付款回傳 Controller 存在且保有 MVC 繼承關係，同時禁止舊 C# Controller 別名回流。
+/// 測試以真實產品組件查詢型別；既有 URL 的相容性由產品路由負責，不在此呼叫銀行回傳流程。
+/// 全程只讀型別資訊，不建立控制器、Session、付款或外部連線。
+/// </summary>
 public sealed class DonationPaymentNamingCompatibilityTests
 {
+    /// <summary>
+    /// 保護舊外部 URL 的相容性不得靠舊 C# Controller 別名實作。
+    /// 在真實產品組件查詢舊控制器名稱；斷言型別不存在，既有路由由中性控制器承接。
+    /// </summary>
     [Fact]
     public void Old_qpay_card_controller_should_not_remain_as_csharp_alias()
     {
-        // 舊的 QPayCardController 仍然承接既有金流回傳 URL。
+        // 既有金流回傳 URL 由中性控制器的路由維持相容。
         // 這是外部金流設定與舊連結的相容層，不代表新程式還應該以 QPay 命名為主。
-        var legacyType = Type.GetType("ChurchReport.Controllers.QPayCardController, ChurchReport");
+        var legacyType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Controllers.QPayCardController");
 
         legacyType.Should().BeNull();
     }
 
+    /// <summary>
+    /// 保護付款回傳的中性 MVC 入口。
+    /// 在實際產品組件查詢完整名稱，偵測控制器遺失或錯誤移出 MVC 邊界；斷言存在且繼承 Controller。
+    /// </summary>
     [Fact]
     public void New_payment_return_controller_exists_after_rename()
     {
         // 這個測試描述目標狀態：
         // 新的主要 controller 名稱應該描述「付款回傳端點」，而不是描述特定金流供應商。
-        var newType = Type.GetType("ChurchReport.Controllers.PaymentReturnController, ChurchReport");
+        var newType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Controllers.PaymentReturnController");
 
         newType.Should().NotBeNull("新的主要回傳 Controller 應該使用 provider-neutral 命名");
         newType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();

diff --git a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorNamingTests.cs b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorNamingTests.cs
index 29d25e90..90f3ca40 100644
--- a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorNamingTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorNamingTests.cs
@@ -28,27 +28,39 @@ namespace ChurchReport.MemberInfo.Tests.Payments;
 /// </summary>
 public sealed class DonationPaymentProcessorNamingTests
 {
+    /// <summary>
+    /// 保護奉獻付款後流程的中性處理器入口。
+    /// 以完整名稱查詢實際產品組件，讓刪除或錯誤搬移型別確實造成失敗；斷言處理器存在，不呼叫銀行、CRM 或 LINE。
+    /// </summary>
     [Fact]
     public void New_donation_payment_processor_exists_as_primary_product_workflow_processor()
     {
-        var processorType = Type.GetType("ChurchReport.WebServiceConnector.DonationPaymentProcessor, ChurchReport");
+        var processorType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.WebServiceConnector.DonationPaymentProcessor");
 
         processorType.Should().NotBeNull(
             "ChurchReport 產品層付款後流程應由 DonationPaymentProcessor 作為主要類別名稱");
     }
 
+    /// <summary>
+    /// 保護舊供應商處理器相容包裝已移除的架構契約。
+    /// 在已載入的產品組件查詢舊型別，模擬未來誤加回舊入口；斷言不存在，不能因組件查詢失敗而假綠燈。
+    /// </summary>
     [Fact]
     public void Legacy_qpay_processor_alias_should_not_remain()
     {
-        Type.GetType("ChurchReport.WebServiceConnector.QPayProcessor, ChurchReport").Should().BeNull(
+        typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.WebServiceConnector.QPayProcessor").Should().BeNull(
             "QPayProcessor 是產品層舊 alias；重構後應直接使用 DonationPaymentProcessor，" +
             "舊外部 URL 可由 route 保留，但 C# 類別名稱不應再保留 QPay alias");
     }
 
+    /// <summary>
+    /// 保護產品處理器必須注入中性付款建立轉接器。
+    /// 反射實際公開建構式，偵測移除依賴或把依賴改為可選參數的退化；斷言轉接器參數存在且沒有預設值。
+    /// </summary>
     [Fact]
     public void Donation_payment_processor_constructors_require_neutral_gateway_create_adapter()
     {
-        var processorType = Type.GetType("ChurchReport.WebServiceConnector.DonationPaymentProcessor, ChurchReport");
+        var processorType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.WebServiceConnector.DonationPaymentProcessor");
 
         processorType.Should().NotBeNull();
         var adapterParameters = processorType!

diff --git a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnProcessorNamingTests.cs b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnProcessorNamingTests.cs
index 39f15f92..fd692932 100644
--- a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnProcessorNamingTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnProcessorNamingTests.cs
@@ -25,33 +25,49 @@ namespace ChurchReport.MemberInfo.Tests.Payments;
 /// </summary>
 public sealed class DonationPaymentReturnProcessorNamingTests
 {
+    /// <summary>
+    /// 保護收費單付款完成的中性 MVC 入口。
+    /// 在實際產品組件查詢指定型別，偵測入口遭刪除或搬移；斷言存在且繼承 Controller，不建立付款或使用者狀態。
+    /// </summary>
     [Fact]
     public void New_donation_fee_payment_processor_exists_as_primary_fee_return_processor()
     {
-        var processorType = Type.GetType("ChurchReport.Tools.DonationFeePaymentProcessor, ChurchReport");
+        var processorType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Tools.DonationFeePaymentProcessor");
 
         processorType.Should().NotBeNull("收費單付款完成流程應由 DonationFeePaymentProcessor 作為主要入口");
         processorType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
     }
 
+    /// <summary>
+    /// 保護定期奉獻回傳的中性 MVC 入口。
+    /// 查詢實際產品組件以偵測型別遺失，斷言入口存在且可作為 Controller；不觸發實際扣款或對外通知。
+    /// </summary>
     [Fact]
     public void New_recurring_donation_payment_processor_exists_as_primary_recurring_return_processor()
     {
-        var processorType = Type.GetType("ChurchReport.Tools.RecurringDonationPaymentProcessor, ChurchReport");
+        var processorType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Tools.RecurringDonationPaymentProcessor");
 
         processorType.Should().NotBeNull("定期定額付款完成流程應由 RecurringDonationPaymentProcessor 作為主要入口");
         processorType!.IsAssignableTo(typeof(Controller)).Should().BeTrue();
     }
 
+    /// <summary>
+    /// 保護付款結果與除錯工具保持中性公開入口。
+    /// 對實際產品組件查詢兩個完整名稱，以刪除任一工具為故障情境；斷言兩者存在，不執行日誌寫入。
+    /// </summary>
     [Fact]
     public void New_payment_result_helper_and_debug_logger_exist()
     {
-        Type.GetType("ChurchReport.Tools.DonationPaymentResultHelper, ChurchReport")
+        typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Tools.DonationPaymentResultHelper")
             .Should().NotBeNull("付款結果判斷 helper 應使用中性命名");
-        Type.GetType("ChurchReport.Tools.DonationPaymentDebugLogger, ChurchReport")
+        typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Tools.DonationPaymentDebugLogger")
             .Should().NotBeNull("付款除錯記錄 helper 應使用中性命名");
     }
 
+    /// <summary>
+    /// 保護既定移除四個舊付款包裝型別的契約。
+    /// 在真實產品組件逐項查詢可能被重新加入的完整名稱；斷言命中集合為空，避免舊入口與共用流程並存。
+    /// </summary>
     [Fact]
     public void ChurchReport_assembly_does_not_expose_legacy_qpay_payment_workflow_wrappers()
     {

diff --git a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowNamingTests.cs b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowNamingTests.cs
index f4c12dad..9fb513ff 100644
--- a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowNamingTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowNamingTests.cs
@@ -16,23 +16,36 @@ using Xunit;
 
 namespace ChurchReport.MemberInfo.Tests.Payments;
 
+/// <summary>
+/// 驗證付款後產品流程使用中性結果 DTO，且舊供應商結果型別已移除。
+/// 以實際產品組件的完整型別名稱偵測 DTO 遺失或相容別名回流，不執行付款回呼。
+/// 只讀反射資訊，不建立 DTO 實例或保存任何使用者、付款及外部連線狀態。
+/// </summary>
 public sealed class DonationPaymentReturnWorkflowNamingTests
 {
+    /// <summary>
+    /// 保護舊供應商付款結果 DTO 已移除。
+    /// 在已載入的真實產品組件查詢舊完整名稱，讓重新引入相容 DTO 的退化可被偵測；斷言舊型別不存在。
+    /// </summary>
     [Fact]
     public void Legacy_qpay_workflow_result_should_not_remain_after_neutral_rename()
     {
         // CRM 更新與 LINE 通知流程已經改用 DonationPaymentWorkflowResult。
         // 舊 QPayWorkflowPaymentResult 不應再保留成 alias，避免未來高鉅、台新或新金流看起來也在依賴永豐名稱。
-        var resultType = Type.GetType("ChurchReport.Payments.QPayWorkflowPaymentResult, ChurchReport");
+        var resultType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Payments.QPayWorkflowPaymentResult");
 
         resultType.Should().BeNull();
     }
 
+    /// <summary>
+    /// 保護產品流程使用中性付款結果 DTO。
+    /// 以完整型別名稱查詢真實產品組件，偵測結果型別遭刪除或搬到錯誤邊界；斷言新 DTO 存在。
+    /// </summary>
     [Fact]
     public void New_donation_payment_workflow_result_exists_after_rename()
     {
         // ChurchReport 產品流程應該使用 DonationPaymentWorkflowResult，而不是使用永豐專屬的 QPay 名稱。
-        var resultType = Type.GetType("ChurchReport.Payments.DonationPaymentWorkflowResult, ChurchReport");
+        var resultType = typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType("ChurchReport.Payments.DonationPaymentWorkflowResult");
 
         resultType.Should().NotBeNull("新的產品中性 DTO 會成為 ChurchReport 付款回傳流程的主要型別");
     }

diff --git a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentServiceExtractionTests.cs b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentServiceExtractionTests.cs
index 360df32a..38ac36cd 100644
--- a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentServiceExtractionTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentServiceExtractionTests.cs
@@ -20,8 +20,17 @@ using Xunit;
 
 namespace ChurchReport.MemberInfo.Tests.Payments;
 
+/// <summary>
+/// 保護虛構信用卡資料解析與序列化、固定日期分類、表單驗證，以及認獻狀態與付款方式的既有映射。
+/// 使用固定假資料驗證成功、錯誤輸入及未知選項，並讀取本 worktree 的 manager 區段守住服務委派邊界。
+/// 不建立 CRM、付款或 LINE 連線；解析結果及來源文字由單一案例擁有，不跨案例保存。
+/// </summary>
 public sealed class DonationPaymentServiceExtractionTests
 {
+    /// <summary>
+    /// 保護既有信用卡顯示資料的解析契約。
+    /// 注入兩筆虛構卡片記錄，驗證遮罩、到期年月與 token 對應；斷言筆數及兩筆欄位逐一相符，不接觸真實付款資料。
+    /// </summary>
     [Fact]
     public void ParseCreditCards_should_convert_legacy_visa_info_into_display_cards()
     {
@@ -37,6 +46,10 @@ public sealed class DonationPaymentServiceExtractionTests
         cards[1].ExpireDate.Should().Be("25/12");
     }
 
+    /// <summary>
+    /// 保護 CRM 舊格式的讀寫相容性。
+    /// 使用固定虛構卡片資料解析後序列化，偵測分隔字元或欄位順序改變；斷言輸出逐字等於原始格式。
+    /// </summary>
     [Fact]
     public void SerializeCreditCards_should_preserve_legacy_crm_storage_format()
     {
@@ -47,6 +60,10 @@ public sealed class DonationPaymentServiceExtractionTests
             .Be("tok_1，1234，5678，2607|");
     }
 
+    /// <summary>
+    /// 保護日期區間內的特別奉獻分類。
+    /// 注入固定日期及跨月區間以避免系統時鐘依賴；斷言回傳指定分類名稱。
+    /// </summary>
     [Fact]
     public void ResolveSpecialCategory_should_return_name_when_today_is_inside_range()
     {
@@ -59,6 +76,10 @@ public sealed class DonationPaymentServiceExtractionTests
             .Be("暑期特別奉獻");
     }
 
+    /// <summary>
+    /// 保護日期區間外不發布已過期分類。
+    /// 注入固定的區間外日期，模擬活動結束；斷言回傳空字串而非沿用先前分類。
+    /// </summary>
     [Fact]
     public void ResolveSpecialCategory_should_return_empty_when_today_is_outside_range()
     {
@@ -71,6 +92,10 @@ public sealed class DonationPaymentServiceExtractionTests
             .BeEmpty();
     }
 
+    /// <summary>
+    /// 保護不合法日期設定的可預期拒絕行為。
+    /// 注入兩端皆無法解析的日期文字，斷言不丟例外且分類為空，避免錯誤設定變成有效分類。
+    /// </summary>
     [Fact]
     public void ResolveSpecialCategory_should_ignore_malformed_date_text_without_throwing()
     {
@@ -88,6 +113,10 @@ public sealed class DonationPaymentServiceExtractionTests
             .BeEmpty();
     }
 
+    /// <summary>
+    /// 保護未填金額不得進入付款流程。
+    /// 注入金額為零的操作區域模型，斷言回傳既有拒絕訊息；不建立付款或跨請求狀態。
+    /// </summary>
     [Fact]
     public void ValidateDonationForm_should_reject_empty_amount()
     {
@@ -99,6 +128,10 @@ public sealed class DonationPaymentServiceExtractionTests
         result.Should().Be("未輸入奉獻金額");
     }
 
+    /// <summary>
+    /// 保護節期奉獻必填節期選項。
+    /// 注入有金額但未選節期的模型，斷言回傳既有驗證訊息而非接受表單。
+    /// </summary>
     [Fact]
     public void ValidateDonationForm_should_require_festival_selection_for_festival_dedication()
     {
@@ -112,6 +145,10 @@ public sealed class DonationPaymentServiceExtractionTests
         result.Should().Be("錯誤:沒有選擇節期!");
     }
 
+    /// <summary>
+    /// 保護 ATM 付款指示的既有分類。
+    /// 注入固定虛構銀行與帳號文字，模擬成功取得虛擬帳號；斷言狀態及付款方式正確。
+    /// </summary>
     [Fact]
     public void ClassifyCreatePaymentResult_should_mark_payment_instruction_as_virtual_account()
     {
@@ -122,6 +159,10 @@ public sealed class DonationPaymentServiceExtractionTests
         result.PayWay.Should().Be("虛擬帳號");
     }
 
+    /// <summary>
+    /// 保護信用卡建立付款失敗不得被分類為成功。
+    /// 注入既有失敗訊息，斷言錯誤狀態與原始顯示訊息均保留。
+    /// </summary>
     [Fact]
     public void ClassifyCreatePaymentResult_should_return_error_for_credit_card_failure()
     {
@@ -132,6 +173,12 @@ public sealed class DonationPaymentServiceExtractionTests
         result.Message.Should().Be("信用卡繳費失敗! 資料輸入有誤");
     }
 
+    /// <summary>
+    /// 保護認獻狀態選項對顯示文字的既有映射。
+    /// 表格注入所有已知狀態與未知值，偵測錯誤分支或後備文字變更；逐案斷言固定期望值。
+    /// </summary>
+    /// <param name="optionSetValue">固定的已知或未知認獻選項值。</param>
+    /// <param name="expected">獨立列出的既有顯示文字。</param>
     [Theory]
     [InlineData(100000000, "尚未啟動")]
     [InlineData(100000001, "進行中")]
@@ -144,6 +191,12 @@ public sealed class DonationPaymentServiceExtractionTests
         DonationBookingService.ConvertStatus(optionSetValue).Should().Be(expected);
     }
 
+    /// <summary>
+    /// 保護既有付款方式選項與未知值後備。
+    /// 使用固定選項表注入各種付款方式，偵測漏列或錯誤映射；逐案斷言顯示文字。
+    /// </summary>
+    /// <param name="optionSetValue">固定的付款方式選項值。</param>
+    /// <param name="expected">獨立列出的既有顯示文字。</param>
     [Theory]
     [InlineData(100000000, "現金")]
     [InlineData(100000001, "信用卡")]
@@ -159,13 +212,17 @@ public sealed class DonationPaymentServiceExtractionTests
         DonationFeeQueryService.ConvertPayWay(optionSetValue).Should().Be(expected);
     }
 
+    /// <summary>
+    /// 保護會友映射集中在 contact service 的既有架構邊界。
+    /// 讀取目前方案的 manager 區段，偵測重新內嵌 CRM 建立或欄位映射；斷言服務委派完整且無直接建立實體的程式。
+    /// </summary>
     [Fact]
     public void DonationPaymentManager_should_delegate_contact_mapping_to_contact_service()
     {
         // 這個測試刻意檢查原始碼結構，而不是測 CRM 行為。
         // 目的在於鎖住本次重構邊界：DonationPaymentManager 只能協調流程，
         // ChurchReport CRM contact 的建立、比對、補欄位細節必須集中在 DonationContactService。
-        string managerSource = ReadRepositoryFile("ChurchReport", "Models", "DonationPaymentManager.cs");
+        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
         string contactSection = ExtractSourceSection(
             managerSource,
             "public Entity CreateDonationContact",
@@ -180,12 +237,16 @@ public sealed class DonationPaymentServiceExtractionTests
         contactSection.Should().NotContain("SetOptionSetAttribute(ref aContactToCreate");
     }
 
+    /// <summary>
+    /// 保護手動奉獻流程由產品服務擁有。
+    /// 讀取目前方案的指定方法區段，偵測 CRM 查詢重新散回 manager；斷言有服務委派且沒有舊查詢入口。
+    /// </summary>
     [Fact]
     public void DonationPaymentManager_should_delegate_key_in_dedication_workflow()
     {
         // 手動奉獻查詢/更新是 ChurchReport 產品流程，manager 應只保留公開入口，
         // CRM 查詢與 JSON payload 組裝要集中在 DonationKeyInDedicationService。
-        string managerSource = ReadRepositoryFile("ChurchReport", "Models", "DonationPaymentManager.cs");
+        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
         string keyInSection = ExtractSourceSection(
             managerSource,
             "public async Task<IActionResult> SaveKeyInDedication",
@@ -195,12 +256,16 @@ public sealed class DonationPaymentServiceExtractionTests
         keyInSection.Should().NotContain("QueryDediccationContatsByFetchXml");
     }
 
+    /// <summary>
+    /// 保護認獻清單及取消流程的服務邊界。
+    /// 讀取目前方案的 booking 區段，偵測直接查詢認獻實體的退化；斷言委派服務且未內嵌 CRM 邏輯。
+    /// </summary>
     [Fact]
     public void DonationPaymentManager_should_delegate_booking_workflow()
     {
         // 認獻清單與取消認獻會碰 CRM、LINE 與舊金流取消流程，
         // 這些流程應移到 ChurchReport service，不能留在大型 manager 裡。
-        string managerSource = ReadRepositoryFile("ChurchReport", "Models", "DonationPaymentManager.cs");
+        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
         string bookingSection = ExtractSourceSection(
             managerSource,
             "public void ProcessDedicationBooking()",
@@ -211,12 +276,16 @@ public sealed class DonationPaymentServiceExtractionTests
         bookingSection.Should().NotContain("new_dedication_booking");
     }
 
+    /// <summary>
+    /// 保護新增會友與奉獻編號集中在產品服務。
+    /// 讀取目前方案的建立區段，偵測 manager 直接新增實體或計算編號；斷言存在服務委派並排除直接查詢。
+    /// </summary>
     [Fact]
     public void DonationPaymentManager_should_delegate_contact_creation_numbering_workflow()
     {
         // 查無新人時建立 contact 以及奉獻編號規則是 ChurchReport 資料規則，
         // manager 不應直接建立 contact 或計算 pager 編號。
-        string managerSource = ReadRepositoryFile("ChurchReport", "Models", "DonationPaymentManager.cs");
+        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
         string creationSection = ExtractSourceSection(
             managerSource,
             "public async Task<IActionResult> CreateContact",
@@ -227,12 +296,16 @@ public sealed class DonationPaymentServiceExtractionTests
         creationSection.Should().NotContain("QueryContatsByStartedDedicationNumber");
     }
 
+    /// <summary>
+    /// 保護付款表單組裝集中在 assembler。
+    /// 讀取目前方案的模型組裝區段，偵測 task 查詢與 OptionSet 細節重新回流；斷言有 assembler 委派且無底層映射細節。
+    /// </summary>
     [Fact]
     public void DonationPaymentManager_should_delegate_payment_model_assembly()
     {
         // 奉獻表單初始化牽涉 CRM 欄位、OptionSet、信用卡清單與認獻清單，
         // manager 應把組裝細節交給 assembler，避免 UI model 規則散在大型協調器。
-        string managerSource = ReadRepositoryFile("ChurchReport", "Models", "DonationPaymentManager.cs");
+        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
         string modelSection = ExtractSourceSection(
             managerSource,
             "public DonationPaymentFormModel SetDonationPaymentModel",
@@ -243,12 +316,16 @@ public sealed class DonationPaymentServiceExtractionTests
         modelSection.Should().NotContain("OptionSetMetadataService");
     }
 
+    /// <summary>
+    /// 保護奉獻登入的會友比對由專用服務負責。
+    /// 讀取目前方案的登入區段，偵測依姓名或證號的查詢重新回流；斷言委派登入服務且無直接 CRM 查詢。
+    /// </summary>
     [Fact]
     public void DonationPaymentManager_should_delegate_donation_login_contact_workflow()
     {
         // 官網奉獻登入會處理身分證、姓名、同名資料與新 contact 建立，
         // 這是 ChurchReport contact workflow，manager 不應保留大段 CRM 查詢判斷。
-        string managerSource = ReadRepositoryFile("ChurchReport", "Models", "DonationPaymentManager.cs");
+        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
         string loginSection = ExtractSourceSection(
             managerSource,
             "public Entity GetDonationPaymentLoginContact",
@@ -259,12 +336,16 @@ public sealed class DonationPaymentServiceExtractionTests
         loginSection.Should().NotContain("RetrieveContactCollectionByName");
     }
 
+    /// <summary>
+    /// 保護奉獻查詢表單刷新由產品服務負責。
+    /// 讀取目前方案的表單刷新區段，偵測 manager 直接查詢會友或自行建立費用服務；斷言保留專用服務委派。
+    /// </summary>
     [Fact]
     public void DonationPaymentManager_should_delegate_dedication_fee_form_refresh()
     {
         // 奉獻查詢表單刷新包含 contact 欄位投影與 new_fee 查詢，屬於 ChurchReport 產品表單流程。
         // manager 只應保留 public wrapper，讓 service 負責欄位填值與 fee list refresh。
-        string managerSource = ReadRepositoryFile("ChurchReport", "Models", "DonationPaymentManager.cs");
+        string managerSource = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Models", "DonationPaymentManager.cs");
         string feeListSection = ExtractSourceSection(
             managerSource,
             "public DonationPaymentFormModel SetDedicationFeeList(String UserLineId)",
@@ -275,24 +356,39 @@ public sealed class DonationPaymentServiceExtractionTests
         feeListSection.Should().NotContain("new DonationFeeQueryService");
     }
 
+    /// <summary>
+    /// 只讀取最近方案根目錄內的指定檔案；方案找到後不得退回父 worktree 搜尋另一份程式。
+    /// 檔案由固定測試案例指定，缺檔即失敗；讀取完成即關閉檔案，文字只保留於該次測試。
+    /// </summary>
+    /// <param name="pathSegments">由可信任測試定義提供的方案相對路徑片段。</param>
+    /// <returns>當次測試擁有的完整來源文字。</returns>
     private static string ReadRepositoryFile(params string[] pathSegments)
     {
         DirectoryInfo? directory = new(AppContext.BaseDirectory);
 
-        while (directory != null)
+        // 一旦找到本 worktree 的方案即停止向上尋找，避免讀到父 worktree 的其他版本。
+        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
         {
-            string candidate = Path.Combine(directory.FullName, Path.Combine(pathSegments));
-            if (File.Exists(candidate))
-            {
-                return File.ReadAllText(candidate);
-            }
-
             directory = directory.Parent;
         }
 
-        throw new FileNotFoundException("找不到測試需要讀取的專案原始碼檔案。", Path.Combine(pathSegments));
+        if (directory == null)
+        {
+            throw new DirectoryNotFoundException("找不到目前 worktree 的 SpeechMessageProducts.sln。");
+        }
+
+        string candidate = Path.Combine(directory.FullName, Path.Combine(pathSegments));
+        return File.ReadAllText(candidate);
     }
 
+    /// <summary>
+    /// 取得既有架構測試指定的來源區段；起訖標記缺失必須直接失敗，不能改用空字串略過檢查。
+    /// 傳入文字只屬於本次檢查，方法不保存跨測試狀態。
+    /// </summary>
+    /// <param name="source">目前方案檔案的完整來源文字。</param>
+    /// <param name="startMarker">區段起點的既有方法簽章。</param>
+    /// <param name="endMarker">區段終點的既有方法或區域標記。</param>
+    /// <returns>不含終點標記的來源區段。</returns>
     private static string ExtractSourceSection(string source, string startMarker, string endMarker)
     {
         int start = source.IndexOf(startMarker, StringComparison.Ordinal);

diff --git a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs
index f3244369..270b700c 100644
--- a/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs
@@ -591,13 +591,18 @@ public sealed class DonationPaymentViewDefaultsTests
         public void SaveChanges() => throw new NotSupportedException();
     }
 
+    /// <summary>
+    /// 以最近的目前方案標記界定測試來源；只接受 SpeechMessageProducts.sln，避免舊名稱把檢查導向父 worktree。
+    /// 找不到根目錄即明確失敗，不從工作目錄或其他版本補檔；路徑只在本次測試使用且不持有檔案資源。
+    /// </summary>
+    /// <returns>最近上層目前方案的根目錄。</returns>
+    /// <exception cref="DirectoryNotFoundException">沒有找到目前方案標記。</exception>
     private static string FindRepositoryRoot()
     {
         var directory = new DirectoryInfo(AppContext.BaseDirectory);
         while (directory != null)
         {
-            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))
-                || File.Exists(Path.Combine(directory.FullName, "ChurchReport.sln")))
+            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
             {
                 return directory.FullName;
             }

diff --git a/ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentArchitectureTests.cs b/ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentArchitectureTests.cs
index 19a84942..5eddbbde 100644
--- a/ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentArchitectureTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentArchitectureTests.cs
@@ -21,8 +21,17 @@ using Xunit;
 
 namespace ChurchReport.MemberInfo.Tests.Payments;
 
+/// <summary>
+/// 驗證付款控制器及處理器經由建構式依賴共用付款後工作流程，並保留產品層 presenter。
+/// 以反射檢查依賴與宣告組件，偵測 CRM／LINE 產品處理器被錯移到可重用組件的架構退化。
+/// 不建立服務或發出通知，檢查結果僅存活於當次案例。
+/// </summary>
 public sealed class PaymentPostPaymentArchitectureTests
 {
+    /// <summary>
+    /// 保護銀行回傳控制器使用共用付款後流程。
+    /// 檢查公開建構式，偵測流程依賴被移除的退化；斷言參數包含 PaymentPostPaymentWorkflow，無須外部銀行服務。
+    /// </summary>
     [Fact]
     public void Tspg_controller_depends_on_common_post_payment_workflow()
     {
@@ -34,6 +43,10 @@ public sealed class PaymentPostPaymentArchitectureTests
             .Contain(typeof(PaymentPostPaymentWorkflow));
     }
 
+    /// <summary>
+    /// 保護奉獻付款完成處理器使用共用工作流程。
+    /// 反射公開建構式以偵測重新複製產品流程的退化；斷言存在共用 PaymentPostPaymentWorkflow 依賴。
+    /// </summary>
     [Fact]
     public void Donation_fee_payment_processor_depends_on_common_post_payment_workflow()
     {
@@ -45,6 +58,10 @@ public sealed class PaymentPostPaymentArchitectureTests
             .Contain(typeof(PaymentPostPaymentWorkflow));
     }
 
+    /// <summary>
+    /// 保護付款回傳畫面組裝仍由產品層負責。
+    /// 檢查處理器建構式，偵測產品 presenter 被移除或繞過；斷言依賴 DonationPaymentReturnPresenter。
+    /// </summary>
     [Fact]
     public void Donation_fee_payment_processor_keeps_mvc_presentation_in_churchreport()
     {
@@ -56,10 +73,16 @@ public sealed class PaymentPostPaymentArchitectureTests
             .Contain(typeof(DonationPaymentReturnPresenter));
     }
 
+    /// <summary>
+    /// 保護 CRM 與 LINE 產品處理器不得移入可重用付款組件。
+    /// 以實際宣告組件檢查搬移風險；斷言兩者屬於目前產品組件，且與共用流程組件不同。
+    /// </summary>
     [Fact]
     public void ChurchReport_specific_handlers_do_not_move_to_reusable_workflow_project()
     {
-        typeof(ChurchReportPaymentRecordUpdater).Assembly.GetName().Name.Should().Be("ChurchReport");
-        typeof(ChurchReportPaymentPayerNotifier).Assembly.GetName().Name.Should().Be("ChurchReport");
+        typeof(ChurchReportPaymentRecordUpdater).Assembly.GetName().Name.Should().Be("SpeechMessageProducts.ChurchReport");
+        typeof(ChurchReportPaymentPayerNotifier).Assembly.GetName().Name.Should().Be("SpeechMessageProducts.ChurchReport");
+        typeof(ChurchReportPaymentRecordUpdater).Assembly.Should().NotBeSameAs(typeof(PaymentPostPaymentWorkflow).Assembly);
+        typeof(ChurchReportPaymentPayerNotifier).Assembly.Should().NotBeSameAs(typeof(PaymentPostPaymentWorkflow).Assembly);
     }
 }

diff --git a/ChurchReport.MemberInfo.Tests/Payments/PaymentProductServiceNamingTests.cs b/ChurchReport.MemberInfo.Tests/Payments/PaymentProductServiceNamingTests.cs
index dbd4a85e..fd8a18ec 100644
--- a/ChurchReport.MemberInfo.Tests/Payments/PaymentProductServiceNamingTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Payments/PaymentProductServiceNamingTests.cs
@@ -23,40 +23,48 @@ namespace ChurchReport.MemberInfo.Tests.Payments;
 /// </summary>
 public sealed class PaymentProductServiceNamingTests
 {
+    /// <summary>
+    /// 保護 CRM 更新、費別、回呼日誌、訊息與通知服務的中性型別名稱。
+    /// 在真實產品組件逐一查詢固定清單，任一服務遺失就失敗；不建立服務實例或保存付款資料。
+    /// </summary>
     [Fact]
     public void Product_payment_services_use_provider_neutral_names()
     {
         var expectedServiceNames = new[]
         {
-            "ChurchReport.Services.PaymentCrmService, ChurchReport",
-            "ChurchReport.Services.PaymentFeeTypeHelper, ChurchReport",
-            "ChurchReport.Services.PaymentCallbackLogger, ChurchReport",
-            "ChurchReport.Services.PaymentMessageBuilder, ChurchReport",
-            "ChurchReport.Services.PaymentNotificationService, ChurchReport"
+            "ChurchReport.Services.PaymentCrmService",
+            "ChurchReport.Services.PaymentFeeTypeHelper",
+            "ChurchReport.Services.PaymentCallbackLogger",
+            "ChurchReport.Services.PaymentMessageBuilder",
+            "ChurchReport.Services.PaymentNotificationService"
         };
 
         foreach (var expectedServiceName in expectedServiceNames)
         {
-            Type.GetType(expectedServiceName).Should().NotBeNull(
+            typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType(expectedServiceName).Should().NotBeNull(
                 "ChurchReport 付款後流程服務應以 provider-neutral 名稱呈現，避免把 CRM/LINE/收費單流程誤認為 MyPay 協定實作");
         }
     }
 
+    /// <summary>
+    /// 保護供應商名稱不回流到產品流程服務。
+    /// 在已載入的產品組件逐項查詢舊服務名稱，模擬重新引入相容包裝；斷言全部不存在，不能透過不存在的組件取得假綠燈。
+    /// </summary>
     [Fact]
     public void Legacy_mypay_product_service_names_are_removed()
     {
         var legacyServiceNames = new[]
         {
-            "ChurchReport.Services.MyPayCrmService, ChurchReport",
-            "ChurchReport.Services.MyPayFeeTypeHelper, ChurchReport",
-            "ChurchReport.Services.MyPayLogger, ChurchReport",
-            "ChurchReport.Services.MyPayMessageBuilder, ChurchReport",
-            "ChurchReport.Services.MyPayNotificationService, ChurchReport"
+            "ChurchReport.Services.MyPayCrmService",
+            "ChurchReport.Services.MyPayFeeTypeHelper",
+            "ChurchReport.Services.MyPayLogger",
+            "ChurchReport.Services.MyPayMessageBuilder",
+            "ChurchReport.Services.MyPayNotificationService"
         };
 
         foreach (var legacyServiceName in legacyServiceNames)
         {
-            Type.GetType(legacyServiceName).Should().BeNull(
+            typeof(global::ChurchReport.Models.DonationPaymentManager).Assembly.GetType(legacyServiceName).Should().BeNull(
                 "這些服務已經只消費 provider-neutral payment result，不應保留 MyPay 開頭的產品流程型別名稱");
         }
     }

diff --git a/Directory.Build.targets b/Directory.Build.targets
new file mode 100644
index 00000000..8b1fc8d0
--- /dev/null
+++ b/Directory.Build.targets
@@ -0,0 +1,54 @@
+<Project>
+  <!-- 嚴格規則只套用到明確啟用的新專案；舊專案同時開啟會引入大量既有警告。
+       SDK 在 csproj 本體之前讀取 Directory.Build.props，因此必須在 targets 才判斷
+       SpeechMessageStrictCode，不能靠命令列覆寫讓測試假通過。 -->
+  <PropertyGroup Condition="'$(SpeechMessageStrictCode)' == 'true'">
+    <EnableNETAnalyzers>true</EnableNETAnalyzers>
+    <AnalysisMode>Recommended</AnalysisMode>
+    <!-- net48 預設只稽核直接相依；嚴格專案須在還原時稽核全部相依，與 net10 保持一致。 -->
+    <NuGetAuditMode>all</NuGetAuditMode>
+    <!-- SDK 只為 .NETCoreApp 自動選分析層級；net48 空值會讓 Recommended 規則集不載入。
+         使用者 2026-11-03 核定：嚴格模式只在空值時明確採 latest，使雙目標確實同規則；
+         net10 原有層級不變，舊專案不啟用嚴格模式也不受影響。 -->
+    <AnalysisLevel Condition="'$(AnalysisLevel)' == ''">latest</AnalysisLevel>
+    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
+    <Nullable>enable</Nullable>
+    <LangVersion>latest</LangVersion>
+    <WarningsAsErrors>$(WarningsAsErrors);CA1307;CA1309;CA1310;RS0030</WarningsAsErrors>
+  </PropertyGroup>
+
+  <!-- 使用者 2026-11-03 核定的永久規則，適用本計畫所有階段：
+       只有嚴格模式、明確測試標記、專案名稱以 .Tests 結尾三者同時成立，
+       才允許計畫要求的底線測試命名；僅排除 CA1707，其他分析器與警告視為錯誤不變。
+       必須集中在此設定，不得在個別測試專案重複加入其他 NoWarn。 -->
+  <PropertyGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And '$(IsTestProject)' == 'true' And $([System.String]::Copy('$(MSBuildProjectName)').EndsWith('.Tests', System.StringComparison.Ordinal))">
+    <NoWarn>$(NoWarn);CA1707</NoWarn>
+  </PropertyGroup>
+
+  <!-- SDK 在匯入本檔之前已依文件開關決定 DocumentationFile 與輸出複製行為。
+       因此 GenerateDocumentationFile 必須在 csproj 本體與嚴格模式一同啟用，
+       不能在此補設開關或改寫 SDK 私有屬性；編譯前拒絕空的實際文件路徑，避免假綠燈。 -->
+  <Target Name="ValidateSpeechMessageDocumentation" BeforeTargets="CoreCompile" Condition="'$(SpeechMessageStrictCode)' == 'true'">
+    <Error Condition="'$(DocumentationFile)' == ''" Code="SMQ0001" Text="嚴格專案必須在 csproj 本體同時設定 SpeechMessageStrictCode=true 與 GenerateDocumentationFile=true；目前 DocumentationFile 為空，無法驗證 XML 文件。" />
+  </Target>
+
+  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true'">
+    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" PrivateAssets="all" />
+    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
+    <!-- WarningsAsErrors 不會自行啟用預設停用的規則，另以專用全域設定確保分析器真的執行。 -->
+    <GlobalAnalyzerConfigFiles Include="$(MSBuildThisFileDirectory)build/StrictCode.globalconfig" />
+  </ItemGroup>
+
+  <!-- Plugin 與 Workflow 都在 D365 Sandbox 執行，額外拒絕 GDI；一般主機不受此限制。
+       分析器依 BannedSymbols.txt 檔名辨識清單，故 Plugin 清單保留同名並置於獨立目錄。 -->
+  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And ('$(MSBuildProjectName)' == 'SpeechMessage.Dynamics.Plugins' Or '$(MSBuildProjectName)' == 'SpeechMessage.Dynamics.Workflows')">
+    <AdditionalFiles Include="$(MSBuildThisFileDirectory)build/Plugin/BannedSymbols.txt" />
+  </ItemGroup>
+
+  <!-- 補丁只補 net48 缺少的編譯器中繼資料，不在 net10 重複宣告框架型別。
+       使用連結檔確保所有雙目標程式庫共用一份定義，不建立額外狀態或組件相依。 -->
+  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And '$(TargetFramework)' == 'net48'">
+    <Compile Include="$(MSBuildThisFileDirectory)src-shared/Polyfills/IsExternalInit.cs" Link="Polyfills/IsExternalInit.cs" />
+    <Compile Include="$(MSBuildThisFileDirectory)src-shared/Polyfills/NotNullWhenAttribute.cs" Link="Polyfills/NotNullWhenAttribute.cs" />
+  </ItemGroup>
+</Project>

diff --git a/Line.Messaging.Tests/Line.Messaging.Tests.csproj b/Line.Messaging.Tests/Line.Messaging.Tests.csproj
index 03940521f..2334d450a 100644
--- a/Line.Messaging.Tests/Line.Messaging.Tests.csproj
+++ b/Line.Messaging.Tests/Line.Messaging.Tests.csproj
@@ -7,17 +7,17 @@
     <IsTestProject>true</IsTestProject>
   </PropertyGroup>
   <ItemGroup>
-    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
-    <PackageReference Include="xunit" Version="2.6.6" />
-    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="xunit" />
+    <PackageReference Include="xunit.runner.visualstudio">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
-    <PackageReference Include="FluentAssertions" Version="6.12.0" />
-    <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
-    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
+    <PackageReference Include="FluentAssertions" />
+    <PackageReference Include="Microsoft.Extensions.Configuration" />
+    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" />
+    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
+    <PackageReference Include="Newtonsoft.Json" />
   </ItemGroup>
   <ItemGroup>
     <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />

diff --git a/Line.Messaging/Line.Messaging.csproj b/Line.Messaging/Line.Messaging.csproj
index 1593ff3af..ffeee03a8 100644
--- a/Line.Messaging/Line.Messaging.csproj
+++ b/Line.Messaging/Line.Messaging.csproj
@@ -1,4 +1,4 @@
-﻿<Project Sdk="Microsoft.NET.Sdk">
+<Project Sdk="Microsoft.NET.Sdk">
 
   <PropertyGroup>
     <TargetFramework>net10.0</TargetFramework>
@@ -50,7 +50,7 @@ https://developers.line.biz/en/news/2019/01/
   </PropertyGroup>
 
   <ItemGroup>
-    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
+    <PackageReference Include="Newtonsoft.Json" />
   </ItemGroup>
 
 </Project>

diff --git a/Line.Messaging/Line.Messaging_Net10.csproj b/Line.Messaging/Line.Messaging_Net10.csproj
index 29f7e10e0..ffeee03a8 100644
--- a/Line.Messaging/Line.Messaging_Net10.csproj
+++ b/Line.Messaging/Line.Messaging_Net10.csproj
@@ -50,7 +50,7 @@ https://developers.line.biz/en/news/2019/01/
   </PropertyGroup>
 
   <ItemGroup>
-    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
+    <PackageReference Include="Newtonsoft.Json" />
   </ItemGroup>
 
 </Project>

diff --git a/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj b/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj
index 1fa82a5c4..3ec9112b9 100644
--- a/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj
+++ b/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj
@@ -5,16 +5,15 @@
     <Nullable>enable</Nullable>
     <IsPackable>false</IsPackable>
     <IsTestProject>true</IsTestProject>
-    <NoWarn>NU1605</NoWarn>
   </PropertyGroup>
   <ItemGroup>
-    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
-    <PackageReference Include="xunit" Version="2.6.6" />
-    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="xunit" />
+    <PackageReference Include="xunit.runner.visualstudio">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
-    <PackageReference Include="FluentAssertions" Version="6.12.0" />
+    <PackageReference Include="FluentAssertions" />
   </ItemGroup>
   <ItemGroup>
     <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />
@@ -23,4 +22,4 @@
     <ProjectReference Include="..\LineMessagingProcessor.Workflows\LineMessagingProcessor.Workflows.csproj" />
     <ProjectReference Include="..\LineMessagingProcessor.RichMenus\LineMessagingProcessor.RichMenus.csproj" />
   </ItemGroup>
-</Project>
\ No newline at end of file
+</Project>

diff --git a/LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs b/LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs
index b354766fa..e89bdf643 100644
--- a/LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs
+++ b/LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs
@@ -23,7 +23,8 @@ namespace LineMessagingProcessor.RichMenus.Tests.Boundary;
 public sealed class RichMenuProjectBoundaryTests
 {
     /// <summary>
-    /// 掃描 RichMenu 共用專案原始碼，若出現產品名稱或上層框架關鍵字就視為邊界破壞。
+    /// 保護 RichMenu 不依賴產品、CRM 或 MVC 層；掃描最近 worktree 方案下的真實來源。
+    /// 注入的退化情境是重新加入任一禁止依賴；決定性斷言是完整命中集合為空，不能因找錯方案而漏掃。
     /// </summary>
     [Fact]
     public void RichMenu_project_does_not_reference_product_specific_dependencies()
@@ -53,12 +54,14 @@ public sealed class RichMenuProjectBoundaryTests
     }
 
     /// <summary>
-    /// 從測試輸出目錄往上找 solution root，讓測試可在 IDE、CLI 與 CI 中穩定定位專案檔。
+    /// 從測試輸出目錄尋找最近的 SpeechMessageProducts.sln，界定本次 worktree 的來源範圍。
+    /// 找不到方案就明確失敗；不使用其他名稱或目前目錄作為後備，以免驗證到另一份程式。
     /// </summary>
+    /// <returns>含目前方案標記的最近上層目錄。</returns>
     private static string FindProjectRoot()
     {
         var directory = new DirectoryInfo(AppContext.BaseDirectory);
-        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ChurchReport.sln")))
+        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
         {
             directory = directory.Parent;
         }

diff --git a/LineMessagingProcessor.RichMenus.Tests/LineMessagingProcessor.RichMenus.Tests.csproj b/LineMessagingProcessor.RichMenus.Tests/LineMessagingProcessor.RichMenus.Tests.csproj
index 4dbd28788..49ade2c65 100644
--- a/LineMessagingProcessor.RichMenus.Tests/LineMessagingProcessor.RichMenus.Tests.csproj
+++ b/LineMessagingProcessor.RichMenus.Tests/LineMessagingProcessor.RichMenus.Tests.csproj
@@ -5,17 +5,16 @@
     <Nullable>enable</Nullable>
     <IsPackable>false</IsPackable>
     <IsTestProject>true</IsTestProject>
-    <NoWarn>NU1605</NoWarn>
   </PropertyGroup>
   <ItemGroup>
-    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
-    <PackageReference Include="xunit" Version="2.6.6" />
-    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="xunit" />
+    <PackageReference Include="xunit.runner.visualstudio">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
-    <PackageReference Include="FluentAssertions" Version="6.12.0" />
-    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
+    <PackageReference Include="FluentAssertions" />
+    <PackageReference Include="Newtonsoft.Json" />
   </ItemGroup>
   <ItemGroup>
     <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />

diff --git a/LineMessagingProcessor.Workflows.Tests/LineMessagingProcessor.Workflows.Tests.csproj b/LineMessagingProcessor.Workflows.Tests/LineMessagingProcessor.Workflows.Tests.csproj
index 45650a1f5..aecb50f5c 100644
--- a/LineMessagingProcessor.Workflows.Tests/LineMessagingProcessor.Workflows.Tests.csproj
+++ b/LineMessagingProcessor.Workflows.Tests/LineMessagingProcessor.Workflows.Tests.csproj
@@ -5,17 +5,16 @@
     <Nullable>enable</Nullable>
     <IsPackable>false</IsPackable>
     <IsTestProject>true</IsTestProject>
-    <NoWarn>NU1605</NoWarn>
   </PropertyGroup>
   <ItemGroup>
-    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
-    <PackageReference Include="xunit" Version="2.6.6" />
-    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="xunit" />
+    <PackageReference Include="xunit.runner.visualstudio">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
-    <PackageReference Include="FluentAssertions" Version="6.12.0" />
-    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
+    <PackageReference Include="FluentAssertions" />
+    <PackageReference Include="Newtonsoft.Json" />
   </ItemGroup>
   <ItemGroup>
     <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />

diff --git a/LineMessagingProcessor/LineMessagingProcessor.csproj b/LineMessagingProcessor/LineMessagingProcessor.csproj
index df1a192ac..7bb1e7dbe 100644
--- a/LineMessagingProcessor/LineMessagingProcessor.csproj
+++ b/LineMessagingProcessor/LineMessagingProcessor.csproj
@@ -35,12 +35,12 @@
   </ItemGroup>
 
   <ItemGroup>
-    <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
-    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
-    <PackageReference Include="RestSharp" Version="112.1.0" />
+    <PackageReference Include="Microsoft.Extensions.Configuration" />
+    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" />
+    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" />
+    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
+    <PackageReference Include="Newtonsoft.Json" />
+    <PackageReference Include="RestSharp" />
   </ItemGroup>
 
   <ItemGroup>

diff --git a/LineMessagingProcessor/LineMessagingProcessor_Net10.csproj b/LineMessagingProcessor/LineMessagingProcessor_Net10.csproj
index 72c2c1520..577e8f189 100644
--- a/LineMessagingProcessor/LineMessagingProcessor_Net10.csproj
+++ b/LineMessagingProcessor/LineMessagingProcessor_Net10.csproj
@@ -11,8 +11,8 @@
   </PropertyGroup>
 
   <ItemGroup>
-    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
-    <PackageReference Include="RestSharp" Version="112.1.0" />
+    <PackageReference Include="Newtonsoft.Json" />
+    <PackageReference Include="RestSharp" />
   </ItemGroup>
 
   <ItemGroup>

diff --git a/LinePayCSharp/LinePayCSharp.csproj b/LinePayCSharp/LinePayCSharp.csproj
index 3010d66a7..aa59abe62 100644
--- a/LinePayCSharp/LinePayCSharp.csproj
+++ b/LinePayCSharp/LinePayCSharp.csproj
@@ -1,4 +1,4 @@
-﻿<Project Sdk="Microsoft.NET.Sdk">
+<Project Sdk="Microsoft.NET.Sdk">
 
   <PropertyGroup>
     <TargetFramework>netstandard1.3</TargetFramework>
@@ -9,7 +9,7 @@
   </PropertyGroup>
 
   <ItemGroup>
-    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
+    <PackageReference Include="Newtonsoft.Json" />
   </ItemGroup>
 
   <ItemGroup>

diff --git a/PowerPlatform.Dataverse.Client/NSspi/NSspi.csproj b/PowerPlatform.Dataverse.Client/NSspi/NSspi.csproj
index f4aab88e0..0b107840b 100644
--- a/PowerPlatform.Dataverse.Client/NSspi/NSspi.csproj
+++ b/PowerPlatform.Dataverse.Client/NSspi/NSspi.csproj
@@ -1,4 +1,4 @@
-﻿<Project Sdk="Microsoft.NET.Sdk">
+<Project Sdk="Microsoft.NET.Sdk">
   <PropertyGroup>
     <TargetFrameworks>netstandard2.0;net40</TargetFrameworks>
     <RootNamespace>NSspi</RootNamespace>
@@ -19,7 +19,7 @@
   </PropertyGroup>
 
   <ItemGroup Condition=" '$(TargetFramework)' == 'netstandard2.0' ">
-    <PackageReference Include="System.Security.Principal.Windows" Version="4.5.1" />
+    <PackageReference Include="System.Security.Principal.Windows" />
   </ItemGroup>
 
   <ItemGroup>
@@ -28,4 +28,4 @@
       <PackagePath></PackagePath>
     </None>
   </ItemGroup>
-</Project>
\ No newline at end of file
+</Project>

diff --git a/SpeechMessage.Payments.Tests/SpeechMessage.Payments.Tests.csproj b/SpeechMessage.Payments.Tests/SpeechMessage.Payments.Tests.csproj
index fa31880f6..75b361dba 100644
--- a/SpeechMessage.Payments.Tests/SpeechMessage.Payments.Tests.csproj
+++ b/SpeechMessage.Payments.Tests/SpeechMessage.Payments.Tests.csproj
@@ -5,20 +5,19 @@
     <Nullable>enable</Nullable>
     <IsPackable>false</IsPackable>
     <IsTestProject>true</IsTestProject>
-    <NoWarn>NU1605</NoWarn>
   </PropertyGroup>
   <ItemGroup>
-    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
-    <PackageReference Include="xunit" Version="2.6.6" />
-    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="xunit" />
+    <PackageReference Include="xunit.runner.visualstudio">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
-    <PackageReference Include="FluentAssertions" Version="6.12.0" />
-    <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
-    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
+    <PackageReference Include="FluentAssertions" />
+    <PackageReference Include="Microsoft.Extensions.Configuration" />
+    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
+    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
+    <PackageReference Include="Microsoft.Extensions.Http" />
   </ItemGroup>
   <ItemGroup>
     <ProjectReference Include="..\SpeechMessage.Payments\SpeechMessage.Payments.csproj" />

diff --git a/SpeechMessage.Payments/SpeechMessage.Payments.csproj b/SpeechMessage.Payments/SpeechMessage.Payments.csproj
index a4f8d8a75..40ad5b708 100644
--- a/SpeechMessage.Payments/SpeechMessage.Payments.csproj
+++ b/SpeechMessage.Payments/SpeechMessage.Payments.csproj
@@ -8,11 +8,11 @@
     <RootNamespace>SpeechMessage.Payments</RootNamespace>
   </PropertyGroup>
   <ItemGroup>
-    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
-    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.0" />
-    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
+    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
+    <PackageReference Include="Microsoft.Extensions.Http" />
+    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
+    <PackageReference Include="Microsoft.Extensions.Options" />
+    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
+    <PackageReference Include="Newtonsoft.Json" />
   </ItemGroup>
 </Project>

diff --git a/SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj b/SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj
index 56c2b02e..a6041319 100644
--- a/SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj
+++ b/SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj
@@ -1,6 +1,8 @@
 <Project ToolsVersion="15.0" Sdk="Microsoft.NET.Sdk.Web">
   <PropertyGroup>
     <TargetFramework>net10.0</TargetFramework>
+    <!-- 固定主機識別碼只供 Development 載入 User Secrets；repo 不保存帳密或測試 token。 -->
+    <UserSecretsId>SpeechMessageProducts.ChurchReport.Development</UserSecretsId>
     <EnableDefaultContentItems>false</EnableDefaultContentItems>
     <!-- ✅ 效能：本專案未註冊 AddRazorRuntimeCompilation，不需要保留編譯期中繼資料參考。
          設為 false 可讓 publish 不再輸出 refs\ 目錄與龐大的 .deps.json 編譯區段，
@@ -18,8 +20,8 @@
     <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
     <RetainVMGarbageCollection>true</RetainVMGarbageCollection>
     
-    <!-- 允許套件降級警告不影響建置 -->
-    <NoWarn>$(NoWarn);NU1605;ASPDEPR005;CA1416;CA2200;CS0105;CS0108;CS0114;CS0162;CS0168;CS0169;CS0219;CS0414;CS0472;CS0618;CS0649;CS8073;CS8632;CS8981;CS1717;CS4014;MVC1000;SYSLIB0014;SYSLIB0021</NoWarn>
+    <!-- 套件降版警告維持可見；下列既有非套件抑制設定原樣保留。 -->
+    <NoWarn>$(NoWarn);ASPDEPR005;CA1416;CA2200;CS0105;CS0108;CS0114;CS0162;CS0168;CS0169;CS0219;CS0414;CS0472;CS0618;CS0649;CS8073;CS8632;CS8981;CS1717;CS4014;MVC1000;SYSLIB0014;SYSLIB0021</NoWarn>
     
     <!-- ✅ Phase 5: .NET 10 效能優化設定 -->
     <TieredPGO>true</TieredPGO>
@@ -85,12 +87,12 @@
     <Content Remove="wwwroot\**\*.debug.js" />
   </ItemGroup>
   <ItemGroup>
-    <PackageReference Include="DevExtreme.AspNet.Core" Version="23.1.5" />
-    <PackageReference Include="DevExtreme.AspNet.Data" Version="5.1.0" />
-    <PackageReference Include="Microsoft.AspNet.WebApi.Client" Version="6.0.0" />
-    <PackageReference Include="Microsoft.AspNetCore.Mvc.NewtonsoftJson" Version="10.0.0" />
+    <PackageReference Include="DevExtreme.AspNet.Core" />
+    <PackageReference Include="DevExtreme.AspNet.Data" />
+    <PackageReference Include="Microsoft.AspNet.WebApi.Client" />
+    <PackageReference Include="Microsoft.AspNetCore.Mvc.NewtonsoftJson" />
     <!-- ✅ SixLabors.ImageSharp - 用於處理圖片 EXIF Orientation，修正直拍照片旋轉問題 -->
-    <PackageReference Include="SixLabors.ImageSharp" Version="3.1.12" />
+    <PackageReference Include="SixLabors.ImageSharp" />
     <!-- .NET 10 使用框架引用，不需要明確的 AspNetCore 套件 -->
     <!--
     <PackageReference Include="Microsoft.AspNetCore" Version="2.2.0" />
@@ -105,7 +107,7 @@
     -->
     <!-- BrowserLink 在新版本中已不推薦使用 -->
     <!-- <PackageReference Include="Microsoft.VisualStudio.Web.BrowserLink.Loader" Version="14.1.0" /> -->
-    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
+    <PackageReference Include="Newtonsoft.Json" />
   </ItemGroup>
   <!-- DotNetCliToolReference 在 .NET Core 3.0+ 已被移除 -->
   <!--

diff --git a/SpeechMessageProducts.sln b/SpeechMessageProducts.sln
index 0fe66079..3dde51a4 100644
--- a/SpeechMessageProducts.sln
+++ b/SpeechMessageProducts.sln
@@ -41,6 +41,20 @@ Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LineMessagingProcessor.Rich
 EndProject
 Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ToolUtility.Dataverse.Tests", "ToolUtility.Dataverse.Tests\ToolUtility.Dataverse.Tests.csproj", "{2F17EA82-5E48-44B8-ADDB-75520E43DE70}"
 EndProject
+Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "SpeechMessage.Localization", "SpeechMessage.Localization\SpeechMessage.Localization.csproj", "{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}"
+EndProject
+Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "SpeechMessage.Localization.Tests", "SpeechMessage.Localization.Tests\SpeechMessage.Localization.Tests.csproj", "{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}"
+EndProject
+Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LineMessagingProcessor.Core", "LineMessagingProcessor.Core\LineMessagingProcessor.Core.csproj", "{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}"
+EndProject
+Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "SpeechMessage.Dynamics.Plugins", "SpeechMessage.Dynamics.Plugins\SpeechMessage.Dynamics.Plugins.csproj", "{47D74DE9-0B15-48FB-B386-EE4A0E514E47}"
+EndProject
+Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "SpeechMessage.Dynamics.Plugins.Tests", "SpeechMessage.Dynamics.Plugins.Tests\SpeechMessage.Dynamics.Plugins.Tests.csproj", "{61CE6AB0-7721-45FF-9371-D435D5722A59}"
+EndProject
+Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "SpeechMessage.Dynamics.Deployment", "SpeechMessage.Dynamics.Deployment\SpeechMessage.Dynamics.Deployment.csproj", "{42636C2F-E96D-480E-8A8A-8F2167251910}"
+EndProject
+Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "SpeechMessage.Dynamics.Deployment.Tests", "SpeechMessage.Dynamics.Deployment.Tests\SpeechMessage.Dynamics.Deployment.Tests.csproj", "{A0AAE470-0442-47A1-9524-CB880B137ECA}"
+EndProject
 Global
 	GlobalSection(SolutionConfigurationPlatforms) = preSolution
 		Debug_LearnCrm|Any CPU = Debug_LearnCrm|Any CPU
@@ -630,6 +644,216 @@ Global
 		{2F17EA82-5E48-44B8-ADDB-75520E43DE70}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU
 		{2F17EA82-5E48-44B8-ADDB-75520E43DE70}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU
 		{2F17EA82-5E48-44B8-ADDB-75520E43DE70}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Debug_LearnCrm|Any CPU.ActiveCfg = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Debug_LearnCrm|Any CPU.Build.0 = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Debug_LearnCrm|x64.ActiveCfg = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Debug_LearnCrm|x64.Build.0 = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Debug_LearnCrm|x86.ActiveCfg = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Debug_LearnCrm|x86.Build.0 = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Debug|Any CPU.Build.0 = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Debug|x64.ActiveCfg = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Debug|x64.Build.0 = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Debug|x86.ActiveCfg = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Debug|x86.Build.0 = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.DebugOracleConnector|Any CPU.ActiveCfg = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.DebugOracleConnector|Any CPU.Build.0 = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.DebugOracleConnector|x64.ActiveCfg = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.DebugOracleConnector|x64.Build.0 = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.DebugOracleConnector|x86.ActiveCfg = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.DebugOracleConnector|x86.Build.0 = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Release|Any CPU.ActiveCfg = Release|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Release|Any CPU.Build.0 = Release|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Release|x64.ActiveCfg = Release|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Release|x64.Build.0 = Release|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Release|x86.ActiveCfg = Release|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Release|x86.Build.0 = Release|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Test_Exchange_Service|Any CPU.ActiveCfg = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Test_Exchange_Service|Any CPU.Build.0 = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Test_Exchange_Service|x64.ActiveCfg = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU
+		{3102E9EB-F8B2-4CBA-AE9F-C735899734EF}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Debug_LearnCrm|Any CPU.ActiveCfg = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Debug_LearnCrm|Any CPU.Build.0 = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Debug_LearnCrm|x64.ActiveCfg = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Debug_LearnCrm|x64.Build.0 = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Debug_LearnCrm|x86.ActiveCfg = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Debug_LearnCrm|x86.Build.0 = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Debug|Any CPU.Build.0 = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Debug|x64.ActiveCfg = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Debug|x64.Build.0 = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Debug|x86.ActiveCfg = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Debug|x86.Build.0 = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.DebugOracleConnector|Any CPU.ActiveCfg = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.DebugOracleConnector|Any CPU.Build.0 = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.DebugOracleConnector|x64.ActiveCfg = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.DebugOracleConnector|x64.Build.0 = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.DebugOracleConnector|x86.ActiveCfg = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.DebugOracleConnector|x86.Build.0 = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Release|Any CPU.ActiveCfg = Release|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Release|Any CPU.Build.0 = Release|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Release|x64.ActiveCfg = Release|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Release|x64.Build.0 = Release|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Release|x86.ActiveCfg = Release|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Release|x86.Build.0 = Release|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Test_Exchange_Service|Any CPU.ActiveCfg = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Test_Exchange_Service|Any CPU.Build.0 = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Test_Exchange_Service|x64.ActiveCfg = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU
+		{946C5DBE-01BA-4CE1-B24D-8B9953BAEA9C}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Debug_LearnCrm|Any CPU.ActiveCfg = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Debug_LearnCrm|Any CPU.Build.0 = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Debug_LearnCrm|x64.ActiveCfg = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Debug_LearnCrm|x64.Build.0 = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Debug_LearnCrm|x86.ActiveCfg = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Debug_LearnCrm|x86.Build.0 = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Debug|Any CPU.Build.0 = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Debug|x64.ActiveCfg = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Debug|x64.Build.0 = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Debug|x86.ActiveCfg = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Debug|x86.Build.0 = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.DebugOracleConnector|Any CPU.ActiveCfg = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.DebugOracleConnector|Any CPU.Build.0 = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.DebugOracleConnector|x64.ActiveCfg = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.DebugOracleConnector|x64.Build.0 = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.DebugOracleConnector|x86.ActiveCfg = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.DebugOracleConnector|x86.Build.0 = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Release|Any CPU.ActiveCfg = Release|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Release|Any CPU.Build.0 = Release|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Release|x64.ActiveCfg = Release|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Release|x64.Build.0 = Release|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Release|x86.ActiveCfg = Release|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Release|x86.Build.0 = Release|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Test_Exchange_Service|Any CPU.ActiveCfg = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Test_Exchange_Service|Any CPU.Build.0 = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Test_Exchange_Service|x64.ActiveCfg = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU
+		{43D6A4D0-6605-48C1-8042-A5AE5BAC495E}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Debug_LearnCrm|Any CPU.ActiveCfg = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Debug_LearnCrm|Any CPU.Build.0 = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Debug_LearnCrm|x64.ActiveCfg = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Debug_LearnCrm|x64.Build.0 = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Debug_LearnCrm|x86.ActiveCfg = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Debug_LearnCrm|x86.Build.0 = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Debug|Any CPU.Build.0 = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Debug|x64.ActiveCfg = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Debug|x64.Build.0 = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Debug|x86.ActiveCfg = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Debug|x86.Build.0 = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.DebugOracleConnector|Any CPU.ActiveCfg = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.DebugOracleConnector|Any CPU.Build.0 = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.DebugOracleConnector|x64.ActiveCfg = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.DebugOracleConnector|x64.Build.0 = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.DebugOracleConnector|x86.ActiveCfg = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.DebugOracleConnector|x86.Build.0 = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Release|Any CPU.ActiveCfg = Release|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Release|Any CPU.Build.0 = Release|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Release|x64.ActiveCfg = Release|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Release|x64.Build.0 = Release|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Release|x86.ActiveCfg = Release|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Release|x86.Build.0 = Release|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Test_Exchange_Service|Any CPU.ActiveCfg = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Test_Exchange_Service|Any CPU.Build.0 = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Test_Exchange_Service|x64.ActiveCfg = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU
+		{47D74DE9-0B15-48FB-B386-EE4A0E514E47}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Debug_LearnCrm|Any CPU.ActiveCfg = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Debug_LearnCrm|Any CPU.Build.0 = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Debug_LearnCrm|x64.ActiveCfg = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Debug_LearnCrm|x64.Build.0 = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Debug_LearnCrm|x86.ActiveCfg = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Debug_LearnCrm|x86.Build.0 = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Debug|Any CPU.Build.0 = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Debug|x64.ActiveCfg = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Debug|x64.Build.0 = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Debug|x86.ActiveCfg = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Debug|x86.Build.0 = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.DebugOracleConnector|Any CPU.ActiveCfg = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.DebugOracleConnector|Any CPU.Build.0 = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.DebugOracleConnector|x64.ActiveCfg = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.DebugOracleConnector|x64.Build.0 = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.DebugOracleConnector|x86.ActiveCfg = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.DebugOracleConnector|x86.Build.0 = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Release|Any CPU.ActiveCfg = Release|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Release|Any CPU.Build.0 = Release|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Release|x64.ActiveCfg = Release|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Release|x64.Build.0 = Release|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Release|x86.ActiveCfg = Release|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Release|x86.Build.0 = Release|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Test_Exchange_Service|Any CPU.ActiveCfg = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Test_Exchange_Service|Any CPU.Build.0 = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Test_Exchange_Service|x64.ActiveCfg = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU
+		{61CE6AB0-7721-45FF-9371-D435D5722A59}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Debug_LearnCrm|Any CPU.ActiveCfg = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Debug_LearnCrm|Any CPU.Build.0 = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Debug_LearnCrm|x64.ActiveCfg = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Debug_LearnCrm|x64.Build.0 = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Debug_LearnCrm|x86.ActiveCfg = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Debug_LearnCrm|x86.Build.0 = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Debug|Any CPU.Build.0 = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Debug|x64.ActiveCfg = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Debug|x64.Build.0 = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Debug|x86.ActiveCfg = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Debug|x86.Build.0 = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.DebugOracleConnector|Any CPU.ActiveCfg = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.DebugOracleConnector|Any CPU.Build.0 = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.DebugOracleConnector|x64.ActiveCfg = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.DebugOracleConnector|x64.Build.0 = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.DebugOracleConnector|x86.ActiveCfg = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.DebugOracleConnector|x86.Build.0 = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Release|Any CPU.ActiveCfg = Release|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Release|Any CPU.Build.0 = Release|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Release|x64.ActiveCfg = Release|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Release|x64.Build.0 = Release|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Release|x86.ActiveCfg = Release|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Release|x86.Build.0 = Release|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Test_Exchange_Service|Any CPU.ActiveCfg = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Test_Exchange_Service|Any CPU.Build.0 = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Test_Exchange_Service|x64.ActiveCfg = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU
+		{42636C2F-E96D-480E-8A8A-8F2167251910}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Debug_LearnCrm|Any CPU.ActiveCfg = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Debug_LearnCrm|Any CPU.Build.0 = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Debug_LearnCrm|x64.ActiveCfg = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Debug_LearnCrm|x64.Build.0 = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Debug_LearnCrm|x86.ActiveCfg = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Debug_LearnCrm|x86.Build.0 = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Debug|Any CPU.Build.0 = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Debug|x64.ActiveCfg = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Debug|x64.Build.0 = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Debug|x86.ActiveCfg = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Debug|x86.Build.0 = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.DebugOracleConnector|Any CPU.ActiveCfg = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.DebugOracleConnector|Any CPU.Build.0 = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.DebugOracleConnector|x64.ActiveCfg = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.DebugOracleConnector|x64.Build.0 = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.DebugOracleConnector|x86.ActiveCfg = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.DebugOracleConnector|x86.Build.0 = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Release|Any CPU.ActiveCfg = Release|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Release|Any CPU.Build.0 = Release|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Release|x64.ActiveCfg = Release|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Release|x64.Build.0 = Release|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Release|x86.ActiveCfg = Release|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Release|x86.Build.0 = Release|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Test_Exchange_Service|Any CPU.ActiveCfg = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Test_Exchange_Service|Any CPU.Build.0 = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Test_Exchange_Service|x64.ActiveCfg = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU
+		{A0AAE470-0442-47A1-9524-CB880B137ECA}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU
 	EndGlobalSection
 	GlobalSection(SolutionProperties) = preSolution
 		HideSolutionNode = FALSE

diff --git a/ToolUtility.Dataverse.Tests/FileToolUtilityTracerTests.cs b/ToolUtility.Dataverse.Tests/FileToolUtilityTracerTests.cs
index 0bd88532..bc91b057 100644
--- a/ToolUtility.Dataverse.Tests/FileToolUtilityTracerTests.cs
+++ b/ToolUtility.Dataverse.Tests/FileToolUtilityTracerTests.cs
@@ -18,6 +18,7 @@ using System.Diagnostics;
 using System.IO;
 using System.Text;
 using System.Text.RegularExpressions;
+using System.Threading;
 using ToolUtilityNameSpace.Diagnostics;
 using Xunit;
 
@@ -32,12 +33,125 @@ namespace ToolUtility.Dataverse.Tests
     /// Trace.log listener；若 legacy tracer 也加入，所有 Trace.WriteLine 都會污染
     /// CHURCH_REPORT_TRACE.TXT，並可能造成重複輸出、記憶體 retention 與關閉競態。
     ///
-    /// 所有測試都寫入暫存檔而非正式追蹤路徑，避免污染維運用的日誌。
+    /// 每例只使用自己擁有的隨機暫存路徑；Debug 可寫入並於結束清理，Release 必須維持不建檔。
     /// </remarks>
-    public class FileToolUtilityTracerTests
+    public class FileToolUtilityTracerTests : IDisposable
     {
-        private static string NewTempTracePath()
-            => Path.Combine(Path.GetTempPath(), $"toolutility-trace-{Guid.NewGuid():N}.log");
+        // xUnit 每個案例建立一個 fixture；每例最多擁有一個隨機暫存檔，不共用使用者或測試狀態。
+        private string? _tracePath;
+
+        /// <summary>
+        /// 為當次案例配置唯一暫存路徑但不建立檔案，讓 Release 的無檔案副作用可以被檢驗。
+        /// 路徑只由此 fixture 擁有，最長存活到案例結束；Dispose 會在串流釋放後刪除檔案。
+        /// </summary>
+        /// <returns>只供本次測試使用的追蹤檔路徑。</returns>
+        private string NewTempTracePath()
+        {
+            if (_tracePath != null)
+            {
+                throw new InvalidOperationException("Each fixture can own only one temporary trace path.");
+            }
+
+            return _tracePath = Path.Combine(Path.GetTempPath(), $"toolutility-trace-{Guid.NewGuid():N}.log");
+        }
+
+        /// <summary>
+        /// 在每個案例成功或失敗後清除本 fixture 的唯一暫存檔，避免重跑測試累積檔案。
+        /// 所有 tracer 與讀取串流都由測試方法的 using 先行釋放；清理失敗必須讓測試失敗，不能隱藏資源殘留。
+        /// 短暫鎖檔最多嘗試五次、間隔一百毫秒，等待總量最多四百毫秒，不建立背景工作或計時器。
+        /// </summary>
+        public void Dispose()
+        {
+            if (_tracePath != null && File.Exists(_tracePath))
+            {
+                DeleteTraceFileWithRetry(_tracePath, File.Delete, File.Exists, () => Thread.Sleep(100));
+            }
+        }
+
+        /// <summary>
+        /// 保護每個 fixture 只能配置一次暫存檔；注入第二次配置，斷言立即拒絕且第一個檔案仍由原 fixture 清理。
+        /// 即使拒絕斷言失敗，finally 也只清除本案例建立的檔案，避免紅燈本身留下孤兒檔案。
+        /// </summary>
+        [Fact]
+        public void NewTempTracePath_SecondCall_ThrowsAndKeepsOriginalOwner()
+        {
+            var path = NewTempTracePath();
+            File.WriteAllText(path, "fixture");
+            try
+            {
+                Assert.Throws<InvalidOperationException>(() => NewTempTracePath());
+                Dispose();
+                Assert.False(File.Exists(path));
+            }
+            finally
+            {
+                File.Delete(path);
+            }
+        }
+
+        /// <summary>
+        /// 保護防毒軟體短暫鎖檔時仍能有界清理；注入前兩次刪除失敗，第三次才刪除實際檔案。
+        /// 斷言嘗試三次、等待兩次且檔案消失；等待用區域計數代替真實睡眠，測試不依賴排程或時間。
+        /// </summary>
+        [Fact]
+        public void DeleteTraceFile_TransientLock_RetriesThenDrains()
+        {
+            var path = NewTempTracePath();
+            File.WriteAllText(path, "fixture");
+            var attempts = 0;
+            var waits = 0;
+            DeleteTraceFileWithRetry(path, candidate =>
+            {
+                attempts++;
+                if (attempts < 3) { throw new IOException("Injected file lock."); }
+                File.Delete(candidate);
+            }, File.Exists, () => waits++);
+            Assert.Equal(3, attempts);
+            Assert.Equal(2, waits);
+            Assert.False(File.Exists(path));
+        }
+
+        /// <summary>
+        /// 保護無法清除檔案時必須在有限次嘗試後明確失敗；注入每次刪除後檔案仍存在的情境。
+        /// 決定性斷言是五次刪除、四次等待及測試斷言例外，不能無限等待或假稱已清理。
+        /// </summary>
+        [Fact]
+        public void DeleteTraceFile_PersistentRetention_FailsAfterFiveAttempts()
+        {
+            var attempts = 0;
+            var waits = 0;
+            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
+                DeleteTraceFileWithRetry("owned-fixture", _ => attempts++, _ => true, () => waits++));
+            Assert.Equal(5, attempts);
+            Assert.Equal(4, waits);
+        }
+
+        /// <summary>
+        /// 將暫時檔案系統故障限制在最多五次清理；委派僅在本次呼叫使用，絕不保存到共用狀態。
+        /// </summary>
+        /// <param name="path">目前 fixture 唯一擁有的暫存檔。</param>
+        /// <param name="delete">刪除檔案的同步操作。</param>
+        /// <param name="exists">檢查殘留檔案的同步操作。</param>
+        /// <param name="wait">生產清理路徑每次等待一百毫秒；故障測試使用不睡眠的區域計數。</param>
+        private static void DeleteTraceFileWithRetry(string path, Action<string> delete, Func<string, bool> exists, Action wait)
+        {
+            for (var attempt = 0; attempt < 5; attempt++)
+            {
+                try
+                {
+                    delete(path);
+                    if (!exists(path)) { return; }
+                }
+                catch (IOException) when (attempt < 4)
+                {
+                    // 只容忍短暫檔案鎖；最後一次的真實刪除失敗仍須直接回報。
+                }
+
+                if (attempt < 4) { wait(); }
+            }
+
+            Assert.False(exists(path), "案例結束後暫存檔必須在五次有界重試內回到不存在基線");
+        }
 
         /// <summary>
         /// 保護的契約：單一 tracer 反覆輸出時，全域 listener 集合不得成長。
@@ -52,11 +166,11 @@ namespace ToolUtility.Dataverse.Tests
         public void Write_ManyTimes_DoesNotGrowTraceListeners()
         {
             var path = NewTempTracePath();
+            var baseline = Trace.Listeners.Count;
             using var tracer = new FileToolUtilityTracer(path);
 
             // 第一次輸出才會延遲建立私有串流；legacy tracer 不得掛上全域 listener。
             tracer.Write(5, 1, "第一次輸出", new StackFrame(0, true));
-            var baseline = Trace.Listeners.Count;
 
             for (var i = 0; i < 100; i++)
             {
@@ -70,9 +184,9 @@ namespace ToolUtility.Dataverse.Tests
         /// 保護的契約：釋放時必須確定性關閉私有 writer，且不應改動全域 listener 集合。
         /// </summary>
         /// <remarks>
-        /// 故障注入方式：建立 tracer、輸出一次使其掛上 listener，然後 Dispose。
-        /// 決定性斷言：Dispose 前後的 listener 數量維持不變，且檔案 handle 可重新開啟。
-        /// 這同時證明釋放路徑是確定性的，不依賴 GC。
+        /// 故障注入方式：建立 tracer、嘗試輸出一次後 Dispose；tracer 始終不得加入全域 listener。
+        /// 決定性斷言：listener 數量維持不變；Debug 產生的檔案能以獨佔模式重開，Release 則完全不建檔。
+        /// Debug 驗證實際 handle 確定釋放，Release 驗證停用時沒有檔案副作用；兩種設定都不依賴 GC。
         /// </remarks>
         [Fact]
         public void Dispose_RemovesListenerFromGlobalCollection()
@@ -80,16 +194,20 @@ namespace ToolUtility.Dataverse.Tests
             var path = NewTempTracePath();
             var before = Trace.Listeners.Count;
 
-            var tracer = new FileToolUtilityTracer(path);
-            tracer.Write(5, 1, "寫入私有 writer", new StackFrame(0, true));
-            Assert.Equal(before, Trace.Listeners.Count);
-
-            tracer.Dispose();
+            using (var tracer = new FileToolUtilityTracer(path))
+            {
+                tracer.Write(5, 1, "寫入私有 writer", new StackFrame(0, true));
+                Assert.Equal(before, Trace.Listeners.Count);
+            }
 
             Assert.Equal(before, Trace.Listeners.Count);
 
-            using var reopened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
+#if DEBUG
+            using var reopened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
             Assert.True(reopened.Length > 0, "Dispose 後檔案應可重新開啟且保留輸出");
+#else
+            Assert.False(File.Exists(path), "Release 的 legacy 追蹤停用契約不得因測試要求而改為寫檔");
+#endif
         }
 
         /// <summary>
@@ -112,9 +230,9 @@ namespace ToolUtility.Dataverse.Tests
         }
 
         /// <summary>
-        /// 保護 legacy 時間欄位的跨文化穩定格式。故障注入是以目前程序文化寫入一筆
-        /// legacy 訊息；決定性斷言是以 Big5 讀回後符合 ISO-like 的固定毫秒格式，避免
-        /// 分析器因伺服器地區設定不同而無法建立時間範圍。
+        /// 保護 legacy 時間欄位格式及 Release 停用契約。注入一筆虛構訊息；Debug 以 Big5
+        /// 讀回並斷言固定毫秒格式，Release 則斷言沒有建立檔案。只驗證格式而不比較系統時間值，
+        /// 避免測試依賴執行日期；所有 writer、stream 與暫存檔在本案例結束時確定釋放。
         /// </summary>
         [Fact]
         public void Write_uses_culture_invariant_legacy_timestamp_format()
@@ -125,12 +243,16 @@ namespace ToolUtility.Dataverse.Tests
                 tracer.Write(5, 1, "固定時間格式測試", new StackFrame(0, true));
             }
 
+#if DEBUG
             Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
             var big5 = Encoding.GetEncoding(950);
             var text = File.ReadAllText(path, big5);
             Assert.Matches(
                 new Regex(@"Time\s+=\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}", RegexOptions.CultureInvariant),
                 text);
+#else
+            Assert.False(File.Exists(path), "Release 不應建立追蹤檔或輸出任何 legacy 時間欄位");
+#endif
         }
     }
 }

diff --git a/ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj b/ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
index bf92b70c..3c203c92 100644
--- a/ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
+++ b/ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
@@ -9,13 +9,13 @@
   </PropertyGroup>
 
   <ItemGroup>
-    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
-    <PackageReference Include="xunit" Version="2.6.6" />
-    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="xunit" />
+    <PackageReference Include="xunit.runner.visualstudio">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
-    <PackageReference Include="Moq" Version="4.20.70" />
+    <PackageReference Include="Moq" />
   </ItemGroup>
 
   <ItemGroup>
```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.