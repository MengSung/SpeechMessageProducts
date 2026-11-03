# 階段0 第1批 第2/3份：守門工具與 CI

你是唯讀外部審查者。本輪為「單模型（使用者指定 ClaudeOnly）」，不是雙模型或配額降級。只審查，不改檔、不提交、不執行產品／工具／測試，不建立任何網路或 D365／LINE 連線。
審查重點：跨使用者／跨教會隔離、資源釋放、機密外洩、例外責任（設計10.5／10.9）、net48/net10行為一致、守門假綠燈。所有真實連線驗收仍由使用者負責。
機密欄位已遮罩；遮罩值不可用來推斷實際設定正確性。不得讀取 appsettings*、User Secrets、環境變數、Logs、金鑰／授權檔，或以 git show 取未遮罩版本。只以所附安全差異審查；如需上下文，可只查明確的非機密程式來源。不要開啟 .ccg/dual-model-runs 的其他歷史輸出或 research 原始紀錄。
輸出只列可驗證的 Critical／High／Medium／Low，包含檔案、行號、觸發條件、具體影響及最小修法；測試不足須說明可重現的缺口，不把純猜測當發現。沒有問題請明寫。不得聲稱實跑測試。
已核定例外：嚴格 .Tests 測試專案僅 CA1707 放寬；未實作 API 的 CA1716 可改語意近名；Core 不通知，Plugin 僅追蹤，最終告警在 net10 重試用盡／階段5；正式金鑰只程序環境且不讀內容；本階段不存在業務 Rejected。小組頁面由使用者確認恢復，圖表未逐項驗證，之前審查兩次為逾時。

範圍：73b6e6cf409daa7559fd3c11d482e3efc542c0d7..ae29cb40aa5c395f83c169068d7ca40e7094c509
安全差異 SHA256：9d98223da51abc2e2e98d4dea3a4f9aaccd1340a9c187d3d13d66ab1fc97ad36
同批其他分份須一併審查後才算完成，本份不可代表全批。
專項：檔案掃描邊界只限本 worktree／方案；編碼、重複、分析器、NuGetAudit、覆蓋率、生成檔與 CI 是否可能假綠燈；一般金鑰缺失失敗及 runner 清理；簽章只能明確要求。P0-01 的既有測試修復不得削弱斷言。

```diff
diff --git a/ToolUtility.Tests/ToolUtility.Tests.csproj b/ToolUtility.Tests/ToolUtility.Tests.csproj
index 5c49f616..309c7461 100644
--- a/ToolUtility.Tests/ToolUtility.Tests.csproj
+++ b/ToolUtility.Tests/ToolUtility.Tests.csproj
@@ -10,25 +10,25 @@
 
   <ItemGroup>
     <!-- 測試框架 -->
-    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
-    <PackageReference Include="xunit" Version="2.6.6" />
-    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="xunit" />
+    <PackageReference Include="xunit.runner.visualstudio">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
     
     <!-- Mock 框架 -->
-    <PackageReference Include="Moq" Version="4.20.70" />
+    <PackageReference Include="Moq" />
     
     <!-- 斷言庫 -->
-    <PackageReference Include="FluentAssertions" Version="6.12.0" />
+    <PackageReference Include="FluentAssertions" />
     
     <!-- 程式碼覆蓋率 -->
-    <PackageReference Include="coverlet.collector" Version="6.0.0">
+    <PackageReference Include="coverlet.collector">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
-    <PackageReference Include="coverlet.msbuild" Version="6.0.0">
+    <PackageReference Include="coverlet.msbuild">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
@@ -41,8 +41,8 @@
 
   <ItemGroup>
     <!-- CRM SDK 套件（用於測試） -->
-    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" Version="9.0.2.56" />
-    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
+    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" />
+    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
   </ItemGroup>
 
 </Project>

diff --git a/build/Plugin/BannedSymbols.txt b/build/Plugin/BannedSymbols.txt
new file mode 100644
index 00000000..304b4abb
--- /dev/null
+++ b/build/Plugin/BannedSymbols.txt
@@ -0,0 +1 @@
+N:System.Drawing;沙箱不支援 GDI

diff --git a/build/PluginMerge.targets b/build/PluginMerge.targets
new file mode 100644
index 00000000..4a7a162b
--- /dev/null
+++ b/build/PluginMerge.targets
@@ -0,0 +1,34 @@
+<Project>
+  <!-- 套件預設會覆寫 bin 並併入所有相依；此自訂入口只產生獨立的沙箱部署組件。
+       只有明確指定 SpeechMessagePackagePlugin=true 才打包；一般建置不讀金鑰、不建立 artifacts。
+       正式金鑰只能讀取程序環境變數，不複製內容、不設後備、不由 MSBuild 同名屬性冒充。 -->
+  <Target Name="PackageSpeechMessagePlugin" AfterTargets="Build" Condition="'$(SpeechMessagePackagePlugin)' == 'true' And '$(DesignTimeBuild)' != 'true'">
+    <PropertyGroup>
+      <_SpeechMessagePluginKeyFile>$([System.Environment]::GetEnvironmentVariable('SPEECHMESSAGE_PLUGIN_KEYFILE'))</_SpeechMessagePluginKeyFile>
+      <SpeechMessagePluginOutputDirectory Condition="'$(SpeechMessagePluginOutputDirectory)' == ''">$(MSBuildThisFileDirectory)../artifacts/plugins</SpeechMessagePluginOutputDirectory>
+      <_SpeechMessagePluginOutput>$(SpeechMessagePluginOutputDirectory)/$(TargetFileName)</_SpeechMessagePluginOutput>
+    </PropertyGroup>
+    <Error Condition="'$(_SpeechMessagePluginKeyFile)' == '' And '$(Configuration)' == 'Release'"
+           Code="SMP0001" Text="Release 外掛打包必須設定 SPEECHMESSAGE_PLUGIN_KEYFILE；請由使用者準備 repo 外的正式強式名稱金鑰。" />
+    <Error Condition="'$(_SpeechMessagePluginKeyFile)' != '' And !Exists('$(_SpeechMessagePluginKeyFile)')"
+           Code="SMP0003" Text="SPEECHMESSAGE_PLUGIN_KEYFILE 指定的金鑰不存在；禁止改用其他金鑰或產生正式金鑰。" />
+    <Warning Condition="'$(_SpeechMessagePluginKeyFile)' == '' And '$(Configuration)' != 'Release'"
+             Code="SMP0002" Text="未設定 SPEECHMESSAGE_PLUGIN_KEYFILE；本次 Debug 只產生未簽章診斷組件，不可部署。" />
+    <ItemGroup>
+      <!-- 第一個輸入是唯一公開外殼；其餘型別全部內部化，SDK 仍由 D365 沙箱提供。 -->
+      <_SpeechMessageMergeInput Include="$(TargetPath)" />
+      <_SpeechMessageMergeInput Include="$(TargetDir)SpeechMessage.Localization.dll" />
+      <_SpeechMessageMergeInput Include="$(TargetDir)LineMessagingProcessor.Core.dll" />
+      <_SpeechMessageMergeInput Include="$(TargetDir)Newtonsoft.Json.dll" />
+    </ItemGroup>
+    <MakeDir Directories="$(SpeechMessagePluginOutputDirectory)" />
+    <ILRepack InputAssemblies="@(_SpeechMessageMergeInput)"
+              OutputFile="$(_SpeechMessagePluginOutput)"
+              TargetKind="Dll"
+              Internalize="true"
+              KeyFile="$(_SpeechMessagePluginKeyFile)"
+              LibraryPath="$(TargetDir);$(TargetFrameworkDirectory)"
+              DebugInfo="false"
+              Parallel="true" />
+  </Target>
+</Project>

diff --git a/build/StrictCode.globalconfig b/build/StrictCode.globalconfig
new file mode 100644
index 00000000..93996b31
--- /dev/null
+++ b/build/StrictCode.globalconfig
@@ -0,0 +1,8 @@
+# 只由 SpeechMessageStrictCode 啟用的新專案載入；三項比較規則必須實際執行。
+is_global = true
+global_level = 100
+
+dotnet_diagnostic.CA1307.severity = error
+dotnet_diagnostic.CA1309.severity = error
+dotnet_diagnostic.CA1310.severity = error
+dotnet_diagnostic.RS0030.severity = error

diff --git a/docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1 b/docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1
index 8fd3c0b1..dc59ced0 100644
--- a/docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1
+++ b/docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1
@@ -1,4 +1,4 @@
-﻿param(
+param(
     [Parameter(Mandatory = $true)]
     [ValidateScript({ Test-Path -LiteralPath $_ })]
     [string]$TaskFile,
@@ -11,7 +11,9 @@
     [int]$TimeoutSeconds = 900,
     [int]$MaxAttempts = 2,
     [switch]$RunHealthBackendSmoke,
-    [switch]$AllowSingleModelWhenQuotaBlocked
+    [switch]$AllowSingleModelWhenQuotaBlocked,
+    # 選用單模型是使用者的執行策略，不得標示為雙模型成功或配額降級。
+    [switch]$ClaudeOnly
 )
 
 $ErrorActionPreference = 'Stop'
@@ -500,6 +502,42 @@ function Invoke-GeminiDirectQuotaProbe {
     }
 }
 
+function Get-CcgReviewCompletion {
+    param([switch]$ClaudeOnly, [string[]]$CompletedBackends = @())
+    # 僅彙整本次已驗證完成的後端名稱，不保留提示、帳號或其他可變會話狀態。
+    # ok 是要求是否完成；雙模型完成是獨立契約，不能由成功結束碼推導。
+    $required = if ($ClaudeOnly) { @('claude') } else { @('gemini', 'claude') }
+    $missing = @($required | Where-Object { $CompletedBackends -notcontains $_ })
+    return [pscustomobject]@{
+        ok = ($missing.Count -eq 0)
+        reviewMode = $(if ($ClaudeOnly) { 'claude-only' } else { 'dual' })
+        dualModelComplete = ($CompletedBackends -contains 'gemini' -and $CompletedBackends -contains 'claude')
+    }
+}
+
+function ConvertTo-CcgRunMarkdown {
+    param([Parameter(Mandatory = $true)]$Summary)
+    # 文字摘要與 JSON 使用相同欄位；兩個完成指標都必須公開，避免人工或自動流程誤判。
+    return ((@(
+        $(if ($Summary.reviewMode -eq 'claude-only') { '# CCG Claude-only Run Summary' } else { '# CCG Dual Model Run Summary' }),
+        '',
+        "- RunId: $($Summary.runId)",
+        "- Role: $($Summary.role)",
+        "- ReviewMode: $($Summary.reviewMode)",
+        "- DualModelComplete: $($Summary.dualModelComplete)",
+        "- RequestedBackends: $($Summary.requestedBackends -join ', ')",
+        "- Repository: $($Summary.repositoryPath)",
+        "- TaskFile: $($Summary.taskFile)",
+        "- OK: $($Summary.ok)",
+        "- DegradedFallback: $($Summary.degradedFallback)",
+        "- FallbackAccepted: $($Summary.fallbackAccepted)",
+        "- QuotaBlocked: $($Summary.quotaBlocked)",
+        "- CompletedBackends: $($Summary.completedBackends -join ', ')",
+        "- FailedBackends: $($Summary.failedBackends -join ', ')",
+        '', 'Artifacts are stored in:', '', $Summary.runDirectory
+    ) | ForEach-Object { ([string]$_).TrimEnd() }) -join [Environment]::NewLine)
+}
+
 $toolchainEnvironment = Initialize-CcgToolchainEnvironment
 
 $repositoryFullPath = (Resolve-Path -LiteralPath $RepositoryPath).Path
@@ -527,15 +565,21 @@ if (-not (Test-Path -LiteralPath $healthScript)) {
     throw "Missing health script: $healthScript"
 }
 
+$requestedBackends = if ($ClaudeOnly) { @("claude") } else { @("gemini", "claude") }
+$initialCompletion = Get-CcgReviewCompletion -ClaudeOnly:$ClaudeOnly
 $summary = [ordered]@{
     runId = $runId
     role = $Role
+    requestedBackends = @($requestedBackends)
+    claudeOnly = [bool]$ClaudeOnly
+    reviewMode = $initialCompletion.reviewMode
+    dualModelComplete = $false
     repositoryPath = $repositoryFullPath
     taskFile = $resolvedTaskFile
     runDirectory = $runDirectory
     wrapperPath = $wrapperPath
     toolchainEnvironment = $toolchainEnvironment
-    healthBackendSmoke = [bool]$RunHealthBackendSmoke
+    healthBackendSmoke = [bool]($RunHealthBackendSmoke -and -not $ClaudeOnly)
     attempts = @()
     ok = $false
     degradedFallback = $false
@@ -551,7 +595,8 @@ for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
 
     $healthOutputPath = Join-Path $runDirectory "health-attempt-$attempt.json"
     $healthArguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $healthScript, "-RepositoryPath", $repositoryFullPath, "-OutputDirectory", $runDirectory)
-    if (-not $RunHealthBackendSmoke) {
+    # 舊健康檢查的 smoke 會呼叫兩個供應商；單模型模式只檢查本機工具鏈。
+    if (-not $RunHealthBackendSmoke -or $ClaudeOnly) {
         $healthArguments += "-SkipBackendSmoke"
     }
 
@@ -611,7 +656,7 @@ for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
         break
     }
 
-    foreach ($backend in @("gemini", "claude")) {
+    foreach ($backend in $requestedBackends) {
         $prompt = New-BackendPrompt -Backend $backend -Role $Role -TaskText $taskText
         $promptPath = Join-Path $runDirectory "$backend-$Role-attempt-$attempt.prompt.md"
         $stdoutPath = Join-Path $runDirectory "$backend-$Role-attempt-$attempt.stdout.md"
@@ -630,7 +675,11 @@ for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
         [System.IO.File]::WriteAllText($stderrPath, $result.StdErr, [System.Text.UTF8Encoding]::new($false))
 
         $combined = (($result.StdOut + "`n" + $result.StdErr) -replace "`r", "")
-        $diagnostic = $null
+        $diagnostic = if ($result.ExitCode -ne 0 -or $result.TimedOut) {
+            Get-ShortDiagnostic -Text $result.StdErr -Fallback 'Backend produced no diagnostic.'
+        } else { $null }
+        $probeDiagnostic = $null
+        $probePath = $null
         $quotaBlocked = Test-BackendQuotaBlocked -Result $result -Diagnostic $diagnostic
 
         if ($backend -eq "gemini" -and -not $quotaBlocked -and $result.ExitCode -ne 0) {
@@ -639,9 +688,11 @@ for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
             # exhaustion. Probe directly so quota/billing blocks are not
             # misclassified as local toolchain failures.
             $directProbe = Invoke-GeminiDirectQuotaProbe -WorkingDirectory $repositoryFullPath
+            $probePath = Join-Path $runDirectory "gemini-probe-attempt-$attempt.stdout.md"
+            [System.IO.File]::WriteAllText($probePath, $directProbe.Output, [System.Text.UTF8Encoding]::new($false))
+            $probeDiagnostic = Get-ShortDiagnostic -Text $directProbe.Output -Fallback 'Gemini probe produced no diagnostic.'
             if ($directProbe.QuotaBlocked) {
                 $quotaBlocked = $true
-                $diagnostic = $directProbe.Output
             }
         }
 
@@ -651,9 +702,12 @@ for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
             # direct probe so the pipeline can stop as "external quota blocked"
             # instead of repeatedly trying local repairs that cannot help.
             $directProbe = Invoke-ClaudeDirectQuotaProbe -WorkingDirectory $repositoryFullPath
+            # wrapper 有時只回傳 exit 1；保存既有 probe 的實際診斷，才能修復登入／參數問題。
+            $probePath = Join-Path $runDirectory "claude-probe-attempt-$attempt.stdout.md"
+            [System.IO.File]::WriteAllText($probePath, $directProbe.Output, [System.Text.UTF8Encoding]::new($false))
+            $probeDiagnostic = Get-ShortDiagnostic -Text $directProbe.Output -Fallback "Claude probe produced no diagnostic."
             if ($directProbe.QuotaBlocked) {
                 $quotaBlocked = $true
-                $diagnostic = $directProbe.Output
             }
         }
 
@@ -674,6 +728,8 @@ for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
             producedOutput = $producedOutput
             outputLength = (Get-ModelResponseText -StdOut $result.StdOut).Length
             diagnostic = $diagnostic
+            probeDiagnostic = $probeDiagnostic
+            probe = $probePath
             prompt = $promptPath
             stdout = $stdoutPath
             stderr = $stderrPath
@@ -685,7 +741,7 @@ for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
     $failed = @($attemptRecord.backends | Where-Object { -not $_.ok })
     if ($failed.Count -eq 0) {
         $summary.ok = $true
-        $summary.completedBackends = @("gemini", "claude")
+        $summary.completedBackends = @($requestedBackends)
         break
     }
 
@@ -708,28 +764,15 @@ if (-not $summary.ok -and -not $summary.failedBackends.Count) {
     $summary.failedBackends = @($lastAttempt.backends | Where-Object { -not $_.ok } | ForEach-Object { $_.backend })
 }
 
+$completion = Get-CcgReviewCompletion -ClaudeOnly:$ClaudeOnly -CompletedBackends @($summary.completedBackends)
+$summary.ok = $completion.ok
+$summary.dualModelComplete = $completion.dualModelComplete
 $summaryPath = Join-Path $runDirectory "summary.json"
 $markdownPath = Join-Path $runDirectory "summary.md"
-$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
-
-$markdown = @(
-    "# CCG Dual Model Run Summary",
-    "",
-    "- RunId: $runId",
-    "- Role: $Role",
-    "- Repository: $repositoryFullPath",
-    "- TaskFile: $resolvedTaskFile",
-    "- OK: $($summary.ok)",
-    "- DegradedFallback: $($summary.degradedFallback)",
-    "- FallbackAccepted: $($summary.fallbackAccepted)",
-    "- QuotaBlocked: $($summary.quotaBlocked)",
-    "- CompletedBackends: $($summary.completedBackends -join ', ')",
-    "- FailedBackends: $($summary.failedBackends -join ', ')",
-    "",
-    "Artifacts are stored in:",
-    "",
-    $runDirectory
-) -join [Environment]::NewLine
+$summaryJson = ($summary | ConvertTo-Json -Depth 10) + [Environment]::NewLine
+[System.IO.File]::WriteAllText($summaryPath, $summaryJson, [System.Text.UTF8Encoding]::new($false))
+
+$markdown = (ConvertTo-CcgRunMarkdown -Summary $summary) + [Environment]::NewLine
 [System.IO.File]::WriteAllText($markdownPath, $markdown, [System.Text.UTF8Encoding]::new($false))
 
 $summary | ConvertTo-Json -Depth 10

diff --git a/docs/scripts/Start-CcgDualModelRun.ps1 b/docs/scripts/Start-CcgDualModelRun.ps1
index c27b3b50..62d5e842 100644
--- a/docs/scripts/Start-CcgDualModelRun.ps1
+++ b/docs/scripts/Start-CcgDualModelRun.ps1
@@ -13,7 +13,9 @@ param(
     [int]$TimeoutSeconds = 900,
     [int]$MaxAttempts = 2,
     [switch]$AllowSingleModelWhenQuotaBlocked,
-    [switch]$RunHealthBackendSmoke
+    [switch]$RunHealthBackendSmoke,
+    # 使用者明確選擇時只執行 Claude；預設仍保留原有雙模型流程。
+    [switch]$ClaudeOnly
 )
 
 $ErrorActionPreference = 'Stop'
@@ -113,6 +115,10 @@ if ($RunHealthBackendSmoke) {
     $runnerArgs += '-RunHealthBackendSmoke'
 }
 
+if ($ClaudeOnly) {
+    $runnerArgs += '-ClaudeOnly'
+}
+
 & powershell.exe @runnerArgs
 $runnerExitCode = $LASTEXITCODE
 
@@ -121,7 +127,7 @@ $runnerExitCode = $LASTEXITCODE
 # 底層 runner 在 `-AllowSingleModelWhenQuotaBlocked` 啟用時，若一個模型成功、
 # 另一個模型只是 quota/session 被擋，會以 exit code 0 讓任務不中斷。
 # 但是 exit code 0 不代表一定是完整雙模型成功；呼叫端仍必須看 summary.json 的
-# `ok`、`degradedFallback`、`quotaBlocked` 欄位。
+# `dualModelComplete`、`reviewMode`、`degradedFallback`、`quotaBlocked` 欄位。
 #
 # 這裡刻意用本次 task 檔名 stem 比對 run folder，而不是單純抓最新 summary.json。
 # 原因是 CCG run 可能連續執行，若只依 LastWriteTime 取最新資料夾，未來在並行或
@@ -143,7 +149,9 @@ if ($latestSummaryDirectory) {
         $summary = Get-Content -LiteralPath $summaryPath -Encoding UTF8 -Raw | ConvertFrom-Json
         if ($summary.degradedFallback) {
             Write-Warning "CCG completed with DEGRADED FALLBACK. At least one backend succeeded, but this is not a completed dual-model review. Read: $summaryPath"
-        } elseif ($summary.ok) {
+        } elseif ($summary.ok -and $summary.reviewMode -eq 'claude-only') {
+            Write-Host "CCG completed with requested Claude-only success. Read: $summaryPath"
+        } elseif ($summary.dualModelComplete) {
             Write-Host "CCG completed with full dual-model success. Read: $summaryPath"
         } elseif ($summary.quotaBlocked) {
             Write-Warning "CCG stopped on provider quota/session state and did not produce an accepted fallback. Read: $summaryPath"

diff --git a/docs/scripts/Tests/Test-CcgReviewSummary.ps1 b/docs/scripts/Tests/Test-CcgReviewSummary.ps1
new file mode 100644
index 00000000..71de98bd
--- /dev/null
+++ b/docs/scripts/Tests/Test-CcgReviewSummary.ps1
@@ -0,0 +1,56 @@
+param([string]$RunnerPath = (Join-Path $PSScriptRoot '..\Invoke-CcgDualModelWithSelfHealing.ps1'))
+
+$ErrorActionPreference = 'Stop'
+# 只載入摘要純函式；不可執行 runner 主流程，以免測試呼叫模型、變更 PATH 或留下子程序。
+$tokens = $null
+$parseErrors = $null
+$ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path -LiteralPath $RunnerPath).Path, [ref]$tokens, [ref]$parseErrors)
+if ($parseErrors.Count -gt 0) { throw 'Runner parsing failed.' }
+foreach ($name in @('Get-CcgReviewCompletion', 'ConvertTo-CcgRunMarkdown')) {
+    $definition = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $false)
+    if (-not $definition) { throw "Missing summary function: $name" }
+    . ([scriptblock]::Create($definition.Extent.Text))
+}
+
+function Assert-Condition {
+    param([bool]$Condition, [string]$Message)
+    if (-not $Condition) { throw $Message }
+}
+
+# 固定期望值分別保護要求完成、雙模型完成及文字產物，不從被測函式推導期望。
+$cases = [ordered]@{
+    Dual_AllRequestedCompleted_IsDualComplete = {
+        $actual = Get-CcgReviewCompletion -CompletedBackends @('gemini', 'claude')
+        Assert-Condition ($actual.ok -and $actual.reviewMode -eq 'dual' -and $actual.dualModelComplete) 'Dual completion contract failed.'
+    }
+    ClaudeOnly_Completed_IsOkButNotDual = {
+        $actual = Get-CcgReviewCompletion -ClaudeOnly -CompletedBackends @('claude')
+        Assert-Condition ($actual.ok -and $actual.reviewMode -eq 'claude-only' -and -not $actual.dualModelComplete) 'Claude-only must never imply dual completion.'
+    }
+    Dual_OneCompleted_IsIncomplete = {
+        $actual = Get-CcgReviewCompletion -CompletedBackends @('claude')
+        Assert-Condition (-not $actual.ok -and -not $actual.dualModelComplete -and $actual.reviewMode -eq 'dual') 'A partial dual run must remain incomplete.'
+    }
+    ClaudeOnly_NoOutput_IsIncomplete = {
+        $actual = Get-CcgReviewCompletion -ClaudeOnly
+        Assert-Condition (-not $actual.ok -and -not $actual.dualModelComplete) 'No output cannot complete a review.'
+    }
+    Json_ContainsExplicitModeAndCompletion = {
+        $actual = Get-CcgReviewCompletion -ClaudeOnly -CompletedBackends @('claude') | ConvertTo-Json | ConvertFrom-Json
+        Assert-Condition ($actual.PSObject.Properties.Name -contains 'dualModelComplete') 'Missing dualModelComplete JSON field.'
+        Assert-Condition ($actual.reviewMode -eq 'claude-only' -and $actual.ok -and -not $actual.dualModelComplete) 'Serialized review metadata is misleading.'
+    }
+    Markdown_ContainsModeAndDualCompletion = {
+        $sample = [ordered]@{ runId='fixture'; role='reviewer'; requestedBackends=@('claude'); repositoryPath='X:/repo'; taskFile='request.md'; ok=$true; reviewMode='claude-only'; dualModelComplete=$false; degradedFallback=$false; fallbackAccepted=$false; quotaBlocked=$false; completedBackends=@('claude'); failedBackends=@(); runDirectory='X:/audit' }
+        $text = ConvertTo-CcgRunMarkdown -Summary $sample
+        Assert-Condition ($text.Contains('- ReviewMode: claude-only') -and $text.Contains('- DualModelComplete: False')) 'Markdown must expose explicit completion metadata.'
+        Assert-Condition (-not [regex]::IsMatch($text, '(?m)[ \t]+\r?$')) 'Markdown must not contain trailing whitespace.'
+    }
+}
+$failed = 0
+foreach ($entry in $cases.GetEnumerator()) {
+    try { & $entry.Value; Write-Output ("PASS " + $entry.Key) }
+    catch { $failed++; Write-Output ("FAIL " + $entry.Key + ': ' + $_.Exception.Message) }
+}
+Write-Output ("Summary tests: " + ($cases.Count - $failed) + '/' + $cases.Count)
+if ($failed -gt 0) { exit 1 }

diff --git a/global.json b/global.json
new file mode 100644
index 00000000..d9997a01
--- /dev/null
+++ b/global.json
@@ -0,0 +1,6 @@
+{
+  "sdk": {
+    "version": "10.0.403",
+    "rollForward": "latestFeature"
+  }
+}

diff --git a/tools/I18nCheck.Tests/BoundaryTests.cs b/tools/I18nCheck.Tests/BoundaryTests.cs
new file mode 100644
index 000000000..0de9bc1e5
--- /dev/null
+++ b/tools/I18nCheck.Tests/BoundaryTests.cs
@@ -0,0 +1,291 @@
+using System.Text;
+using System.Text.Json;
+using System.Xml;
+using Xunit;
+
+namespace I18nCheck.Tests;
+
+/// <summary>
+/// 以假原始碼、假資源與獨佔目錄保護掃描準確性、Unicode、檔案所有權與命令列棘輪。
+/// 測試失敗時指出真正的語法、資料或生命週期契約，不依賴網路、主機文化或真實組織。
+/// </summary>
+public sealed class BoundaryTests
+{
+    /// <summary>注入不同 C# 字串語法與範圍邊界；以明列數量保護 Roslyn 語法計數。</summary>
+    /// <param name="source">完整假來源。</param>
+    /// <param name="count">預期常值數量。</param>
+    [Theory(DisplayName = "注入不同 C# 字串語法與範圍邊界")]
+    [InlineData("class C { string X = @\"中文\"; }", 1)]
+    [InlineData("class C { string X = \"\\u4e2d文\"; }", 1)]
+    [InlineData("class C { string X = \"\"\"中文\"\"\"; }", 1)]
+    [InlineData("class C { string X = $\"中文{1}中文\"; }", 1)]
+    [InlineData("class C { string X = $\"English{\"中文\"}\"; }", 1)]
+    [InlineData("class C { string X = \"㐀\" + \"鿿\" + \"豈\" + \"﫿\"; }", 4)]
+    [InlineData("class C { char X = '中'; string Y = \"English\"; }", 0)]
+    [InlineData("class C { [I18nIgnore(\"\")] string X = \"中文\"; }", 1)]
+    [InlineData("class C { [I18nIgnore(\"協定\")] string X = \"中文\"; }", 0)]
+    public void Scan_SyntaxAndRanges_UsesLiteralValues(string source, int count) => Assert.Equal(count, SourceScanner.CountCSharp(source));
+
+    /// <summary>相似方法名稱不得造成假排除；真正診斷只排除模板或 Trace／Debug 引數。</summary>
+    /// <param name="source">含同名干擾或真實診斷型別的來源。</param>
+    /// <param name="count">仍應納入的文字數。</param>
+    [Theory(DisplayName = "相似方法名稱不得造成假排除")]
+    [InlineData("class C { void M(dynamic logger) { logger.LogInformation(\"顯示\"); } }", 1)]
+    [InlineData("using System.Diagnostics; class C { void M() { Trace.WriteLine(\"診斷\"); Debug.WriteLine(\"診斷\"); } }", 0)]
+    [InlineData("class Trace { public static void WriteLine(string x) {} } class C { void M() { Trace.WriteLine(\"顯示\"); } }", 1)]
+    [InlineData("using Microsoft.Extensions.Logging; class C { void M(ILogger log) { log.LogWarning(\"診斷 {Value}\", \"顯示值\"); } }", 1)]
+    [InlineData("using Microsoft.Extensions.Logging; class C { void M(ILogger log) { log.LogError(message: \"診斷\"); } }", 0)]
+    public void Scan_Diagnostics_ExcludesOnlyProvenTemplates(string source, int count) => Assert.Equal(count, SourceScanner.CountCSharp(source));
+
+    /// <summary>僅計算可見節點與四種指定屬性；Razor、註解與腳本字串不得被假算。</summary>
+    /// <param name="source">最小 Razor 片段。</param>
+    /// <param name="count">CJK 顯示片段數。</param>
+    [Theory(DisplayName = "僅計算可見節點與四種指定屬性")]
+    [InlineData("<p>中文</p><p>中文</p>", 2)]
+    [InlineData("<input title='標題' placeholder=\"提示\" alt='替代' value='值' data-note='忽略' />", 4)]
+    [InlineData("<!-- 中文 -- > -->@* 中文 *@<script>const x='中文';</script><style>中文</style>", 0)]
+    [InlineData("@{ var x = \"中文\"; }<p>顯示 @Model.Name</p>", 1)]
+    [InlineData("@if (Model.Ready) { <p title='提示'>中文</p> }", 2)]
+    [InlineData("<input value='@Model.Name' /><p>@(\"程式字串\")</p>", 0)]
+    public void Scan_Razor_OnlyCountsDisplayFragments(string source, int count) => Assert.Equal(count, SourceScanner.CountRazor(source));
+
+    /// <summary>補充平面字元必須按 Rune 計數，空白與標點不可被修剪，參數重複不增加字數。</summary>
+    /// <param name="text">Unicode 與參數邊界輸入。</param>
+    /// <param name="expected">獨立列出的完整答案。</param>
+    [Theory(DisplayName = "補充平面字元必須按 Rune 計數")]
+    [InlineData("😀😀😀", "［😀😀😀ǂǂ］")]
+    [InlineData("   ", "［   ǂǂ］")]
+    [InlineData("", "［ǂ］")]
+    [InlineData("{name}{name}", "［{name}{name}ǂ］")]
+    [InlineData("{a}一{b}二", "［{a}一{b}二ǂ］")]
+    public void Pseudo_UnicodeAndWhitespace_CountsRunes(string text, string expected) => Assert.Equal(expected, PseudoLocale.Transform(text));
+
+    /// <summary>參數辨識不得任意限縮為 ASCII 名稱；Unicode 及數字名稱都由同一解析器排除。</summary>
+    /// <param name="text">不含非參數文字的輸入。</param>
+    [Theory(DisplayName = "參數辨識不得任意限縮為 ASCII 名稱")]
+    [InlineData("{這是參數名稱}")]
+    [InlineData("{1234567890}")]
+    public void Pseudo_UnicodeOrNumericPlaceholder_IsNotCounted(string text) => Assert.Equal("［" + text + "ǂ］", PseudoLocale.Transform(text));
+
+    /// <summary>新專案以零為基準，各專案分別比較，禁止另一專案改善抵銷新增。</summary>
+    [Fact(DisplayName = "新專案以零為基準")]
+    public void Ratchet_NewProjectAndOffsettingDecrease_StillFails()
+    {
+        Assert.False(CjkRatchet.Compare(new Dictionary<string, int> { ["New"] = 1 }, new Dictionary<string, int>()).Passed);
+        Assert.False(CjkRatchet.Compare(new Dictionary<string, int> { ["A"] = 0, ["B"] = 2 }, new Dictionary<string, int> { ["A"] = 2, ["B"] = 1 }).Passed);
+    }
+
+    /// <summary>英文空殼允許缺字；產出必須保留 null，偽語系依中性來源生成。</summary>
+    [Fact(DisplayName = "英文空殼允許缺字")]
+    public void Generate_EnglishShellAndEscaping_PreservesValues()
+    {
+        using var files = new ResourceFixture();
+        files.Write("", ("A.Text", "中文\"\\\r\n{name}"), ("B.Text", "第二筆"));
+        files.Write("en", ("A.Text", "English\"\\\r\n{name}"));
+        var text = Encoding.UTF8.GetString(ResourceGenerator.Generate(ResourceCatalog.Load(files.BasePath), "Sample.Resources", "SampleData"));
+        Assert.Contains("null", text, StringComparison.Ordinal);
+        Assert.Contains("\\\"", text, StringComparison.Ordinal);
+        Assert.Contains("\\\\", text, StringComparison.Ordinal);
+        Assert.Contains("\\r\\n", text, StringComparison.Ordinal);
+        Assert.Contains("internal static class SampleData", text, StringComparison.Ordinal);
+    }
+
+    /// <summary>參數集合忽略次數及出現順序；同一解析器也必須排除偽語系的參數長度。</summary>
+    [Fact(DisplayName = "參數集合忽略次數及出現順序")]
+    public void Resx_RepeatedAndReorderedParameters_ValidatesAsSet()
+    {
+        using var files = new ResourceFixture();
+        files.Write("", ("A.Text", "{first}{second}{first}"));
+        files.Write("en", ("A.Text", "{second}{first}"));
+        Assert.NotEmpty(ResourceGenerator.Generate(ResourceCatalog.Load(files.BasePath), "Sample", "Data"));
+        Assert.Equal("［{first}{second}{first}ǂ］", PseudoLocale.Transform("{first}{second}{first}"));
+    }
+
+    /// <summary>XML DTD 不得解析外部檔案，注入純假實體宣告即必須拒絕。</summary>
+    [Fact(DisplayName = "XML DTD 不得解析外部檔案")]
+    public void Resx_Dtd_IsRejectedWithoutResolution()
+    {
+        using var files = new ResourceFixture();
+        File.WriteAllText(files.BasePath, "<!DOCTYPE root [<!ENTITY x 'fake'>]><root><data name='A.Text'><value>&x;</value></data></root>");
+        Assert.Throws<XmlException>(() => ResourceCatalog.Load(files.BasePath));
+    }
+
+    /// <summary>翻譯資源的重複鍵與沒有中性來源的鍵也必須拒絕，不能由字典覆寫。</summary>
+    [Fact(DisplayName = "翻譯資源的重複鍵與沒有中性來源的鍵也必須拒絕")]
+    public void Resx_TranslationDuplicateOrUnknownKey_IsRejected()
+    {
+        using var files = new ResourceFixture();
+        files.Write("", ("A.Text", "中文"));
+        files.Write("en", ("A.Text", "One"), ("A.Text", "Two"));
+        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
+        files.Write("en", ("B.Other", "Other"));
+        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
+    }
+
+    /// <summary>實際 CLI 建立基準後通過；加入一筆文字即失敗，且輸出不含原始字串。</summary>
+    [Fact(DisplayName = "實際 CLI 建立基準後通過")]
+    public void Cli_ScanBaselineAndVerify_ProtectsProjectRatchet()
+    {
+        using var repo = new RepositoryFixture();
+        repo.AddProject("App", false);
+        repo.Write("App/Code.cs", "class C { string X = \"假機密中文\"; }");
+        using var output = new StringWriter();
+        Assert.Equal(0, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "tools/I18nCheck/baselines/cjk-literals.json"], output));
+        Assert.Equal(0, CommandRunner.Run(repo.Root, ["verify"], output));
+        repo.Write("App/Code.cs", "class C { string X = \"假機密中文\", Y = \"新增\"; }");
+        Assert.Equal(1, CommandRunner.Run(repo.Root, ["scan", "--baseline", "tools/I18nCheck/baselines/cjk-literals.json"], output));
+        Assert.DoesNotContain("假機密中文", output.ToString(), StringComparison.Ordinal);
+        Assert.DoesNotContain("假機密中文", File.ReadAllText(Path.Combine(repo.Root, "tools/I18nCheck/baselines/cjk-literals.json")), StringComparison.Ordinal);
+    }
+
+    /// <summary>只讀方案非測試專案；父 worktree、未列專案、生成檔與輸出目錄均不能增加數量。</summary>
+    [Fact(DisplayName = "只讀方案非測試專案")]
+    public void Cli_SolutionBoundary_ExcludesTestsGeneratedAndParentFiles()
+    {
+        using var repo = new RepositoryFixture();
+        repo.AddProject("App", false);
+        repo.AddProject("App.Tests", true);
+        repo.Write("App/Code.cs", "class C { string X = \"一\"; }");
+        foreach (var path in new[] { "App.Tests/Code.cs", "Outside/Code.cs", "App/obj/Code.cs", "App/bin/Code.cs", "App/.worktrees/Code.cs", "App/.其他解決方案/Code.cs", "App/Generated.g.cs" })
+            repo.Write(path, "class D { string X = \"不可掃到\"; }");
+        using var output = new StringWriter();
+        Assert.Equal(0, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "baseline.json"], output));
+        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo.Root, "baseline.json")));
+        var projects = json.RootElement.GetProperty("projects");
+        Assert.Single(projects.EnumerateArray());
+        Assert.Equal(1, projects[0].GetProperty("count").GetInt32());
+        var nested = Path.Combine(repo.Root, ".worktrees", "nested");
+        Directory.CreateDirectory(nested);
+        File.WriteAllText(Path.Combine(nested, "SpeechMessageProducts.sln"), "Microsoft Visual Studio Solution File, Format Version 12.00\r\n");
+        Assert.Equal(nested, RepositoryScanner.FindRoot(nested));
+        Assert.NotEqual(0, CommandRunner.Run(nested, ["scan", "--write-baseline", "baseline.json"], output));
+    }
+
+    /// <summary>Windows 副檔名大小寫不影響掃描，否則改成大寫即可繞過文字棘輪。</summary>
+    [Fact(DisplayName = "Windows 副檔名大小寫不影響掃描")]
+    public void Cli_UppercaseSourceExtensions_AreCounted()
+    {
+        using var repo = new RepositoryFixture();
+        repo.AddProject("App", false);
+        repo.Write("App/Upper.CS", "class C { string X = \"中文\"; }");
+        repo.Write("App/View.CSHTML", "<p>中文</p>");
+        using var output = new StringWriter();
+        Assert.Equal(0, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "baseline.json"], output));
+        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo.Root, "baseline.json")));
+        Assert.Equal(2, baseline.RootElement.GetProperty("projects")[0].GetProperty("count").GetInt32());
+    }
+
+    /// <summary>即使方案誤列排除目錄內的專案，也必須在讀取來源前拒絕，不能走訪子 worktree。</summary>
+    [Fact(DisplayName = "即使方案誤列排除目錄內的專案")]
+    public void Cli_ExcludedProjectDirectory_IsRejected()
+    {
+        using var repo = new RepositoryFixture();
+        repo.AddProject("App", false);
+        repo.Write(".worktrees/Hidden/Hidden.csproj", "<Project Sdk='Microsoft.NET.Sdk' />");
+        File.AppendAllText(Path.Combine(repo.Root, "SpeechMessageProducts.sln"), "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Hidden\", \".worktrees/Hidden/Hidden.csproj\", \"{00000000-0000-0000-0000-000000000001}\"\r\nEndProject\r\n");
+        using var output = new StringWriter();
+        Assert.NotEqual(0, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "baseline.json"], output));
+        Assert.False(File.Exists(Path.Combine(repo.Root, "baseline.json")));
+    }
+
+    /// <summary>產生、唯讀檢查、竄改後拒絕；檢查不能覆寫既有檔案，命令後可獨佔開檔。</summary>
+    [Fact(DisplayName = "產生、唯讀檢查、竄改後拒絕")]
+    public void Cli_GenerateCheck_IsReadOnlyAndClosesHandles()
+    {
+        using var repo = new RepositoryFixture();
+        repo.AddProject("App", false);
+        repo.Write("App/Resources/Text.resx", "<root><data name='A.Text'><value>中文</value></data></root>");
+        var args = new[] { "generate", "--resx", "App/Resources/Text.resx", "--namespace", "App.Resources", "--class", "TextData", "--out", "App/Resources/Text.g.cs" };
+        using var output = new StringWriter();
+        Assert.Equal(0, CommandRunner.Run(repo.Root, args, output));
+        Assert.Equal(0, CommandRunner.Run(repo.Root, ["generate", "--check"], output));
+        var path = Path.Combine(repo.Root, "App/Resources/Text.g.cs");
+        File.AppendAllText(path, "// stale\r\n");
+        var original = File.ReadAllBytes(path);
+        Assert.Equal(1, CommandRunner.Run(repo.Root, [.. args, "--check"], output));
+        Assert.Equal(original, File.ReadAllBytes(path));
+        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
+        Assert.True(stream.Length > 0);
+        Assert.Empty(Directory.GetFiles(repo.Root, "*.tmp", SearchOption.AllDirectories));
+    }
+
+    /// <summary>缺參數、未知命令、缺基準與非法輸出均必須非零結束，不能假稱驗證成功。</summary>
+    /// <param name="command">以分隔符號列出的獨立負向命令列。</param>
+    [Theory(DisplayName = "缺參數、未知命令、缺基準與非法輸出均必須非零結束")]
+    [InlineData("invalid")]
+    [InlineData("scan|--baseline|missing.json")]
+    [InlineData("generate")]
+    [InlineData("generate|--check|--unknown")]
+    [InlineData("scan|--write-baseline|../outside.json")]
+    public void Cli_InvalidArgumentsOrInputs_Fails(string command)
+    {
+        using var repo = new RepositoryFixture();
+        repo.AddProject("App", false);
+        using var output = new StringWriter();
+        Assert.NotEqual(0, CommandRunner.Run(repo.Root, command.Split('|'), output));
+    }
+
+    /// <summary>基準重複專案身分必須拒絕，不能由 JSON 字典覆寫繞過發布契約。</summary>
+    [Fact(DisplayName = "基準重複專案身分必須拒絕")]
+    public void Cli_DuplicateBaselineIdentity_FailsClosed()
+    {
+        using var repo = new RepositoryFixture();
+        repo.AddProject("App", false);
+        repo.Write("baseline.json", "{\"version\":1,\"projects\":[{\"project\":\"App/App.csproj\",\"count\":0,\"files\":[]},{\"project\":\"App/App.csproj\",\"count\":0,\"files\":[]}]}");
+        using var output = new StringWriter();
+        Assert.NotEqual(0, CommandRunner.Run(repo.Root, ["scan", "--baseline", "baseline.json"], output));
+    }
+
+    /// <summary>兩個並行掃描不得共享來源、計數或輸出，結束後所有目錄可被立即清理。</summary>
+    [Fact(DisplayName = "兩個並行掃描不得共享來源、計數或輸出")]
+    public async Task Cli_ParallelRepositories_DoNotShareState()
+    {
+        using var first = new RepositoryFixture();
+        using var second = new RepositoryFixture();
+        first.AddProject("First", false);
+        second.AddProject("Second", false);
+        first.Write("First/Code.cs", "class C { string X = \"一\"; }");
+        second.Write("Second/Code.cs", "class C { string X = \"一\", Y = \"二\"; }");
+        await Task.WhenAll(new[] { first, second }.Select(repo => Task.Run(() =>
+        {
+            using var output = new StringWriter();
+            Assert.Equal(0, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "baseline.json"], output));
+        })));
+        using var a = JsonDocument.Parse(File.ReadAllText(Path.Combine(first.Root, "baseline.json")));
+        using var b = JsonDocument.Parse(File.ReadAllText(Path.Combine(second.Root, "baseline.json")));
+        Assert.Equal(1, a.RootElement.GetProperty("projects")[0].GetProperty("count").GetInt32());
+        Assert.Equal(2, b.RootElement.GetProperty("projects")[0].GetProperty("count").GetInt32());
+    }
+}
+
+/// <summary>建立完全離線的假方案，根目錄由單一案例擁有；不讀取父 repo、帳密或產品設定。</summary>
+internal sealed class RepositoryFixture : IDisposable
+{
+    /// <summary>本案例專屬根，最長生命週期到 Dispose，沒有任何共享靜態來源清單。</summary>
+    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "i18n-repo-" + Guid.NewGuid().ToString("N"));
+
+    /// <summary>建立有效標頭，專案由 AddProject 明確列入，避免工具走訪未列目錄。</summary>
+    internal RepositoryFixture() => Write("SpeechMessageProducts.sln", "Microsoft Visual Studio Solution File, Format Version 12.00\r\n");
+
+    /// <summary>將假 SDK 專案加入方案；測試標記用於驗證正式掃描範圍。</summary>
+    /// <param name="name">本案例的簡單專案名稱。</param>
+    /// <param name="isTest">是否為應排除的測試專案。</param>
+    internal void AddProject(string name, bool isTest)
+    {
+        Write(name + "/" + name + ".csproj", "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>" + (isTest ? "true" : "false") + "</IsTestProject></PropertyGroup></Project>");
+        File.AppendAllText(Path.Combine(Root, "SpeechMessageProducts.sln"), "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"" + name + "\", \"" + name + "/" + name + ".csproj\", \"{" + Guid.NewGuid().ToString("D") + "}\"\r\nEndProject\r\n");
+    }
+
+    /// <summary>只在自己建立的目錄寫入假資料，每次寫入立即關閉檔案把手。</summary>
+    /// <param name="relative">受測案例固定提供的相對路徑。</param>
+    /// <param name="text">不含真實個資的假內容。</param>
+    internal void Write(string relative, string text)
+    {
+        var path = Path.Combine(Root, relative);
+        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
+        File.WriteAllText(path, text, new UTF8Encoding(false));
+    }
+
+    /// <summary>刪除本案例自建的根及所有假資料；沒有外部路徑輸入或保留背景工作。</summary>
+    public void Dispose() => Directory.Delete(Root, true);
+}

diff --git a/tools/I18nCheck.Tests/I18nCheck.Tests.csproj b/tools/I18nCheck.Tests/I18nCheck.Tests.csproj
new file mode 100644
index 000000000..edc92d6ee
--- /dev/null
+++ b/tools/I18nCheck.Tests/I18nCheck.Tests.csproj
@@ -0,0 +1,18 @@
+<Project Sdk="Microsoft.NET.Sdk">
+  <PropertyGroup>
+    <TargetFramework>net10.0</TargetFramework>
+    <ImplicitUsings>enable</ImplicitUsings>
+    <IsTestProject>true</IsTestProject>
+    <IsPackable>false</IsPackable>
+    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
+    <!-- 與工具同樣在本體開啟文件產出，測試文件也受嚴格模式保護。 -->
+    <GenerateDocumentationFile>true</GenerateDocumentationFile>
+  </PropertyGroup>
+  <ItemGroup>
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="xunit" />
+    <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
+    <PackageReference Include="coverlet.collector" PrivateAssets="all" />
+    <ProjectReference Include="../I18nCheck/I18nCheck.csproj" />
+  </ItemGroup>
+</Project>

diff --git a/tools/I18nCheck.Tests/I18nContractsTests.cs b/tools/I18nCheck.Tests/I18nContractsTests.cs
new file mode 100644
index 000000000..82e1f10fe
--- /dev/null
+++ b/tools/I18nCheck.Tests/I18nContractsTests.cs
@@ -0,0 +1,144 @@
+using System.Text;
+using System.Xml;
+using System.Xml.Linq;
+using Xunit;
+
+namespace I18nCheck.Tests;
+
+/// <summary>
+/// 保護 P0-07 原列十二個契約，以最小來源及假資源注入 CJK、忽略、棘輪與資源錯誤。
+/// 每個案例使用獨立暫存目錄，沒有共享可變狀態，斷言實際數量、錯誤或輸出位元組。
+/// </summary>
+public sealed class I18nContractsTests
+{
+    /// <summary>注入一個中文字串，移除字串辨識會使數量斷言失敗。</summary>
+    [Fact(DisplayName = "CJK 字串常值必須納入計數")]
+    public void Scan_LiteralWithCjk_IsCounted() => Assert.Equal(1, SourceScanner.CountCSharp("class C { string Text = \"中文\"; }"));
+
+    /// <summary>注入 ILogger 樣板及一個顯示常值，排除診斷時仍必須保留顯示常值。</summary>
+    [Fact(DisplayName = "ILogger 訊息樣板不增加顯示文字棘輪")]
+    public void Scan_LoggerTemplate_IsExcluded() => Assert.Equal(1, SourceScanner.CountCSharp("using Microsoft.Extensions.Logging; class C { void M(ILogger logger) { logger.LogInformation(\"診斷 {Id}\", 1); var text = \"顯示\"; } }"));
+
+    /// <summary>中文只出現在註解；若改為文字搜尋而非語法樹，零計數就會失敗。</summary>
+    [Fact(DisplayName = "中文註解不當成字串")]
+    public void Scan_CommentOnly_IsNotCounted() => Assert.Equal(0, SourceScanner.CountCSharp("// 中文註解\nclass C { }"));
+
+    /// <summary>對成員注入有理由的忽略標記，同類別未標記成員仍必須計數。</summary>
+    [Fact(DisplayName = "有理由的 I18nIgnore 僅排除其成員")]
+    public void Scan_I18nIgnoreAttribute_IsExcluded() => Assert.Equal(1, SourceScanner.CountCSharp("class C { [I18nIgnore(\"協定固定值\")] void M() { var x = \"略過\"; } string Name = \"保留\"; }"));
+
+    /// <summary>專案增加一筆常值即失敗，避免總數相抵讓跨專案新增通過。</summary>
+    [Fact(DisplayName = "專案中文字串數增加必須拒絕")]
+    public void Ratchet_CountIncreases_Fails() => Assert.False(CjkRatchet.Compare(new Dictionary<string, int> { ["A"] = 2 }, new Dictionary<string, int> { ["A"] = 1 }).Passed);
+
+    /// <summary>數量下降應通過並指出改善專案，不默默改寫基準。</summary>
+    [Fact(DisplayName = "減少中文字串通過並回報改善")]
+    public void Ratchet_CountDecreases_PassesAndReportsImprovement()
+    {
+        var result = CjkRatchet.Compare(new Dictionary<string, int> { ["A"] = 1 }, new Dictionary<string, int> { ["A"] = 2 });
+        Assert.True(result.Passed);
+        Assert.Contains("A", result.Improvements);
+    }
+
+    /// <summary>打亂資源列順序，兩次產出仍必須逐位元組相同，且鍵依 Ordinal 排序。</summary>
+    [Fact(DisplayName = "相同資源的產生結果逐位元組相同")]
+    public void Generate_SameInput_ByteIdenticalOutput()
+    {
+        using var files = new ResourceFixture();
+        files.Write("", ("Z.Last", "末筆"), ("A.First", "首筆"));
+        var first = ResourceGenerator.Generate(ResourceCatalog.Load(files.BasePath), "Sample.Resources", "SampleData");
+        files.Write("", ("A.First", "首筆"), ("Z.Last", "末筆"));
+        Assert.Equal(first, ResourceGenerator.Generate(ResourceCatalog.Load(files.BasePath), "Sample.Resources", "SampleData"));
+        var text = Encoding.UTF8.GetString(first);
+        Assert.True(text.IndexOf("A.First", StringComparison.Ordinal) < text.IndexOf("Z.Last", StringComparison.Ordinal));
+    }
+
+    /// <summary>包含多行資源時輸出仍為無 BOM UTF-8，所有檔案換行與結尾都固定 CRLF。</summary>
+    [Fact(DisplayName = "產出符合無 BOM UTF-8 與 CRLF")]
+    public void Generate_OutputIsUtf8NoBomCrlf()
+    {
+        using var files = new ResourceFixture();
+        files.Write("", ("A.Text", "中文\n第二行"));
+        var bytes = ResourceGenerator.Generate(ResourceCatalog.Load(files.BasePath), "Sample.Resources", "SampleData");
+        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
+        var text = new UTF8Encoding(false, true).GetString(bytes);
+        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
+        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
+        Assert.Contains("qps-ploc", text, StringComparison.Ordinal);
+    }
+
+    /// <summary>
+    /// 使用者核定七筆範例保護字數公式及參數完整性；每筆期望明列，沒有在測試重算實作公式。
+    /// 改用三個固定補字、移動補字位置或把參數算入長度，都會使決定性字串斷言失敗。
+    /// </summary>
+    /// <param name="input">中性資源字串。</param>
+    /// <param name="expected">使用者核定的完整偽語系輸出。</param>
+    [Theory(DisplayName = "偽語系保留參數並依核定公式補字")]
+    [InlineData("{name}，報名成功", "［{name}，報名成功ǂǂ］")]
+    [InlineData("課程報名", "［課程報名ǂǂ］")]
+    [InlineData("連線測試成功", "［連線測試成功ǂǂǂ］")]
+    [InlineData("小組", "［小組ǂ］")]
+    [InlineData("{name}", "［{name}ǂ］")]
+    [InlineData("目前語系：{culture}", "［目前語系：{culture}ǂǂ］")]
+    [InlineData("{churchName}小幫手", "［{churchName}小幫手ǂǂ］")]
+    public void Pseudo_WrapsAndExpands_KeepsPlaceholders(string input, string expected) => Assert.Equal(expected, PseudoLocale.Transform(input));
+
+    /// <summary>同一資源檔重複鍵必須失敗，禁止字典覆寫把衝突隱藏。</summary>
+    [Fact(DisplayName = "resx 重複鍵拒絕載入")]
+    public void Resx_DuplicateKey_Fails()
+    {
+        using var files = new ResourceFixture();
+        files.Write("", ("A.Text", "甲"), ("A.Text", "乙"));
+        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
+    }
+
+    /// <summary>翻譯把 name 改成 other 時拒絕，防止執行時遺失替換參數。</summary>
+    [Fact(DisplayName = "跨語系參數名稱集合必須一致")]
+    public void Resx_ParameterMismatchBetweenCultures_Fails()
+    {
+        using var files = new ResourceFixture();
+        files.Write("", ("A.Text", "你好 {name}"));
+        files.Write("en", ("A.Text", "Hello {other}"));
+        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
+    }
+
+    /// <summary>注入沒有分層的大寫鍵，必須因格式而失敗，不能產生不可追蹤資料。</summary>
+    [Fact(DisplayName = "resx 鍵必須符合分層格式")]
+    public void Resx_InvalidKeyFormat_Fails()
+    {
+        using var files = new ResourceFixture();
+        files.Write("", ("invalid", "中文"));
+        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
+    }
+}
+
+/// <summary>
+/// 每個案例獨佔一個隨機暫存目錄，僅保存假資源；Write 的串流立即關閉，Dispose 遞迴清理自己建立的目錄。
+/// 不使用真實 repo 設定、個資或服務連線，最長生命週期為單一測試，平行案例互不共享。
+/// </summary>
+internal sealed class ResourceFixture : IDisposable
+{
+    /// <summary>本案例唯一擁有的目錄；清理目標永遠由建構式建立，不接受外部路徑。</summary>
+    private readonly string directory = Path.Combine(Path.GetTempPath(), "i18n-tests-" + Guid.NewGuid().ToString("N"));
+
+    /// <summary>建立隔離目錄，之後只在此寫入測試 resx。</summary>
+    internal ResourceFixture() => Directory.CreateDirectory(directory);
+
+    /// <summary>中性資源固定路徑，衍生語系使用同一個名稱前綴。</summary>
+    internal string BasePath => Path.Combine(directory, "Sample.resx");
+
+    /// <summary>寫入假資源，保留呼叫者提供的順序與重複鍵供負向測試使用。</summary>
+    /// <param name="culture">空字串為中性，否則為語系後綴。</param>
+    /// <param name="rows">鍵與純假文字；不讀取實際產品機密。</param>
+    internal void Write(string culture, params (string Key, string Text)[] rows)
+    {
+        var root = new XElement("root", rows.Select(row => new XElement("data", new XAttribute("name", row.Key), new XElement("value", row.Text))));
+        // XML 會把原始 CRLF 正規化成 LF；用字元實體保留 CR，才能真正測到產生器的逸出契約。
+        using var writer = XmlWriter.Create(Path.Combine(directory, "Sample" + (culture.Length == 0 ? "" : "." + culture) + ".resx"),
+            new XmlWriterSettings { Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.Entitize, Indent = true });
+        root.Save(writer);
+    }
+
+    /// <summary>測試結束後確定釋放自己持有的全部檔案，跨案例不保留任何可變資料或把手。</summary>
+    public void Dispose() => Directory.Delete(directory, true);
+}

diff --git a/tools/I18nCheck.Tests/InputIntegrityTests.cs b/tools/I18nCheck.Tests/InputIntegrityTests.cs
new file mode 100644
index 000000000..d2ad6f78d
--- /dev/null
+++ b/tools/I18nCheck.Tests/InputIntegrityTests.cs
@@ -0,0 +1,88 @@
+using System.Text;
+using Microsoft.CodeAnalysis;
+using Microsoft.CodeAnalysis.CSharp;
+using Xunit;
+
+namespace I18nCheck.Tests;
+
+/// <summary>以假檔案保護產出可編譯性、舊編碼唯讀盤點與 XML／方案輸入邊界，不接觸外部服務。</summary>
+public sealed class InputIntegrityTests
+{
+    /// <summary>把包含引號、反斜線、多行及參數的真實產出交給 C# 編譯器，避免只比字串的假綠燈。</summary>
+    [Fact(DisplayName = "產生的 C# 必須可被編譯器接受")]
+    public void Generate_EscapedResourceRows_CompileWithoutErrors()
+    {
+        using var files = new ResourceFixture();
+        files.Write("", ("A.Text", "中文\"\\\r\n{name}"));
+        var bytes = ResourceGenerator.Generate(ResourceCatalog.Load(files.BasePath), "Sample.Resources", "SampleData");
+        var compilation = CSharpCompilation.Create("GeneratedProbe", [CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(bytes))],
+            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
+        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
+    }
+
+    /// <summary>專案 XML 的 DTD 即使只有內部假實體也必須拒絕，避免隱含實體展開或外部解析。</summary>
+    [Fact(DisplayName = "專案 XML 不允許 DTD")]
+    public void Cli_ProjectDtd_IsRejectedBeforeScan()
+    {
+        using var repo = new RepositoryFixture();
+        repo.AddProject("App", false);
+        repo.Write("App/App.csproj", "<!DOCTYPE Project [<!ENTITY sdk 'Microsoft.NET.Sdk'>]><Project Sdk='&sdk;' />");
+        using var output = new StringWriter();
+        Assert.Equal(2, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "baseline.json"], output));
+        Assert.False(File.Exists(Path.Combine(repo.Root, "baseline.json")));
+    }
+
+    /// <summary>方案的父路徑參照在開檔前失敗，不因父 worktree 剛好有同名專案而讀到舊來源。</summary>
+    [Fact(DisplayName = "方案不能參照 repo 以外的專案")]
+    public void Cli_ExternalProjectPath_IsRejected()
+    {
+        using var repo = new RepositoryFixture();
+        repo.AddProject("App", false);
+        File.AppendAllText(Path.Combine(repo.Root, "SpeechMessageProducts.sln"), "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"External\", \"../External.csproj\", \"{00000000-0000-0000-0000-000000000002}\"\r\nEndProject\r\n");
+        using var output = new StringWriter();
+        Assert.Equal(2, CommandRunner.Run(repo.Root, ["scan", "--write-baseline", "baseline.json"], output));
+    }
+
+    /// <summary>重複檔案身分不能靠字典覆寫或總數相抵隱藏；讀取基準時必須直接拒絕。</summary>
+    [Fact(DisplayName = "基準重複檔案身分必須拒絕")]
+    public void Cli_DuplicateBaselineFileIdentity_IsRejected()
+    {
+        using var repo = new RepositoryFixture();
+        repo.AddProject("App", false);
+        repo.Write("baseline.json", "{\"version\":1,\"projects\":[{\"project\":\"App/App.csproj\",\"count\":0,\"files\":[{\"path\":\"App/Code.cs\",\"count\":0},{\"path\":\"App/Code.cs\",\"count\":0}]}]}");
+        using var output = new StringWriter();
+        Assert.Equal(2, CommandRunner.Run(repo.Root, ["scan", "--baseline", "baseline.json"], output));
+    }
+
+    /// <summary>CP950 是既有來源盤點的只讀後備，解碼後必須保留常值且來源位元組完全不變。</summary>
+    [Fact(DisplayName = "舊 CP950 來源唯讀盤點且不改寫")]
+    public void Scan_Cp950Source_DecodesWithoutMutation()
+    {
+        using var repo = new RepositoryFixture();
+        var path = Path.Combine(repo.Root, "Legacy.cs");
+        const string source = "class C { string X = \"中文測試\"; }";
+        var bytes = CodePagesEncodingProvider.Instance.GetEncoding(950)!.GetBytes(source);
+        File.WriteAllBytes(path, bytes);
+        Assert.Equal(source, LocalFiles.ReadSource(path));
+        Assert.Equal(1, SourceScanner.CountCSharp(LocalFiles.ReadSource(path)));
+        Assert.Equal(bytes, File.ReadAllBytes(path));
+    }
+
+    /// <summary>鍵必須完整符合格式，不能利用 Regex 的行尾語意讓最後一個換行穿過驗證。</summary>
+    /// <param name="key">帶換行、空段、空白或非允許字元的鍵。</param>
+    [Theory(DisplayName = "resx 鍵格式必須完整匹配整個名稱")]
+    [InlineData("A.Text\n")]
+    [InlineData("A.Text\r")]
+    [InlineData("A..Text")]
+    [InlineData(".A.Text")]
+    [InlineData("A.")]
+    [InlineData("a.Text")]
+    [InlineData("A.文字")]
+    [InlineData("A.X Y")]
+    public void Resx_InvalidKeyBoundaries_AreRejected(string key)
+    {
+        using var files = new ResourceFixture();
+        files.Write("", (key, "假資料"));
+        Assert.Throws<InvalidDataException>(() => ResourceCatalog.Load(files.BasePath));
+    }
+}

diff --git a/tools/I18nCheck/CommandRunner.cs b/tools/I18nCheck/CommandRunner.cs
new file mode 100644
index 000000000..59d0fde55
--- /dev/null
+++ b/tools/I18nCheck/CommandRunner.cs
@@ -0,0 +1,190 @@
+using System.Text;
+using System.Text.Json;
+using System.Xml;
+using Microsoft.CodeAnalysis.CSharp;
+using Microsoft.CodeAnalysis.CSharp.Syntax;
+
+namespace I18nCheck;
+
+/// <summary>
+/// 協調單一離線命令；來源只在命令區域持有，輸出只有計數、路徑及固定錯誤代碼。
+/// 不建構服務 client、不載入產品設定，不使用背景佇列、使用者快取或程序文化狀態。
+/// </summary>
+internal static class CommandRunner
+{
+    /// <summary>固定基準位置，與 verify／CI 使用同一份已審核計數。</summary>
+    private const string DefaultBaseline = "tools/I18nCheck/baselines/cjk-literals.json";
+
+    /// <summary>只含固定格式的共用序列化設定，首次使用後由 System.Text.Json 凍結，無使用者資料。</summary>
+    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
+
+    /// <summary>執行且封閉處理可預期的輸入錯誤，不將任何來源文字或資料值寫入診斷。</summary>
+    /// <param name="root">明確選定的方案根，不在缺檔時向父 worktree 尋找。</param>
+    /// <param name="args">scan、generate、verify 及其選項。</param>
+    /// <param name="output">呼叫者擁有的輸出；本工具不保留也不釋放。</param>
+    /// <returns>成功為零，棘輪／產生檔差異為一，輸入／檔案驗證錯誤為二。</returns>
+    internal static int Run(string root, string[] args, TextWriter output)
+    {
+        try
+        {
+            if (args.Length == 0) throw new InvalidDataException("I18N401: command is required.");
+            root = Path.GetFullPath(root);
+            return args[0] switch
+            {
+                "scan" => Scan(root, Parse(args, ["--baseline", "--write-baseline"]), output),
+                "generate" => Generate(root, Parse(args, ["--resx", "--namespace", "--class", "--out", "--check"]), output),
+                "verify" when args.Length == 1 => Verify(root, output),
+                _ => throw new InvalidDataException("I18N402: command or options are invalid.")
+            };
+        }
+        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or XmlException or JsonException)
+        {
+            // 這些是檔案／命令的預期拒絕，不發外部告警；不輸出含來源值的 Exception.Message。
+            output.WriteLine("I18nCheck rejected input: " + exception.GetType().Name);
+            return 2;
+        }
+    }
+
+    /// <summary>嚴格解析本命令允許的選項，拒絕拼錯、重複或缺值，不默默忽略輸入。</summary>
+    /// <param name="args">原命令列。</param>
+    /// <param name="allowed">此指令的固定白名單。</param>
+    /// <returns>命令區域的選項字典；check 是唯一無值旗標。</returns>
+    private static Dictionary<string, string> Parse(string[] args, string[] allowed)
+    {
+        var options = new Dictionary<string, string>(StringComparer.Ordinal);
+        for (var index = 1; index < args.Length; index++)
+        {
+            var name = args[index];
+            if (!allowed.Contains(name, StringComparer.Ordinal) || options.ContainsKey(name)) throw new InvalidDataException("I18N403: unknown or repeated option.");
+            if (name == "--check") { options.Add(name, string.Empty); continue; }
+            if (++index == args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) throw new InvalidDataException("I18N404: option value is missing.");
+            options.Add(name, args[index]);
+        }
+        return options;
+    }
+
+    /// <summary>完整掃描後才寫基準或比較；每個專案獨立判定，不使用全方案總數相抵。</summary>
+    /// <param name="root">本次根目錄。</param>
+    /// <param name="options">已驗證的選項。</param>
+    /// <param name="output">只接收中繼資料的輸出。</param>
+    /// <returns>基準建立或棘輪判定。</returns>
+    private static int Scan(string root, Dictionary<string, string> options, TextWriter output)
+    {
+        if (options.ContainsKey("--baseline") && options.ContainsKey("--write-baseline")) throw new InvalidDataException("I18N405: baseline modes are mutually exclusive.");
+        var snapshot = RepositoryScanner.Scan(root);
+        Validate(snapshot);
+        foreach (var project in snapshot.Projects)
+        {
+            output.WriteLine($"PROJECT {project.Project}: {project.Count}");
+            foreach (var file in project.Files) output.WriteLine($"  FILE {file.Path}: {file.Count}");
+        }
+        if (options.TryGetValue("--write-baseline", out var destination))
+        {
+            var path = LocalFiles.Resolve(root, destination);
+            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("I18N406: baseline output must be JSON.");
+            var json = JsonSerializer.Serialize(snapshot, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
+            LocalFiles.WriteAtomic(path, new UTF8Encoding(false, true).GetBytes(json));
+            return 0;
+        }
+        var baselinePath = LocalFiles.Resolve(root, options.GetValueOrDefault("--baseline", DefaultBaseline));
+        LocalFiles.CheckFile(baselinePath);
+        var baseline = JsonSerializer.Deserialize<ScanSnapshot>(File.ReadAllBytes(baselinePath), JsonOptions) ?? throw new InvalidDataException("I18N407: baseline is empty.");
+        Validate(baseline);
+        var result = CjkRatchet.Compare(snapshot.Projects.ToDictionary(project => project.Project, project => project.Count, StringComparer.OrdinalIgnoreCase), baseline.Projects.ToDictionary(project => project.Project, project => project.Count, StringComparer.OrdinalIgnoreCase));
+        foreach (var project in result.Improvements) output.WriteLine("IMPROVEMENT " + project);
+        output.WriteLine(result.Passed ? "CJK ratchet: passed" : "CJK ratchet: count increased");
+        return result.Passed ? 0 : 1;
+    }
+
+    /// <summary>拒絕重複／缺失身分及不一致總數，避免 JSON 或字典覆寫掩蓋基準衝突。</summary>
+    /// <param name="snapshot">完整掃描結果或讀回的基準。</param>
+    private static void Validate(ScanSnapshot snapshot)
+    {
+        if (snapshot.Version != 1 || snapshot.Projects is null || snapshot.Projects.Count == 0) throw new InvalidDataException("I18N408: baseline schema is invalid.");
+        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
+        foreach (var project in snapshot.Projects)
+        {
+            if (project is null || string.IsNullOrWhiteSpace(project.Project) || !projects.Add(project.Project) || project.Count < 0 || project.Files is null)
+                throw new InvalidDataException("I18N409: baseline project identity is invalid or duplicated.");
+            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
+            foreach (var file in project.Files)
+                if (file is null || string.IsNullOrWhiteSpace(file.Path) || file.Count < 0 || !files.Add(file.Path)) throw new InvalidDataException("I18N410: baseline file identity is invalid or duplicated.");
+            if (project.Files.Sum(file => (long)file.Count) != project.Count) throw new InvalidDataException("I18N411: baseline total differs from file counts.");
+        }
+    }
+
+    /// <summary>可明確產生一個檔，或在 check 模式巡檢方案內全部中性資源；check 永不寫檔。</summary>
+    /// <param name="root">選定根目錄。</param>
+    /// <param name="options">已驗證的產生器選項。</param>
+    /// <param name="output">只輸出檔名與判定。</param>
+    /// <returns>所有產生檔均一致時為零，否則一。</returns>
+    private static int Generate(string root, Dictionary<string, string> options, TextWriter output)
+    {
+        if (options.Count == 1 && options.ContainsKey("--check")) return CheckAllGenerated(root, output);
+        foreach (var name in new[] { "--resx", "--namespace", "--class", "--out" })
+            if (!options.ContainsKey(name)) throw new InvalidDataException("I18N412: explicit generation requires resx, namespace, class and out.");
+        var source = LocalFiles.Resolve(root, options["--resx"]);
+        var destination = LocalFiles.Resolve(root, options["--out"]);
+        if (!source.EndsWith(".resx", StringComparison.OrdinalIgnoreCase) || !destination.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("I18N413: generation file extensions are invalid.");
+        var bytes = ResourceGenerator.Generate(ResourceCatalog.Load(source), options["--namespace"], options["--class"]);
+        if (options.ContainsKey("--check")) return CompareGenerated(destination, bytes, output);
+        LocalFiles.WriteAtomic(destination, bytes);
+        output.WriteLine("GENERATED " + RepositoryScanner.Relative(root, destination));
+        return 0;
+    }
+
+    /// <summary>只檢查中性 resx；語系檔由 ResourceCatalog 一次驗證，不重複當成新來源。</summary>
+    /// <param name="root">固定方案邊界。</param>
+    /// <param name="output">判定輸出。</param>
+    /// <returns>任一輸出缺失或差異時為一。</returns>
+    private static int CheckAllGenerated(string root, TextWriter output)
+    {
+        var passed = true;
+        var count = 0;
+        foreach (var project in RepositoryScanner.ProjectPaths(root))
+        foreach (var source in RepositoryScanner.Files(root, Path.GetDirectoryName(project)!).Where(path => path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase)))
+        {
+            var stem = Path.GetFileNameWithoutExtension(source);
+            var split = stem.LastIndexOf('.');
+            if (split > 0 && File.Exists(Path.Combine(Path.GetDirectoryName(source)!, stem[..split] + ".resx"))) continue;
+            count++;
+            var destination = LocalFiles.Resolve(root, Path.ChangeExtension(source, ".g.cs"));
+            var catalog = ResourceCatalog.Load(source);
+            if (!File.Exists(destination)) { output.WriteLine("GENERATED MISSING " + RepositoryScanner.Relative(root, destination)); passed = false; continue; }
+            var syntax = CSharpSyntaxTree.ParseText(LocalFiles.ReadSource(destination)).GetRoot();
+            var namespaceName = syntax.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().SingleOrDefault()?.Name.ToString();
+            var className = syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().SingleOrDefault()?.Identifier.ValueText;
+            if (namespaceName is null || className is null) throw new InvalidDataException("I18N414: generated file namespace or class is missing.");
+            if (CompareGenerated(destination, ResourceGenerator.Generate(catalog, namespaceName, className), output) != 0) passed = false;
+        }
+        output.WriteLine($"Generated resource check: files={count}, passed={passed}");
+        return passed ? 0 : 1;
+    }
+
+    /// <summary>逐位元組比較候選，不修正差異，也不在診斷印出資源文字。</summary>
+    /// <param name="destination">生成檔路徑。</param>
+    /// <param name="expected">本次重新產生的候選。</param>
+    /// <param name="output">只記錄一致或不同。</param>
+    /// <returns>相等零，缺檔或不同一。</returns>
+    private static int CompareGenerated(string destination, byte[] expected, TextWriter output)
+    {
+        if (File.Exists(destination))
+        {
+            LocalFiles.CheckFile(destination);
+            if (File.ReadAllBytes(destination).AsSpan().SequenceEqual(expected)) return 0;
+        }
+        output.WriteLine("Generated resource check: missing or stale output");
+        return 1;
+    }
+
+    /// <summary>每包及 CI 的一致入口，固定執行專案棘輪、resx 契約與產生檔比較。</summary>
+    /// <param name="root">本次根。</param>
+    /// <param name="output">呼叫者輸出。</param>
+    /// <returns>所有檢查通過才回傳零。</returns>
+    private static int Verify(string root, TextWriter output)
+    {
+        var scan = Scan(root, new Dictionary<string, string>(StringComparer.Ordinal), output);
+        var generated = CheckAllGenerated(root, output);
+        return scan == 0 && generated == 0 ? 0 : 1;
+    }
+}

diff --git a/tools/I18nCheck/Contracts.cs b/tools/I18nCheck/Contracts.cs
new file mode 100644
index 000000000..fc3b73d1c
--- /dev/null
+++ b/tools/I18nCheck/Contracts.cs
@@ -0,0 +1,24 @@
+namespace I18nCheck;
+
+/// <summary>以各專案的基準計數限制新增硬編碼文字，減少時提供明確改善清單。</summary>
+internal static class CjkRatchet
+{
+    /// <summary>新專案基準為零；比較只建立命令區域結果，不修改任何來源集合。</summary>
+    /// <param name="current">本次掃描的各專案計數。</param>
+    /// <param name="baseline">已審核的各專案基準。</param>
+    /// <returns>判定與減少的專案名稱。</returns>
+    internal static RatchetResult Compare(IReadOnlyDictionary<string, int> current, IReadOnlyDictionary<string, int> baseline)
+    {
+        ArgumentNullException.ThrowIfNull(current);
+        ArgumentNullException.ThrowIfNull(baseline);
+        var passed = current.All(pair => pair.Value <= baseline.GetValueOrDefault(pair.Key));
+        var improvements = baseline.Where(pair => current.GetValueOrDefault(pair.Key) < pair.Value)
+            .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
+        return new RatchetResult(passed, Array.AsReadOnly(improvements));
+    }
+}
+
+/// <summary>棘輪的單次不可變判定，名稱清單由產生者獨立持有，沒有教會或使用者資料。</summary>
+/// <param name="Passed">是否沒有任何專案增加計數。</param>
+/// <param name="Improvements">文字數下降的專案名稱。</param>
+internal sealed record RatchetResult(bool Passed, IReadOnlyList<string> Improvements);

diff --git a/tools/I18nCheck/I18nCheck.csproj b/tools/I18nCheck/I18nCheck.csproj
new file mode 100644
index 000000000..0e1b2272c
--- /dev/null
+++ b/tools/I18nCheck/I18nCheck.csproj
@@ -0,0 +1,16 @@
+<Project Sdk="Microsoft.NET.Sdk">
+  <PropertyGroup>
+    <OutputType>Exe</OutputType>
+    <TargetFramework>net10.0</TargetFramework>
+    <ImplicitUsings>enable</ImplicitUsings>
+    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
+    <!-- 文件開關必須在 SDK 決定 DocumentationFile 之前啟用，避免嚴格模式假綠燈。 -->
+    <GenerateDocumentationFile>true</GenerateDocumentationFile>
+  </PropertyGroup>
+  <ItemGroup>
+    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
+    <!-- 用真實 ILogger 符號辨識訊息樣板，避免僅按 logger 名稱造成假排除；版本沿用中央表。 -->
+    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
+    <InternalsVisibleTo Include="I18nCheck.Tests" />
+  </ItemGroup>
+</Project>

diff --git a/tools/I18nCheck/LocalFiles.cs b/tools/I18nCheck/LocalFiles.cs
new file mode 100644
index 000000000..cea9b385c
--- /dev/null
+++ b/tools/I18nCheck/LocalFiles.cs
@@ -0,0 +1,91 @@
+using System.Text;
+using System.Xml;
+using System.Xml.Linq;
+
+namespace I18nCheck;
+
+/// <summary>
+/// 管理單一命令的本機檔案邊界與所有權；禁止外部路徑、重新解析點及過大輸入。
+/// 寫入只持有同目錄的唯一暫存檔，成功原子替換，失敗 finally 清除，不保留串流或背景工作。
+/// </summary>
+internal static class LocalFiles
+{
+    /// <summary>單一來源最大十六 MiB，限制惡意或誤選的大檔造成無界記憶體保留。</summary>
+    internal const int MaxFileBytes = 16 * 1024 * 1024;
+
+    /// <summary>正規化相對路徑後確認仍在明確選定的 repo 內，不接受父 worktree 檔案。</summary>
+    /// <param name="root">本命令固定的方案根目錄。</param>
+    /// <param name="path">相對或絕對本機路徑。</param>
+    /// <returns>已確認所有既有父目錄均非重新解析點的絕對路徑。</returns>
+    internal static string Resolve(string root, string path)
+    {
+        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
+        var full = Path.GetFullPath(path, fullRoot);
+        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
+            throw new InvalidDataException("I18N201: path leaves the selected repository.");
+        for (var cursor = full; cursor.Length >= fullRoot.Length; cursor = Path.GetDirectoryName(cursor)!)
+        {
+            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
+                throw new InvalidDataException("I18N202: reparse points are not allowed.");
+            if (cursor.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)) break;
+        }
+        return full;
+    }
+
+    /// <summary>在配置記憶體前拒絕過大檔與重新解析點，不在錯誤訊息輸出來源內容。</summary>
+    /// <param name="path">已選定的檔案。</param>
+    internal static void CheckFile(string path)
+    {
+        var info = new FileInfo(path);
+        if (!info.Exists) throw new FileNotFoundException("I18N203: required input file is missing.");
+        if (info.Length > MaxFileBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0)
+            throw new InvalidDataException("I18N204: input size or file boundary is invalid.");
+    }
+
+    /// <summary>讀取既有來源；UTF-8 失敗才以嚴格 CP950 盤點，不改寫舊來源編碼。</summary>
+    /// <param name="path">受方案邊界約束的來源檔。</param>
+    /// <returns>只供本次語法分析的文字。</returns>
+    internal static string ReadSource(string path)
+    {
+        CheckFile(path);
+        var bytes = File.ReadAllBytes(path);
+        try
+        {
+            var offset = bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) ? 3 : 0;
+            return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
+        }
+        catch (DecoderFallbackException)
+        {
+            return CodePagesEncodingProvider.Instance.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!.GetString(bytes);
+        }
+    }
+
+    /// <summary>共用受限 XML 讀取器，禁止 DTD 與外部解析；讀取失敗也會釋放全部 reader。</summary>
+    /// <param name="path">已由呼叫者確認本機邊界的 XML 檔案。</param>
+    /// <returns>由呼叫者持有、不再引用串流的 XML 文件。</returns>
+    internal static XDocument ReadXml(string path)
+    {
+        using var text = new StringReader(ReadSource(path));
+        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxFileBytes });
+        return XDocument.Load(reader);
+    }
+
+    /// <summary>以唯一本命令暫存檔進行原子替換；所有寫入把手在移動前已關閉。</summary>
+    /// <param name="path">由呼叫者 Resolve 過的目的地。</param>
+    /// <param name="bytes">完整候選內容。</param>
+    internal static void WriteAtomic(string path, byte[] bytes)
+    {
+        var directory = Path.GetDirectoryName(path)!;
+        Directory.CreateDirectory(directory);
+        var temporary = Path.Combine(directory, ".i18n-" + Guid.NewGuid().ToString("N") + ".tmp");
+        try
+        {
+            File.WriteAllBytes(temporary, bytes);
+            File.Move(temporary, path, true);
+        }
+        finally
+        {
+            if (File.Exists(temporary)) File.Delete(temporary);
+        }
+    }
+}

diff --git a/tools/I18nCheck/Program.cs b/tools/I18nCheck/Program.cs
new file mode 100644
index 000000000..e36f91b62
--- /dev/null
+++ b/tools/I18nCheck/Program.cs
@@ -0,0 +1,10 @@
+namespace I18nCheck;
+
+/// <summary>只操作本機來源與產生檔的命令列入口，不建立 D365、LINE 或背景工作連線。</summary>
+internal static class Program
+{
+    /// <summary>在單一命令生命週期內完成掃描或產生，回傳可供 CI 判定的結束碼。</summary>
+    /// <param name="args">scan、generate 或 verify 與其選項。</param>
+    /// <returns>成功為零，驗證或輸入失敗為非零。</returns>
+    private static int Main(string[] args) => CommandRunner.Run(RepositoryScanner.FindRoot(Environment.CurrentDirectory), args, Console.Out);
+}

diff --git a/tools/I18nCheck/PseudoLocale.cs b/tools/I18nCheck/PseudoLocale.cs
new file mode 100644
index 000000000..3f55b672a
--- /dev/null
+++ b/tools/I18nCheck/PseudoLocale.cs
@@ -0,0 +1,45 @@
+using System.Collections.Frozen;
+using System.Text;
+using System.Text.RegularExpressions;
+
+namespace I18nCheck;
+
+/// <summary>
+/// 唯一的具名參數解析器；偽語系計數與 resx 集合驗證共用完全相同的辨識規則。
+/// 靜態正則只保存固定文法，無回溯、無來源快取，不保留任何使用者或教會資料。
+/// </summary>
+internal static class Placeholders
+{
+    /// <summary>辨識一組大括號內的非空名稱，不把參數任意限縮為 ASCII；巢狀括號不合併。</summary>
+    private static readonly Regex Pattern = new(@"\{(?<name>[^{}]+)\}", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
+
+    /// <summary>僅移除識別成功的整個參數，保留標點、空白與其他 Unicode 字元。</summary>
+    /// <param name="text">本次資源文字，不存入任何靜態欄位。</param>
+    /// <returns>可按 Rune 計數的非參數文字。</returns>
+    internal static string Remove(string text) => Pattern.Replace(text, string.Empty);
+
+    /// <summary>產生 Ordinal 唯讀集合，重複參數及順序不影響跨語系契約。</summary>
+    /// <param name="text">本次資源文字。</param>
+    /// <returns>只包含參數名稱的不可變集合。</returns>
+    internal static FrozenSet<string> Names(string text) => Pattern.Matches(text).Select(match => match.Groups["name"].Value).ToFrozenSet(StringComparer.Ordinal);
+}
+
+/// <summary>依 2026-11-03 使用者核定公式擴展文字，只在整段外包圍一次，不修改參數。</summary>
+internal static class PseudoLocale
+{
+    /// <summary>以 Rune 計算非參數字數，再用整數 ceil(2n/5) 補字；不使用程序文化或時鐘。</summary>
+    /// <param name="text">完整中性資源，包含必須原樣保留的參數。</param>
+    /// <returns>原文後接至少一個 ǂ，最後才加入右全形方括號。</returns>
+    internal static string Transform(string text)
+    {
+        ArgumentNullException.ThrowIfNull(text);
+        var count = 0;
+        foreach (var rune in Placeholders.Remove(text).EnumerateRunes())
+        {
+            _ = rune;
+            count++;
+        }
+        var padding = (int)Math.Max(1, ((2L * count) + 4) / 5);
+        return "［" + text + new string('ǂ', padding) + "］";
+    }
+}

diff --git a/tools/I18nCheck/RazorScanner.cs b/tools/I18nCheck/RazorScanner.cs
new file mode 100644
index 000000000..15e871ca4
--- /dev/null
+++ b/tools/I18nCheck/RazorScanner.cs
@@ -0,0 +1,94 @@
+using System.Net;
+using System.Text;
+using System.Text.RegularExpressions;
+
+namespace I18nCheck;
+
+/// <summary>
+/// 離線詞法掃描 Razor 的 HTML 文字節點及四種顯示屬性；不建置、執行或解譯產品模板。
+/// 固定正則無回溯，來源與候選字串只在單次掃描持有，沒有靜態內容快取。
+/// </summary>
+internal static class RazorScanner
+{
+    /// <summary>HTML／Razor 註解與 script／style 內容均不屬於此階段定義的 HTML 文字節點。</summary>
+    private static readonly Regex Hidden = new(@"<!--[\s\S]*?-->|@\*[\s\S]*?\*@|<script\b[^>]*>[\s\S]*?</script\s*>|<style\b[^>]*>[\s\S]*?</style\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
+
+    /// <summary>標籤掃描保留引號內的大於符號，避免把屬性誤拆成文字。</summary>
+    private static readonly Regex Tags = new("<(?:\"[^\"]*\"|'[^']*'|[^'\">])*>", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
+
+    /// <summary>僅計畫指定四種屬性，可使用單引號、雙引號或未加引號的值。</summary>
+    private static readonly Regex Attributes = new("(?:^|\\s)(?:title|placeholder|alt|value)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
+
+    /// <summary>先去除非顯示區與 Razor 運算式，再按 HTML 節點邊界計算，不輸出來源片段。</summary>
+    /// <param name="source">大小已受檔案層限制的 Razor 文字。</param>
+    /// <returns>含 CJK 的文字節點與指定屬性個數。</returns>
+    internal static int Count(string source)
+    {
+        var html = StripExpressions(Hidden.Replace(source, string.Empty));
+        var previous = 0;
+        var count = 0;
+        foreach (Match tag in Tags.Matches(html))
+        {
+            if (SourceScanner.ContainsCjk(WebUtility.HtmlDecode(html[previous..tag.Index]))) count++;
+            count += Attributes.Matches(tag.Value).Count(attribute => SourceScanner.ContainsCjk(WebUtility.HtmlDecode(attribute.Groups["value"].Value)));
+            previous = tag.Index + tag.Length;
+        }
+        if (SourceScanner.ContainsCjk(WebUtility.HtmlDecode(html[previous..]))) count++;
+        return count;
+    }
+
+    /// <summary>移除 @ 運算式及純程式區塊；控制流程只跳過條件，保留其內實際 HTML。</summary>
+    /// <param name="source">已移除註解與隱藏元素的來源。</param>
+    /// <returns>僅供此呼叫計數的 HTML 候選。</returns>
+    private static string StripExpressions(string source)
+    {
+        var result = new StringBuilder(source.Length);
+        for (var index = 0; index < source.Length;)
+        {
+            if (source[index] != '@') { result.Append(source[index++]); continue; }
+            index++;
+            if (index == source.Length) break;
+            if (source[index] == '@') { result.Append('@'); index++; continue; }
+            if (source[index] is '(' or '{') { index = SkipBalanced(source, index); continue; }
+            var start = index;
+            while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '.')) index++;
+            var identifier = source[start..index];
+            if (identifier.Length == 0) continue;
+            while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
+            if (index < source.Length && source[index] == '(') index = SkipBalanced(source, index);
+            if (identifier is "functions" or "code")
+            {
+                while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
+                if (index < source.Length && source[index] == '{') index = SkipBalanced(source, index);
+            }
+        }
+        return result.ToString();
+    }
+
+    /// <summary>有界線性略過一組括號，字串中的括號與逸出字元不影響深度。</summary>
+    /// <param name="source">本次 Razor 文字。</param>
+    /// <param name="start">左括號位置。</param>
+    /// <returns>匹配右括號後的位置；不完整輸入則略過至檔尾。</returns>
+    private static int SkipBalanced(string source, int start)
+    {
+        var open = source[start];
+        var close = open == '(' ? ')' : '}';
+        var depth = 0;
+        var quote = '\0';
+        for (var index = start; index < source.Length; index++)
+        {
+            var character = source[index];
+            if (quote != '\0')
+            {
+                if (character == '\\') index++;
+                else if (character == quote) quote = '\0';
+                continue;
+            }
+            if (character is '\'' or '"') { quote = character; continue; }
+            if (character == open) depth++;
+            if (character == close && --depth == 0) return index + 1;
+        }
+        return source.Length;
+    }
+
+}

diff --git a/tools/I18nCheck/RepositoryScanner.cs b/tools/I18nCheck/RepositoryScanner.cs
new file mode 100644
index 000000000..6f431f60f
--- /dev/null
+++ b/tools/I18nCheck/RepositoryScanner.cs
@@ -0,0 +1,135 @@
+using System.Text.RegularExpressions;
+using Microsoft.CodeAnalysis;
+using Microsoft.CodeAnalysis.CSharp;
+
+namespace I18nCheck;
+
+/// <summary>單檔計數中繼資料，不包含原始常值；相對路徑是唯一的檔案身分。</summary>
+/// <param name="Path">相對 repo 路徑，固定使用斜線。</param>
+/// <param name="Count">該檔案的 CJK 顯示片段數。</param>
+internal sealed record ScanFile(string Path, int Count);
+
+/// <summary>單一專案的完整候選結果；掃描成功後才發布，不把半成品交給序列化器。</summary>
+/// <param name="Project">相對 csproj 路徑，作為跨掃描身分。</param>
+/// <param name="Count">此專案所有檔案的總數。</param>
+/// <param name="Files">單次命令建立且不向外部修改的唯讀檔案清單。</param>
+internal sealed record ScanProject(string Project, int Count, IReadOnlyList<ScanFile> Files);
+
+/// <summary>版本化的中文字串基準格式；只保存計數與相對路徑，不保存來源或機密。</summary>
+/// <param name="Version">本版格式固定為一。</param>
+/// <param name="Projects">各專案的完整獨立結果，發布前檢查身分唯一。</param>
+internal sealed record ScanSnapshot(int Version, IReadOnlyList<ScanProject> Projects);
+
+/// <summary>
+/// 以最近方案及其明列專案為唯一來源邊界；不進入父 worktree、未列專案、生成輸出或重新解析點。
+/// 每次依專案建立並釋放語法／語意候選，單專案原文總量上限六十四 MiB，不保留到下一命令。
+/// </summary>
+internal static class RepositoryScanner
+{
+    /// <summary>只辨識方案明列的 C# 專案，不把 solution folder 或任意目錄當成來源。</summary>
+    private static readonly Regex ProjectPattern = new("^Project\\(\"[^\"]+\"\\)\\s*=\\s*\"[^\"]*\",\\s*\"(?<path>[^\"]+\\.csproj)\"", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
+
+    /// <summary>所有目錄邊界與前包一致；先剪枝才走訪，避免讀入被排除的內容。</summary>
+    private static readonly HashSet<string> ExcludedDirectories = new(["bin", "obj", ".git", ".worktrees", ".其他解決方案", "docs", ".ccg", ".trellis", "node_modules", "artifacts", "TestResults"], StringComparer.OrdinalIgnoreCase);
+
+    /// <summary>只向上定位最近方案標記；來源走訪之後固定在回傳的根之下。</summary>
+    /// <param name="start">執行位置。</param>
+    /// <returns>最近的 SpeechMessageProducts.sln 根；找不到直接失敗。</returns>
+    internal static string FindRoot(string start)
+    {
+        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
+            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
+        throw new InvalidDataException("I18N301: SpeechMessageProducts.sln was not found.");
+    }
+
+    /// <summary>完整掃描後才回傳候選，單次結果不含任何語法樹、來源字串或開啟檔案。</summary>
+    /// <param name="root">明確選定、不可退回父目錄的方案根。</param>
+    /// <returns>固定排序、身分唯一的計數快照。</returns>
+    internal static ScanSnapshot Scan(string root)
+    {
+        var projects = new List<ScanProject>();
+        foreach (var project in ProjectPaths(root))
+        {
+            var files = Files(root, Path.GetDirectoryName(project)!).ToArray();
+            if (files.Sum(path => new FileInfo(path).Length) > 64L * 1024 * 1024)
+                throw new InvalidDataException("I18N302: project source size exceeds the bounded scan limit.");
+            var trees = new List<SyntaxTree>();
+            var counts = new List<ScanFile>();
+            foreach (var path in files)
+            {
+                if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
+                    trees.Add(CSharpSyntaxTree.ParseText(LocalFiles.ReadSource(path), path: path));
+                else if (path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
+                    counts.Add(new ScanFile(Relative(root, path), SourceScanner.CountRazor(LocalFiles.ReadSource(path))));
+            }
+            var compilation = SourceScanner.CreateCompilation(trees);
+            foreach (var tree in trees)
+                counts.Add(new ScanFile(Relative(root, tree.FilePath), SourceScanner.CountCSharp(tree, compilation.GetSemanticModel(tree))));
+            projects.Add(new ScanProject(Relative(root, project), counts.Sum(file => file.Count), counts.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray()));
+        }
+        return new ScanSnapshot(1, projects.OrderBy(project => project.Project, StringComparer.Ordinal).ToArray());
+    }
+
+    /// <summary>列出明確非測試專案，拒絕重複、外部路徑與空方案，避免假綠燈。</summary>
+    /// <param name="root">本命令選定的根。</param>
+    /// <returns>不含外部來源的固定專案清單。</returns>
+    internal static IReadOnlyList<string> ProjectPaths(string root)
+    {
+        var solution = LocalFiles.ReadSource(LocalFiles.Resolve(root, "SpeechMessageProducts.sln"));
+        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
+        var projects = new List<string>();
+        foreach (Match match in ProjectPattern.Matches(solution))
+        {
+            var path = LocalFiles.Resolve(root, match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar));
+            if (Relative(root, path).Split('/').SkipLast(1).Any(ExcludedDirectories.Contains))
+                throw new InvalidDataException("I18N307: solution project is inside an excluded directory.");
+            if (!seen.Add(path)) throw new InvalidDataException("I18N303: duplicate solution project identity.");
+            if (seen.Count > 256) throw new InvalidDataException("I18N304: solution project count exceeds the scan limit.");
+            var document = LocalFiles.ReadXml(path);
+            var test = Path.GetFileNameWithoutExtension(path).EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
+                || document.Descendants().Any(element => element.Name.LocalName == "IsTestProject" && element.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
+                || document.Descendants().Any(element => element.Name.LocalName == "PackageReference" && (string?)element.Attribute("Include") == "Microsoft.NET.Test.Sdk");
+            if (!test) projects.Add(path);
+        }
+        if (projects.Count == 0) throw new InvalidDataException("I18N305: solution contains no non-test C# project.");
+        return projects.Order(StringComparer.Ordinal).ToArray();
+    }
+
+    /// <summary>在專案根內剪枝走訪，檔數及目錄數各限兩萬，不穿越重新解析點。</summary>
+    /// <param name="root">方案邊界。</param>
+    /// <param name="projectDirectory">已通過邊界檢查的專案目錄。</param>
+    /// <returns>只有 C#、Razor、resx 的固定來源清單。</returns>
+    internal static IEnumerable<string> Files(string root, string projectDirectory)
+    {
+        var directories = new Stack<string>();
+        directories.Push(projectDirectory);
+        var count = 0;
+        while (directories.Count > 0)
+        {
+            var directory = directories.Pop();
+            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
+            {
+                if (++count > 20_000) throw new InvalidDataException("I18N306: project entry count exceeds the scan limit.");
+                var attributes = File.GetAttributes(entry);
+                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
+                if ((attributes & FileAttributes.Directory) != 0)
+                {
+                    if (!ExcludedDirectories.Contains(Path.GetFileName(entry))) directories.Push(entry);
+                    continue;
+                }
+                var extension = Path.GetExtension(entry);
+                if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
+                    && !extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase)
+                    && !extension.Equals(".resx", StringComparison.OrdinalIgnoreCase)) continue;
+                if (entry.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || entry.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)) continue;
+                yield return LocalFiles.Resolve(root, entry);
+            }
+        }
+    }
+
+    /// <summary>跨機器一致的相對路徑格式，不把機器名稱或絕對目錄寫入基準。</summary>
+    /// <param name="root">方案根。</param>
+    /// <param name="path">根內來源。</param>
+    /// <returns>斜線分隔的穩定來源身分。</returns>
+    internal static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
+}

diff --git a/tools/I18nCheck/ResourceCatalog.cs b/tools/I18nCheck/ResourceCatalog.cs
new file mode 100644
index 000000000..9cc8a43ce
--- /dev/null
+++ b/tools/I18nCheck/ResourceCatalog.cs
@@ -0,0 +1,84 @@
+using System.Collections.Frozen;
+using System.Globalization;
+using System.Text.RegularExpressions;
+
+namespace I18nCheck;
+
+/// <summary>
+/// 驗證後的 resx 快照；載入期間以區域字典組裝，完整成功後才發布不可變集合。
+/// XML reader 在成功與失敗路徑均立即 Dispose，禁止 DTD 與外部解析，大小限制為十六 MiB。
+/// </summary>
+internal sealed class ResourceCatalog
+{
+    /// <summary>鍵必須分層且各段以大寫開頭；使用完整字串邊界，避免 $ 允許尾端換行。</summary>
+    private static readonly Regex KeyPattern = new(@"\A[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)+\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
+
+    /// <summary>固定基礎語系順序，不隨載入次數重複配置，也不向外部公開可變陣列。</summary>
+    private static readonly string[] BaseCultures = ["zh-TW", "en"];
+
+    /// <summary>每個語系的唯讀鍵值，只有靜態資源，不含執行中的使用者、教會或憑證。</summary>
+    internal FrozenDictionary<string, FrozenDictionary<string, string>> Values { get; }
+
+    /// <summary>產出語系的固定順序：zh-TW、en、其他語系 Ordinal 排序、qps-ploc。</summary>
+    internal IReadOnlyList<string> Cultures { get; }
+
+    /// <summary>只接受已驗證的區域候選，將集合凍結後才交給產生器。</summary>
+    /// <param name="values">本次載入建立的全部語系。</param>
+    private ResourceCatalog(Dictionary<string, FrozenDictionary<string, string>> values)
+    {
+        Values = values.ToFrozenDictionary(StringComparer.Ordinal);
+        Cultures = Array.AsReadOnly(BaseCultures.Concat(values.Keys.Where(key => key is not "zh-TW" and not "en").Order(StringComparer.Ordinal)).Append("qps-ploc").ToArray());
+    }
+
+    /// <summary>以中性 resx 為 zh-TW，載入同名的其他語系；重複鍵及參數差異直接拒絕。</summary>
+    /// <param name="path">中性資源的本機路徑。</param>
+    /// <returns>沒有開啟串流或可變來源參考的資源快照。</returns>
+    internal static ResourceCatalog Load(string path)
+    {
+        var neutral = Read(path);
+        var values = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal) { ["zh-TW"] = neutral };
+        var fullPath = Path.GetFullPath(path);
+        var directory = Path.GetDirectoryName(fullPath)!;
+        var prefix = Path.GetFileNameWithoutExtension(fullPath) + ".";
+        foreach (var translationPath in Directory.EnumerateFiles(directory, "*.resx").Order(StringComparer.Ordinal))
+        {
+            var stem = Path.GetFileNameWithoutExtension(translationPath);
+            if (!stem.StartsWith(prefix, StringComparison.Ordinal)) continue;
+            var culture = stem[prefix.Length..];
+            _ = CultureInfo.GetCultureInfo(culture);
+            if (culture is "zh-TW" or "qps-ploc" || values.ContainsKey(culture))
+                throw new InvalidDataException("I18N101: reserved or duplicate resource culture.");
+            var translated = Read(translationPath);
+            foreach (var pair in translated)
+            {
+                if (!neutral.TryGetValue(pair.Key, out var original))
+                    throw new InvalidDataException("I18N102: translation key has no neutral source.");
+                if (!Placeholders.Names(original).SetEquals(Placeholders.Names(pair.Value)))
+                    throw new InvalidDataException("I18N103: resource placeholder name sets differ.");
+            }
+            values.Add(culture, translated);
+        }
+        values.TryAdd("en", FrozenDictionary<string, string>.Empty);
+        return new ResourceCatalog(values);
+    }
+
+    /// <summary>只讀字串 data/value，不反序列化 resx 型別；碰到不明內容時封閉失敗。</summary>
+    /// <param name="path">單一資源檔案。</param>
+    /// <returns>檢查唯一性及格式後的不可變鍵值。</returns>
+    private static FrozenDictionary<string, string> Read(string path)
+    {
+        var document = LocalFiles.ReadXml(path);
+        if (document.Root?.Name != "root") throw new InvalidDataException("I18N104: resource root is invalid.");
+        var result = new Dictionary<string, string>(StringComparer.Ordinal);
+        foreach (var data in document.Root.Elements("data"))
+        {
+            var key = (string?)data.Attribute("name");
+            var value = data.Element("value");
+            if (key is null || !KeyPattern.IsMatch(key)) throw new InvalidDataException("I18N105: resource key format is invalid.");
+            if (value is null || data.Attribute("type") is not null || data.Attribute("mimetype") is not null)
+                throw new InvalidDataException("I18N106: resource must contain a plain string value.");
+            if (!result.TryAdd(key, value.Value)) throw new InvalidDataException("I18N107: duplicate resource key.");
+        }
+        return result.ToFrozenDictionary(StringComparer.Ordinal);
+    }
+}

diff --git a/tools/I18nCheck/ResourceGenerator.cs b/tools/I18nCheck/ResourceGenerator.cs
new file mode 100644
index 000000000..28108ff88
--- /dev/null
+++ b/tools/I18nCheck/ResourceGenerator.cs
@@ -0,0 +1,48 @@
+using System.Text;
+using Microsoft.CodeAnalysis.CSharp;
+
+namespace I18nCheck;
+
+/// <summary>依附錄 E.2 產生不依賴衛星組件的資料表，輸出固定排序、無時間戳記且逐位元組可重現。</summary>
+internal static class ResourceGenerator
+{
+    /// <summary>只從已驗證資源生成；字串由 Roslyn 逸出，不允許來源文字插入可執行程式碼。</summary>
+    /// <param name="catalog">命令區域唯讀資源快照。</param>
+    /// <param name="namespaceName">每段都必須是合法且非保留字的命名空間。</param>
+    /// <param name="className">產生的內部型別名稱。</param>
+    /// <returns>UTF-8 無 BOM、只使用 CRLF 且以 CRLF 結尾的位元組。</returns>
+    internal static byte[] Generate(ResourceCatalog catalog, string namespaceName, string className)
+    {
+        ArgumentNullException.ThrowIfNull(catalog);
+        if (string.IsNullOrWhiteSpace(namespaceName) || !namespaceName.Split('.').All(IsIdentifier) || !IsIdentifier(className))
+            throw new InvalidDataException("I18N108: generated namespace or class name is invalid.");
+        var text = new StringBuilder();
+        text.Append("// <auto-generated>由 tools/I18nCheck 從 resx 產生，請勿手動修改。</auto-generated>\r\n#nullable enable\r\nnamespace ").Append(namespaceName).Append("\r\n{\r\n");
+        text.Append("    /// <summary>固定系統文字資料；僅保存編譯時資源，不含教會覆寫或使用者狀態。</summary>\r\n    internal static class ").Append(className).Append("\r\n    {\r\n");
+        text.Append("        /// <summary>列資料的語系順序，消費端必須建立自己的唯讀表，不向呼叫者公開此陣列。</summary>\r\n        internal static readonly string[] Cultures = { ");
+        text.AppendJoin(", ", catalog.Cultures.Select(Literal)).Append(" };\r\n");
+        text.Append("        /// <summary>每列依序為鍵及各語系文字；缺翻譯為 null，鍵依 Ordinal 排序。</summary>\r\n        internal static readonly string?[][] Rows =\r\n        {\r\n");
+        foreach (var pair in catalog.Values["zh-TW"].OrderBy(pair => pair.Key, StringComparer.Ordinal))
+        {
+            var row = new List<string> { Literal(pair.Key) };
+            foreach (var culture in catalog.Cultures)
+            {
+                row.Add(culture == "qps-ploc" ? Literal(PseudoLocale.Transform(pair.Value)) :
+                    catalog.Values[culture].TryGetValue(pair.Key, out var value) ? Literal(value) : "null");
+            }
+            text.Append("            new string?[] { ").AppendJoin(", ", row).Append(" },\r\n");
+        }
+        text.Append("        };\r\n    }\r\n}\r\n");
+        return new UTF8Encoding(false, true).GetBytes(text.ToString());
+    }
+
+    /// <summary>拒絕命名空間／類別中的注入與 C# 保留字，不以字串拼接接受任意程式碼。</summary>
+    /// <param name="value">單一識別字。</param>
+    /// <returns>只有合法且非保留字時為真。</returns>
+    private static bool IsIdentifier(string value) => SyntaxFacts.IsValidIdentifier(value) && SyntaxFacts.GetKeywordKind(value) == SyntaxKind.None;
+
+    /// <summary>由編譯器統一處理引號、反斜線及換行，避免手動逸出漂移。</summary>
+    /// <param name="value">原始資源值。</param>
+    /// <returns>合法 C# 字串常值文字。</returns>
+    private static string Literal(string value) => SyntaxFactory.Literal(value).ToFullString();
+}

diff --git a/tools/I18nCheck/SourceScanner.cs b/tools/I18nCheck/SourceScanner.cs
new file mode 100644
index 000000000..961a81189
--- /dev/null
+++ b/tools/I18nCheck/SourceScanner.cs
@@ -0,0 +1,96 @@
+using System.Collections.Immutable;
+using Microsoft.CodeAnalysis;
+using Microsoft.CodeAnalysis.CSharp;
+using Microsoft.CodeAnalysis.CSharp.Syntax;
+using Microsoft.CodeAnalysis.Operations;
+using Microsoft.Extensions.Logging;
+
+namespace I18nCheck;
+
+/// <summary>
+/// 用 Roslyn 辨識常值與插值文字，僅在符號可證明時排除診斷呼叫；解析不完整時保守計入。
+/// 語法樹與語意模型由單次掃描持有，完成後不放入靜態快取，避免原始文字無界保留。
+/// </summary>
+internal static class SourceScanner
+{
+    /// <summary>
+    /// 固定框架中繼資料只建立一次，範圍限本工具程序的可信平台清單與 ILogger 組件。
+    /// 不包含來源、request 或教會資訊；數量受程序相依清單限制，程序結束即由執行環境回收。
+    /// </summary>
+    private static readonly ImmutableArray<MetadataReference> References =
+        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? typeof(object).Assembly.Location)
+        .Split(Path.PathSeparator).Append(typeof(ILogger).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
+        .Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();
+
+    /// <summary>建立單檔語法與語意分析；註解從來不是字串常值，因此不會進入計數。</summary>
+    /// <param name="source">只在本次呼叫保存的來源。</param>
+    /// <returns>未被明確排除的 CJK 字串數。</returns>
+    internal static int CountCSharp(string source)
+    {
+        var tree = CSharpSyntaxTree.ParseText(source);
+        var compilation = CreateCompilation([tree]);
+        return CountCSharp(tree, compilation.GetSemanticModel(tree));
+    }
+
+    /// <summary>以同專案所有來源建立一份語意模型集合，避免每檔重建重複的中繼資料。</summary>
+    /// <param name="trees">本專案固定候選語法樹。</param>
+    /// <returns>只在本專案掃描期間持有來源的編譯描述，不輸出或執行程式。</returns>
+    internal static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees) => CSharpCompilation.Create("I18nInventory", trees, References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
+
+    /// <summary>每個常值或整個插值字串各計一次；插值運算式中的獨立常值另行計數。</summary>
+    /// <param name="tree">單一來源語法樹。</param>
+    /// <param name="model">同專案的語意模型，用於證明 ILogger／Trace／Debug 邊界。</param>
+    /// <returns>顯示文字常值數量。</returns>
+    internal static int CountCSharp(SyntaxTree tree, SemanticModel model)
+    {
+        var count = 0;
+        foreach (var node in tree.GetRoot().DescendantNodes())
+        {
+            var hasCjk = node switch
+            {
+                LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) || literal.IsKind(SyntaxKind.Utf8StringLiteralExpression) => ContainsCjk(literal.Token.ValueText),
+                InterpolatedStringExpressionSyntax interpolation => interpolation.Contents.OfType<InterpolatedStringTextSyntax>().Any(text => ContainsCjk(text.TextToken.ValueText)),
+                _ => false
+            };
+            if (hasCjk && !IsIgnoredMember(node) && !IsDiagnosticArgument(node, model)) count++;
+        }
+        return count;
+    }
+
+    /// <summary>Razor 只盤點 HTML 顯示文字，不啟動網頁或執行任何 Razor 程式。</summary>
+    /// <param name="source">單一 Razor 來源。</param>
+    /// <returns>文字節點與指定屬性的 CJK 片段數。</returns>
+    internal static int CountRazor(string source) => RazorScanner.Count(source);
+
+    /// <summary>只包含計畫指定的 BMP CJK 區間，英文、日文假名與 emoji 不誤算。</summary>
+    /// <param name="value">語法常值或 HTML 解碼後的片段。</param>
+    /// <returns>含任一 U+3400–U+9FFF 或 U+F900–U+FAFF 字元時為真。</returns>
+    internal static bool ContainsCjk(string value) => value.Any(character => character is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff');
+
+    /// <summary>標記只對其所在成員與子節點生效，必須有非空固定理由；不按註解猜測忽略。</summary>
+    /// <param name="node">待判定常值。</param>
+    /// <returns>外層成員存在有效 I18nIgnore 標記時為真。</returns>
+    private static bool IsIgnoredMember(SyntaxNode node) => node.Ancestors().OfType<MemberDeclarationSyntax>()
+        .SelectMany(member => member.AttributeLists).SelectMany(list => list.Attributes)
+        .Any(attribute => attribute.Name.ToString().Split('.').Last() is "I18nIgnore" or "I18nIgnoreAttribute"
+            && attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax reason
+            && reason.IsKind(SyntaxKind.StringLiteralExpression) && !string.IsNullOrWhiteSpace(reason.Token.ValueText));
+
+    /// <summary>僅排除真正診斷符號；未知型別或同名方法保守保留，ILogger 的其他資料引數仍計入。</summary>
+    /// <param name="node">候選字串，可能位於插值或其他運算式內。</param>
+    /// <param name="model">單次專案的語意模型。</param>
+    /// <returns>在 Trace／Debug 引數或 ILogger message／state 樣板內時為真。</returns>
+    private static bool IsDiagnosticArgument(SyntaxNode node, SemanticModel model)
+    {
+        foreach (var argument in node.Ancestors().OfType<ArgumentSyntax>())
+        {
+            if (argument.Parent?.Parent is not InvocationExpressionSyntax invocation || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method) continue;
+            var type = method.ContainingType.ToDisplayString();
+            if (type is "System.Diagnostics.Trace" or "System.Diagnostics.Debug") return true;
+            if (method.ContainingNamespace.ToDisplayString() != "Microsoft.Extensions.Logging") continue;
+            if (method.ContainingType.Name is not "LoggerExtensions" and not "ILogger") continue;
+            if (model.GetOperation(argument) is IArgumentOperation operation && operation.Parameter?.Name is "message" or "state") return true;
+        }
+        return false;
+    }
+}

diff --git a/tools/PluginPackaging.Tests/AssemblyInspectionTests.cs b/tools/PluginPackaging.Tests/AssemblyInspectionTests.cs
new file mode 100644
index 000000000..befeaa5fb
--- /dev/null
+++ b/tools/PluginPackaging.Tests/AssemblyInspectionTests.cs
@@ -0,0 +1,117 @@
+using System;
+using System.Globalization;
+using System.IO;
+using System.Linq;
+using System.Security.Cryptography;
+using System.Text.Json;
+using Microsoft.CodeAnalysis;
+using Microsoft.CodeAnalysis.CSharp;
+using SpeechMessage.Tools.PluginPackaging;
+using Xunit;
+
+namespace SpeechMessage.Tools.PluginPackaging.Tests;
+
+/// <summary>
+/// 使用 Roslyn 及一次性測試金鑰編譯惰性 PE，驗證真實 MetadataReader 與確定性關檔。
+/// 樣本不載入執行，不讀正式金鑰或外部服務；各案例的輸出、金鑰與資料夾完全隔離。
+/// </summary>
+public sealed class AssemblyInspectionTests
+{
+    /// <summary>逐項注入錯誤框架、未簽章、未公開型別及禁用相依，正樣本必須通過全部檢查。</summary>
+    /// <param name="fault">固定故障代碼，不接受外部輸入。</param>
+    /// <param name="expected">預期接受或拒絕。</param>
+    [Theory(DisplayName = "檢查器拒絕不符合沙箱契約的中繼資料")]
+    [InlineData("valid", true)]
+    [InlineData("unsigned", false)]
+    [InlineData("framework", false)]
+    [InlineData("no-framework", false)]
+    [InlineData("private", false)]
+    [InlineData("reference", false)]
+    [InlineData("drawing", false)]
+    public void Inspect_MetadataFaults_ReturnsContractResult(string fault, bool expected)
+    {
+        using var fixture = new Fixture();
+        var path = fixture.Emit(fault);
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+        Assert.Equal(expected ? 0 : 1, Program.Run(new[] { path }, output));
+        using var result = JsonDocument.Parse(output.ToString());
+        Assert.Equal(expected, result.RootElement.GetProperty("ok").GetBoolean());
+        // 不等程序結束或 GC：返回後立即以獨佔讀寫開啟，證明 PE reader／stream 已確定性釋放。
+        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
+        Assert.True(exclusive.Length > 0);
+    }
+
+    /// <summary>損毀、過大、缺檔與缺參數皆 fail closed，輸出固定原因且不能包含原始本機路徑。</summary>
+    /// <param name="fault">只供案例選擇的錯誤種類。</param>
+    /// <param name="reason">固定安全拒絕原因。</param>
+    [Theory(DisplayName = "不完整輸入以固定原因拒絕且不洩漏路徑")]
+    [InlineData("arguments", "InvalidArguments")]
+    [InlineData("missing", "InvalidImage")]
+    [InlineData("invalid", "InvalidImage")]
+    [InlineData("oversize", "SizeLimit")]
+    public void Inspect_InvalidInput_FailsClosedWithoutPath(string fault, string reason)
+    {
+        using var fixture = new Fixture();
+        var path = Path.Combine(fixture.Root, "synthetic-sensitive-path.dll");
+        if (fault == "invalid") File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
+        if (fault == "oversize")
+        {
+            using var stream = File.Create(path);
+            stream.SetLength(16 * 1024 * 1024);
+        }
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+        var args = fault == "arguments" ? Array.Empty<string>() : new[] { path };
+        Assert.Equal(1, Program.Run(args, output));
+        using var result = JsonDocument.Parse(output.ToString());
+        Assert.Equal(reason, result.RootElement.GetProperty("reason").GetString());
+        Assert.DoesNotContain(fixture.Root, output.ToString(), StringComparison.Ordinal);
+    }
+
+    /// <summary>獨佔暫存目錄及測試金鑰的唯一 owner；只產生惰性 DLL，Dispose 刪除所有測試資源。</summary>
+    private sealed class Fixture : IDisposable
+    {
+        /// <summary>以隨機識別切分測試邊界，不從待測檔或外部資料推算清理目標。</summary>
+        public string Root { get; } = Path.Combine(Path.GetTempPath(), "speechmessage-inspection-" + Guid.NewGuid().ToString("N"));
+        /// <summary>僅在此 fixture 有效的測試金鑰路徑。</summary>
+        private readonly string key;
+        /// <summary>一次性產生測試 RSA key，匯出後立即釋放 CSP，不碰正式環境變數。</summary>
+        public Fixture()
+        {
+            Directory.CreateDirectory(Root);
+            key = Path.Combine(Root, "test.snk");
+            using var rsa = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false };
+            File.WriteAllBytes(key, rsa.ExportCspBlob(true));
+        }
+        /// <summary>以 net48 參考組件離線編譯指定故障；編譯失敗是測試錯誤，不當作檢查器成功拒絕。</summary>
+        /// <param name="fault">固定測試故障種類。</param>
+        /// <returns>尚未被載入的測試 DLL 路徑。</returns>
+        public string Emit(string fault)
+        {
+            var reference = MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "Framework", "mscorlib.dll"));
+            var framework = fault == "no-framework" ? "" :
+                "[assembly: System.Runtime.Versioning.TargetFramework(\"" + (fault == "framework" ? ".NETCoreApp,Version=v10.0" : ".NETFramework,Version=v4.8") + "\")]";
+            var source = framework + "namespace SpeechMessage.Dynamics.Plugins.Diagnostics { " +
+                (fault == "private" ? "internal" : "public") + " sealed class DiagnosticsPingPlugin { } }";
+            var references = new[] { reference }.AsEnumerable();
+            if (fault is "reference" or "drawing")
+            {
+                var name = fault == "reference" ? "UnexpectedDependency" : "System.Drawing";
+                var dependency = Path.Combine(Root, name + ".dll");
+                var dependencyCompilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText("public class RequiredType { }") },
+                    references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
+                Assert.True(dependencyCompilation.Emit(dependency).Success);
+                references = references.Append(MetadataReference.CreateFromFile(dependency));
+                source += "public class DependencyOwner : RequiredType { }";
+            }
+            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
+            if (fault != "unsigned") options = options.WithCryptoKeyFile(key).WithStrongNameProvider(new DesktopStrongNameProvider());
+            var compilation = CSharpCompilation.Create("InspectionFixture", new[] { CSharpSyntaxTree.ParseText(source) }, references, options);
+            var path = Path.Combine(Root, "fixture.dll");
+            var result = compilation.Emit(path);
+            Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(value => value.ToString())));
+            return path;
+        }
+        /// <summary>Roslyn 已完成並釋放檔案後，在已宣告的獨佔根目錄內刪除一次性 key 與 PE。</summary>
+        public void Dispose() => Directory.Delete(Root, recursive: true);
+    }
+}

diff --git a/tools/PluginPackaging.Tests/PluginPackaging.Tests.csproj b/tools/PluginPackaging.Tests/PluginPackaging.Tests.csproj
new file mode 100644
index 000000000..69699f2c7
--- /dev/null
+++ b/tools/PluginPackaging.Tests/PluginPackaging.Tests.csproj
@@ -0,0 +1,21 @@
+<Project Sdk="Microsoft.NET.Sdk">
+  <PropertyGroup>
+    <TargetFramework>net10.0</TargetFramework>
+    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
+    <GenerateDocumentationFile>true</GenerateDocumentationFile>
+    <IsTestProject>true</IsTestProject>
+    <IsPackable>false</IsPackable>
+  </PropertyGroup>
+  <ItemGroup>
+    <ProjectReference Include="../PluginPackaging/PluginPackaging.csproj" />
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
+    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
+    <PackageReference Include="xunit" />
+    <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
+    <PackageReference Include="coverlet.collector" PrivateAssets="all" />
+    <!-- 只複製中央固定 1.0.3 套件的 net48 編譯參考，產生不執行的 PE；不參考正式 Plugin 或正式金鑰。 -->
+    <Content Include="$(NuGetPackageRoot)microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8/mscorlib.dll"
+             Link="Framework/mscorlib.dll" CopyToOutputDirectory="PreserveNewest" />
+  </ItemGroup>
+</Project>

diff --git a/tools/PluginPackaging/PluginPackaging.csproj b/tools/PluginPackaging/PluginPackaging.csproj
new file mode 100644
index 000000000..3d82dc79e
--- /dev/null
+++ b/tools/PluginPackaging/PluginPackaging.csproj
@@ -0,0 +1,11 @@
+<Project Sdk="Microsoft.NET.Sdk">
+  <PropertyGroup>
+    <OutputType>Exe</OutputType>
+    <TargetFramework>net10.0</TargetFramework>
+    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
+    <GenerateDocumentationFile>true</GenerateDocumentationFile>
+  </PropertyGroup>
+  <ItemGroup>
+    <InternalsVisibleTo Include="PluginPackaging.Tests" />
+  </ItemGroup>
+</Project>

diff --git a/tools/PluginPackaging/Program.cs b/tools/PluginPackaging/Program.cs
new file mode 100644
index 000000000..594e70fd2
--- /dev/null
+++ b/tools/PluginPackaging/Program.cs
@@ -0,0 +1,111 @@
+using System;
+using System.IO;
+using System.Linq;
+using System.Reflection;
+using System.Reflection.Metadata;
+using System.Reflection.PortableExecutable;
+using System.Text.Json;
+
+namespace SpeechMessage.Tools.PluginPackaging;
+
+/// <summary>
+/// 由 PowerShell 啟動的離線中繼資料檢查器；只讀 PE，不載入或執行受檢 DLL。
+/// 每次程序只處理指定的檔案，串流與讀取器由同一操作確定性釋放，不保留到 static。
+/// </summary>
+internal static class Program
+{
+    /// <summary>輸出固定 JSON 檢查結果；未完成檢查時不得以結束碼零假稱通過。</summary>
+    /// <param name="args">受檢 DLL 的本機路徑，不含憑證或連線資訊。</param>
+    /// <returns>零表示所有契約通過，一表示驗證拒絕。</returns>
+    public static int Main(string[] args)
+        => Run(args, Console.Out);
+
+    /// <summary>由呼叫者擁有輸出 writer，讓並行離線驗證不必改全域 Console 或共享診斷緩衝。</summary>
+    /// <param name="args">單一本機 DLL 路徑。</param>
+    /// <param name="output">僅存活於此次操作的輸出目的地，本工具不負責關閉。</param>
+    /// <returns>所有中繼資料契約通過才回傳零。</returns>
+    internal static int Run(string[] args, TextWriter output)
+    {
+        if (args.Length != 1) return Reject("InvalidArguments", output);
+        try
+        {
+            // 每個受檢檔案最多十六 MiB；先以檔案長度拒絕超界，再建立唯讀 PE reader。
+            using var stream = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read);
+            if (stream.Length >= 16 * 1024 * 1024) return Reject("SizeLimit", output);
+            using var pe = new PEReader(stream);
+            if (!pe.HasMetadata) return Reject("InvalidImage", output);
+            var metadata = pe.GetMetadataReader();
+            if (!metadata.IsAssembly) return Reject("InvalidImage", output);
+            var assembly = metadata.GetAssemblyDefinition();
+            var publicKey = metadata.GetBlobBytes(assembly.PublicKey);
+            var identity = new AssemblyName("InspectionOnly");
+            identity.SetPublicKey(publicKey);
+            var strongName = publicKey.Length > 0 && identity.GetPublicKeyToken() is { Length: > 0 } &&
+                (pe.PEHeaders.CorHeader!.Flags & CorFlags.StrongNameSigned) != 0;
+            var targetFramework = ReadFramework(metadata, assembly);
+            var references = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray();
+            var allowed = references.All(name => name is "mscorlib" or "System" or "Microsoft.Xrm.Sdk" or "Microsoft.Crm.Sdk.Proxy" ||
+                name.StartsWith("System.", StringComparison.Ordinal));
+            var noDrawing = references.All(name => !string.Equals(name, "System.Drawing", StringComparison.Ordinal) &&
+                !name.StartsWith("System.Drawing.", StringComparison.Ordinal));
+            var plugin = metadata.TypeDefinitions.Any(handle =>
+            {
+                var type = metadata.GetTypeDefinition(handle);
+                return (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public &&
+                    string.Equals(metadata.GetString(type.Namespace), "SpeechMessage.Dynamics.Plugins.Diagnostics", StringComparison.Ordinal) &&
+                    string.Equals(metadata.GetString(type.Name), "DiagnosticsPingPlugin", StringComparison.Ordinal);
+            });
+            var ok = strongName && targetFramework && allowed && noDrawing && plugin;
+            output.WriteLine(JsonSerializer.Serialize(new
+            {
+                ok,
+                checks = new { readable = true, strongName, targetFramework, allowedReferences = allowed, noDrawing, diagnosticsPlugin = plugin, size = true },
+                assemblyVersion = assembly.Version.ToString(),
+                references,
+                sizeBytes = stream.Length
+            }));
+            return ok ? 0 : 1;
+        }
+        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException or ArgumentException)
+        {
+            // 損壞影像、不可讀檔與不合法中繼資料都是預期輸入拒絕；不輸出原始路徑或例外訊息。
+            return Reject("InvalidImage", output);
+        }
+    }
+
+    /// <summary>只解析 assembly 的框架屬性 blob，不反射載入待測型別或執行屬性建構式。</summary>
+    /// <param name="metadata">當次 PE reader 擁有的唯讀中繼資料。</param>
+    /// <param name="assembly">已確認為組件的定義。</param>
+    /// <returns>只有明確 net48 TargetFrameworkAttribute 才通過。</returns>
+    private static bool ReadFramework(MetadataReader metadata, AssemblyDefinition assembly)
+    {
+        foreach (var handle in assembly.GetCustomAttributes())
+        {
+            var attribute = metadata.GetCustomAttribute(handle);
+            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
+            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
+            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
+            var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
+            if (!string.Equals(metadata.GetString(type.Namespace), "System.Runtime.Versioning", StringComparison.Ordinal) ||
+                !string.Equals(metadata.GetString(type.Name), "TargetFrameworkAttribute", StringComparison.Ordinal)) continue;
+            var blob = metadata.GetBlobReader(attribute.Value);
+            return blob.ReadUInt16() == 1 && string.Equals(blob.ReadSerializedString(), ".NETFramework,Version=v4.8", StringComparison.Ordinal);
+        }
+        return false;
+    }
+
+    /// <summary>驗證尚未完整成功時一律失敗；固定代碼與布林欄位不包含檔案原文或簽章金鑰資訊。</summary>
+    /// <param name="reason">本工具擁有的固定原因代碼。</param>
+    /// <param name="output">呼叫端擁有的操作專屬輸出。</param>
+    /// <returns>供 PowerShell／CI 判定的拒絕結束碼。</returns>
+    private static int Reject(string reason, TextWriter output)
+    {
+        output.WriteLine(JsonSerializer.Serialize(new
+        {
+            ok = false,
+            reason,
+            checks = new { readable = false, strongName = false, targetFramework = false, allowedReferences = false, noDrawing = false, diagnosticsPlugin = false, size = false }
+        }));
+        return 1;
+    }
+}

diff --git a/tools/PluginPackaging/Test-PluginAssembly.ps1 b/tools/PluginPackaging/Test-PluginAssembly.ps1
new file mode 100644
index 000000000..f6dd145cd
--- /dev/null
+++ b/tools/PluginPackaging/Test-PluginAssembly.ps1
@@ -0,0 +1,31 @@
+<#
+以 .NET 10 的 System.Reflection.Metadata 讀取外掛，不把待測組件載入 PowerShell。
+Windows PowerShell 5.1 不具相容的 Metadata 執行環境，故薄入口交給專用工具；兩者皆離線。
+串流／PE reader 由工具操作擁有；此入口沒有背景程序或機密來源。
+#>
+[CmdletBinding()]
+param(
+    [string]$AssemblyPath
+)
+$ErrorActionPreference = 'Stop'
+# 在腳本主體才求預設路徑，避免 Windows PowerShell 5.1 參數繫結階段的 PSScriptRoot 空值。
+if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
+    $AssemblyPath = Join-Path $PSScriptRoot '../../artifacts/plugins/*.dll'
+}
+$projectPath = Join-Path $PSScriptRoot 'PluginPackaging.csproj'
+& dotnet build $projectPath -c Release --nologo -v quiet
+if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
+$toolPath = Join-Path $PSScriptRoot 'bin/Release/net10.0/PluginPackaging.dll'
+# 明確檔名交由工具 fail closed；萬用字元只在本機單層展開，不走訪其他目錄或父 worktree。
+$paths = if (Test-Path -LiteralPath $AssemblyPath -PathType Leaf) { @($AssemblyPath) }
+    else { @(Get-ChildItem -Path $AssemblyPath -File -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -ExpandProperty FullName) }
+if ($paths.Count -eq 0) {
+    Write-Output '{"ok":false,"reason":"NoAssemblies"}'
+    exit 1
+}
+$exitCode = 0
+foreach ($path in $paths) {
+    & dotnet $toolPath $path
+    if ($LASTEXITCODE -ne 0) { $exitCode = 1 }
+}
+exit $exitCode

diff --git a/tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1 b/tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
new file mode 100644
index 000000000..162daaee3
--- /dev/null
+++ b/tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
@@ -0,0 +1,115 @@
+<#
+兩個樣本各自建置於本次暫存資料夾：未簽章必須失敗、簽章完整的 net48 樣本必須通過。
+一次性 RSA 測試金鑰不讀正式環境變數，不輸出內容，完成或失敗均釋放並刪除整個專屬資料夾。
+樣本只供 PE 解析，絕不載入執行；斷言結果內容而非只看結束碼，避免任意失敗造成假綠燈。
+#>
+[CmdletBinding()]
+param()
+$ErrorActionPreference = 'Stop'
+$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
+$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ('speechmessage-packaging-' + [Guid]::NewGuid().ToString('N'))))
+$null = New-Item -ItemType Directory -Path $fixtureRoot
+$utf8 = New-Object Text.UTF8Encoding($false)
+$failures = 0
+try {
+    $keyPath = Join-Path $fixtureRoot 'fixture.snk'
+    $rsa = New-Object Security.Cryptography.RSACryptoServiceProvider(2048)
+    try {
+        $rsa.PersistKeyInCsp = $false
+        [IO.File]::WriteAllBytes($keyPath, $rsa.ExportCspBlob($true))
+    } finally { $rsa.Dispose() }
+    $escapedKey = [Security.SecurityElement]::Escape($keyPath)
+    $project = @"
+<Project Sdk="Microsoft.NET.Sdk">
+  <PropertyGroup>
+    <TargetFramework>net48</TargetFramework>
+    <GenerateDocumentationFile>true</GenerateDocumentationFile>
+    <SignAssembly>true</SignAssembly>
+    <AssemblyOriginatorKeyFile>$escapedKey</AssemblyOriginatorKeyFile>
+  </PropertyGroup>
+  <ItemGroup>
+    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
+  </ItemGroup>
+</Project>
+"@
+    $source = @'
+namespace SpeechMessage.Dynamics.Plugins.Diagnostics
+{
+    /// <summary>Inert metadata-only fixture. No code is executed.</summary>
+    public sealed class DiagnosticsPingPlugin { }
+}
+'@
+    $projectPath = Join-Path $fixtureRoot 'Fixture.csproj'
+    [IO.File]::WriteAllText($projectPath, ($project -replace "`r?`n", "`r`n") + "`r`n", $utf8)
+    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'Fixture.cs'), ($source -replace "`r?`n", "`r`n") + "`r`n", $utf8)
+    $samples = @('signed', 'unsigned', 'wrong-framework', 'missing-plugin', 'unexpected-reference', 'drawing', 'oversize', 'invalid-image')
+    foreach ($sample in $samples) {
+        $signed = $sample -ne 'unsigned'
+        $sampleOutput = Join-Path $fixtureRoot $sample
+        $sampleProject = $project
+        $sampleSource = $source
+        $failedCheck = switch ($sample) {
+            'unsigned' { 'strongName' }
+            'wrong-framework' { 'targetFramework' }
+            'missing-plugin' { 'diagnosticsPlugin' }
+            'unexpected-reference' { 'allowedReferences' }
+            'drawing' { 'noDrawing' }
+            'oversize' { 'size' }
+            'invalid-image' { 'readable' }
+        }
+        if ($sample -eq 'wrong-framework') { $sampleProject = $sampleProject.Replace('<TargetFramework>net48</TargetFramework>', '<TargetFramework>net10.0</TargetFramework>') }
+        if ($sample -eq 'missing-plugin') { $sampleSource = $sampleSource.Replace('DiagnosticsPingPlugin', 'OtherType') }
+        if ($sample -eq 'unexpected-reference') {
+            $sampleProject = $sampleProject.Replace('<ItemGroup>', '<ItemGroup><Reference Include="Microsoft.CSharp" />')
+            $sampleSource = $sampleSource.Replace('public sealed class DiagnosticsPingPlugin { }', 'public sealed class DiagnosticsPingPlugin { /// <summary>Inert dynamic metadata dependency.</summary>' + "`r`n" + 'public static object ReadValue(dynamic value) { return value.Value; } }')
+        }
+        if ($sample -eq 'drawing') {
+            $sampleProject = $sampleProject.Replace('<ItemGroup>', '<ItemGroup><Reference Include="System.Drawing" />')
+            $sampleSource = $sampleSource.Replace('public sealed class DiagnosticsPingPlugin { }', 'public sealed class DiagnosticsPingPlugin { /// <summary>Inert forbidden GDI metadata.</summary>' + "`r`n" + 'public static System.Drawing.Color ReadColor() { return System.Drawing.Color.Red; } }')
+        }
+        [IO.File]::WriteAllText($projectPath, ($sampleProject -replace "`r?`n", "`r`n") + "`r`n", $utf8)
+        [IO.File]::WriteAllText((Join-Path $fixtureRoot 'Fixture.cs'), ($sampleSource -replace "`r?`n", "`r`n") + "`r`n", $utf8)
+        & dotnet build $projectPath -c Release -p:SignAssembly=$signed -o $sampleOutput --nologo -v quiet
+        if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed; this is not a functional red result.' }
+        $assemblyPath = Join-Path $sampleOutput 'Fixture.dll'
+        if ($sample -eq 'oversize') {
+            $sizedStream = [IO.File]::OpenWrite($assemblyPath)
+            try { $sizedStream.SetLength(16 * 1024 * 1024) } finally { $sizedStream.Dispose() }
+        }
+        if ($sample -eq 'invalid-image') { [IO.File]::WriteAllBytes($assemblyPath, [byte[]]@(1, 2, 3, 4)) }
+        $checker = Join-Path $PSScriptRoot '../Test-PluginAssembly.ps1'
+        $output = @(& powershell -NoProfile -File $checker -AssemblyPath $assemblyPath 2>&1)
+        $code = $LASTEXITCODE
+        $jsonLine = $output | Where-Object { $_.ToString().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1
+        if ($null -eq $jsonLine) { throw 'Checker did not return structured evidence.' }
+        $result = $jsonLine.ToString() | ConvertFrom-Json
+        if ($sample -eq 'signed') {
+            $passed = $code -eq 0 -and $result.ok -eq $true -and $result.checks.strongName -eq $true -and
+                $result.checks.targetFramework -eq $true -and $result.checks.allowedReferences -eq $true -and
+                $result.checks.noDrawing -eq $true -and $result.checks.diagnosticsPlugin -eq $true -and $result.checks.size -eq $true
+        } else {
+            $passed = $code -eq 1 -and $result.ok -eq $false -and $result.checks.$failedCheck -eq $false
+        }
+        if ($passed) { Write-Output ('PASS ' + $sample) }
+        else { $failures++; Write-Output ('FAIL ' + $sample + ': contract not satisfied') }
+    }
+    # 多檔結果必須逐檔輸出，且任何一個未簽章都讓整體失敗；不能只驗最後一個 DLL。
+    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'signed/Fixture.dll') -Destination (Join-Path $fixtureRoot 'a-signed.dll')
+    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'unsigned/Fixture.dll') -Destination (Join-Path $fixtureRoot 'z-unsigned.dll')
+    $output = @(& powershell -NoProfile -File $checker -AssemblyPath (Join-Path $fixtureRoot '*.dll') 2>&1)
+    $code = $LASTEXITCODE
+    $results = @($output | Where-Object { $_.ToString().StartsWith('{', [StringComparison]::Ordinal) } | ForEach-Object { $_.ToString() | ConvertFrom-Json })
+    $passed = $code -eq 1 -and $results.Count -eq 2 -and @($results | Where-Object { $_.ok }).Count -eq 1
+    if ($passed) { Write-Output 'PASS mixed-directory' } else { $failures++; Write-Output 'FAIL mixed-directory: every DLL must be inspected' }
+} finally {
+    # 先解析絕對目標並核對專屬暫存邊界，再以同一個 PowerShell 直接移除，禁止跨殼層組命令。
+    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
+    if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
+        -not ([IO.Path]::GetFileName($resolvedFixture)).StartsWith('speechmessage-packaging-', [StringComparison]::Ordinal)) {
+        throw 'Refused cleanup outside the owned fixture directory.'
+    }
+    Remove-Item -LiteralPath $resolvedFixture -Force -Recurse
+}
+Write-Output ('Plugin packaging samples: total=' + ($samples.Count + 1) + ' failed=' + $failures)
+if ($failures -gt 0) { exit 1 }
+exit 0

diff --git a/tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1 b/tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1
new file mode 100644
index 000000000..941856934
--- /dev/null
+++ b/tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1
@@ -0,0 +1,84 @@
+<#
+故障注入只改測試程序的環境變數，finally 恢復；不讀取、複製或輸出正式金鑰內容。
+驗證一般 Debug／Release 不打包；明確要求打包才檢查金鑰、警告或輸出簽章 DLL。
+CI 明確傳入 UseEphemeralTestKey 才生成一次性測試金鑰；不依賴正式 secret，不作正式部署。
+所有輸出隔離於本次暫存資料夾，結束時檢查絕對路徑邊界後刪除，不改寫正式 artifacts。
+#>
+[CmdletBinding()]
+param([switch]$UseEphemeralTestKey)
+$ErrorActionPreference = 'Stop'
+$originalKey = [Environment]::GetEnvironmentVariable('SPEECHMESSAGE_PLUGIN_KEYFILE')
+if (-not $UseEphemeralTestKey -and [string]::IsNullOrWhiteSpace($originalKey)) {
+    throw 'Formal key environment is unavailable; restart Codex before running this suite.'
+}
+$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
+$projectPath = Join-Path $repoRoot 'SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj'
+$checker = Join-Path $PSScriptRoot '../Test-PluginAssembly.ps1'
+$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
+$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ('speechmessage-build-' + [Guid]::NewGuid().ToString('N'))))
+$null = New-Item -ItemType Directory -Path $fixtureRoot
+$failures = 0
+try {
+    $signingKey = $originalKey
+    if ($UseEphemeralTestKey) {
+        # 金鑰只存在本次測試專屬暫存目錄；RSA 與檔案各有確定的釋放／刪除路徑。
+        $signingKey = Join-Path $fixtureRoot 'ephemeral.snk'
+        $rsa = New-Object Security.Cryptography.RSACryptoServiceProvider(2048)
+        try {
+            $rsa.PersistKeyInCsp = $false
+            [IO.File]::WriteAllBytes($signingKey, $rsa.ExportCspBlob($true))
+        } finally { $rsa.Dispose() }
+    }
+    foreach ($sample in @('release-unset-no-package', 'debug-unset-no-package', 'release-unset', 'debug-unset', 'release-signed', 'release-invalid')) {
+        $configuration = if ($sample.StartsWith('debug-', [StringComparison]::Ordinal)) { 'Debug' } else { 'Release' }
+        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_PLUGIN_KEYFILE', $(if ($sample -eq 'release-signed') { $signingKey } elseif ($sample -eq 'release-invalid') { Join-Path $fixtureRoot 'missing.snk' } else { $null }))
+        $outputDirectory = Join-Path $fixtureRoot $sample
+        # 未要求打包的案例不得進入合併目標；即使沒有金鑰，也不能建立 artifacts 目錄。
+        $packageArguments = @()
+        if (-not $sample.EndsWith('-no-package', [StringComparison]::Ordinal)) { $packageArguments = @('-p:SpeechMessagePackagePlugin=true') }
+        $output = @(& dotnet build $projectPath -c $configuration @packageArguments --no-restore "-p:SpeechMessagePluginOutputDirectory=$outputDirectory" -p:SpeechMessageBuildNumber=37 --nologo -v minimal 2>&1)
+        $code = $LASTEXITCODE
+        $assemblyPath = Join-Path $outputDirectory 'SpeechMessage.Dynamics.Plugins.dll'
+        $text = $output -join "`n"
+        if ($sample.EndsWith('-no-package', [StringComparison]::Ordinal)) {
+            $passed = $code -eq 0 -and -not (Test-Path -LiteralPath $outputDirectory) -and
+                -not $text.Contains('SMP0001') -and -not $text.Contains('SMP0002') -and -not $text.Contains('SMP0003')
+        } elseif ($sample -eq 'release-invalid') {
+            $passed = $code -ne 0 -and $text.Contains('SMP0003') -and -not (Test-Path -LiteralPath $outputDirectory)
+        } elseif ($sample -eq 'release-unset') {
+            $passed = $code -ne 0 -and $text.Contains('SMP0001') -and -not (Test-Path -LiteralPath $assemblyPath)
+        } elseif ($sample -eq 'debug-unset') {
+            $passed = $code -eq 0 -and $text.Contains('SMP0002') -and (Test-Path -LiteralPath $assemblyPath)
+            if ($passed) {
+                $inspection = @(& powershell -NoProfile -File $checker -AssemblyPath $assemblyPath 2>&1)
+                $inspectCode = $LASTEXITCODE
+                $jsonLine = $inspection | Where-Object { $_.ToString().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1
+                $result = $jsonLine.ToString() | ConvertFrom-Json
+                $passed = $inspectCode -eq 1 -and $result.checks.strongName -eq $false -and $result.checks.targetFramework -eq $true
+            }
+        } else {
+            $passed = $code -eq 0 -and (Test-Path -LiteralPath $assemblyPath)
+            if ($passed) {
+                $null = & powershell -NoProfile -File $checker -AssemblyPath $assemblyPath
+                $passed = $LASTEXITCODE -eq 0
+                # 版本資源的唯讀查詢不載入執行 DLL；建置序號不改 AssemblyVersion 的穩定身分。
+                $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath)
+                $passed = $passed -and $version.FileVersion -eq '1.0.0.37' -and
+                    $version.ProductVersion.StartsWith('1.0.0-p0.37', [StringComparison]::Ordinal)
+            }
+        }
+        if ($passed) { Write-Output ('PASS ' + $sample) }
+        else { $failures++; Write-Output ('FAIL ' + $sample + ': packaging contract not satisfied') }
+    }
+} finally {
+    [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_PLUGIN_KEYFILE', $originalKey)
+    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
+    if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
+        -not ([IO.Path]::GetFileName($resolvedFixture)).StartsWith('speechmessage-build-', [StringComparison]::Ordinal)) {
+        throw 'Refused cleanup outside the owned build fixture directory.'
+    }
+    Remove-Item -LiteralPath $resolvedFixture -Force -Recurse
+}
+Write-Output ('Plugin build samples: total=6 failed=' + $failures)
+if ($failures -gt 0) { exit 1 }
+exit 0

diff --git a/tools/RepoQuality/AnalyzerProbe/AnalyzerProbe.csproj b/tools/RepoQuality/AnalyzerProbe/AnalyzerProbe.csproj
new file mode 100644
index 000000000..fcd1560fa
--- /dev/null
+++ b/tools/RepoQuality/AnalyzerProbe/AnalyzerProbe.csproj
@@ -0,0 +1,11 @@
+<Project Sdk="Microsoft.NET.Sdk">
+  <PropertyGroup>
+    <TargetFrameworks>net48;net10.0</TargetFrameworks>
+    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
+    <!-- SDK 在載入 Directory.Build.targets 前已決定文件路徑與輸出複製，故文件開關必須在本體設定。 -->
+    <GenerateDocumentationFile>true</GenerateDocumentationFile>
+  </PropertyGroup>
+  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
+    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
+  </ItemGroup>
+</Project>

diff --git a/tools/RepoQuality/AnalyzerProbe/ClockProbe.cs b/tools/RepoQuality/AnalyzerProbe/ClockProbe.cs
new file mode 100644
index 000000000..f110c5745
--- /dev/null
+++ b/tools/RepoQuality/AnalyzerProbe/ClockProbe.cs
@@ -0,0 +1,48 @@
+namespace SpeechMessage.RepoQuality.AnalyzerProbe;
+
+/// <summary>
+/// 以刻意違反時間來源契約的程式證明禁用 API 分析器確實參與建置。
+/// 此專案不加入正式方案，也不執行；測試以建置失敗且診斷為 RS0030 作為成功條件，
+/// 避免分析器沒有載入時仍將可編譯的程式誤判為守門通過。
+/// </summary>
+public static class ClockProbe
+{
+    /// <summary>
+    /// 依編譯符號注入單一時間、全域文化或字串比較違規，讓測試辨認每項規則的診斷。
+    /// 沒有指定符號時固定讀取當地時間；各分支僅供編譯，不執行或修改程序文化。
+    /// 不可移除違規呼叫，否則檢查腳本必須因缺少預期錯誤而失敗。
+    /// </summary>
+    public static void Trigger()
+    {
+#if PROBE_TODAY
+        _ = System.DateTime.Today;
+#elif PROBE_LOCAL_TIME
+        _ = default(System.DateTime).ToLocalTime();
+#elif PROBE_TIME_ZONE
+        _ = System.TimeZoneInfo.Local;
+#elif PROBE_CULTURE
+        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
+#elif PROBE_UI_CULTURE
+        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
+#elif PROBE_COMPARISON
+        _ = string.Compare("first", "second", System.StringComparison.CurrentCulture);
+#elif PROBE_SEARCH
+        _ = "haystack".IndexOf("needle");
+#elif PROBE_ORDINAL_OVERLOAD
+        _ = "haystack".Contains("needle");
+#elif PROBE_DRAWING
+        _ = System.Drawing.Point.Empty;
+#elif PROBE_DOCUMENTED || PROBE_MISSING_XML
+        // 文件測試只驗證編譯契約，避免其他故障注入遮蔽 XML 診斷或阻止正常輸出。
+#else
+        _ = System.DateTime.Now;
+#endif
+    }
+
+#if PROBE_MISSING_XML
+    // 此成員刻意缺少 XML 說明，測試必須以 CS1591 拒絕；不可補上 XML 讓故障消失。
+    public static void UndocumentedMember()
+    {
+    }
+#endif
+}

diff --git a/tools/RepoQuality/AnalyzerProbe/InterfaceNamingProbe.cs b/tools/RepoQuality/AnalyzerProbe/InterfaceNamingProbe.cs
new file mode 100644
index 000000000..04696e508
--- /dev/null
+++ b/tools/RepoQuality/AnalyzerProbe/InterfaceNamingProbe.cs
@@ -0,0 +1,11 @@
+#if PROBE_INTERFACE_KEYWORD
+namespace SpeechMessage.RepoQuality.AnalyzerProbe;
+
+/// <summary>刻意使用跨語言保留字的公開介面，僅供編譯器證明 net48 已載入 Recommended 規則。</summary>
+public interface IInterfaceNamingProbe
+{
+    /// <summary>此故障注入必須保持 Get 原名，預期編譯失敗且只有 CA1716，不執行或發布此介面。</summary>
+    /// <returns>探測不執行，因此沒有實際回傳值。</returns>
+    string Get();
+}
+#endif

diff --git a/tools/RepoQuality/AnalyzerProbe/NamingProbe.cs b/tools/RepoQuality/AnalyzerProbe/NamingProbe.cs
new file mode 100644
index 000000000..c8bfe5114
--- /dev/null
+++ b/tools/RepoQuality/AnalyzerProbe/NamingProbe.cs
@@ -0,0 +1,15 @@
+#if PROBE_NAMING
+namespace SpeechMessage.RepoQuality.AnalyzerProbe;
+
+/// <summary>
+/// 以同一份完整文件來源驗證測試命名例外的邊界，只建置、不執行。
+/// 產品或缺少任一測試條件時必須得到 CA1707；只有符合集中規則的測試專案可編譯。
+/// </summary>
+public static class NamingProbe
+{
+    /// <summary>刻意保留計畫指定底線格式，讓編譯器診斷成為唯一的命名守門依據。</summary>
+    public static void Operation_Condition_ExpectedResult()
+    {
+    }
+}
+#endif

diff --git a/tools/RepoQuality/Convert-TextEncoding.ps1 b/tools/RepoQuality/Convert-TextEncoding.ps1
new file mode 100644
index 000000000..4669b3eec
--- /dev/null
+++ b/tools/RepoQuality/Convert-TextEncoding.ps1
@@ -0,0 +1,110 @@
+<#
+.SYNOPSIS
+預設只預覽的無損編碼轉換；來源文字與字串常值驗證成功後才允許明確套用。
+.DESCRIPTION
+辨認 UTF BOM、嚴格 UTF-8 或 CP950，產生無 BOM UTF-8 與 CRLF 候選。
+manifest 只保存編碼、雜湊與常值一致性，不輸出任何實際常值，避免將憑證或業務資料複製到診斷。
+逐字／raw 字串內的換行若會改變內容，該檔拒絕寫入。每檔以同目錄暫存檔原子替換，失敗保留原檔。
+所有檔案與雜湊生命週期只限本次呼叫，暫存檔於 finally 確定清除；不保存跨使用者狀態。
+.PARAMETER Path
+明確指定要檢查的來源檔案，不遞迴尋找或批次改寫其他檔。
+.PARAMETER ManifestPath
+必要的 JSON 稽核輸出位置，不能與來源相同；DryRun 只寫此 manifest，不改來源。
+.PARAMETER Apply
+明確指定才寫入來源；不可與 DryRun 同用。
+.PARAMETER DryRun
+明確預覽開關；未給 Apply 時預設就是此模式。
+#>
+[CmdletBinding()]
+param([Parameter(Mandatory=$true)][string[]]$Path, [Parameter(Mandatory=$true)][string]$ManifestPath, [switch]$Apply, [switch]$DryRun)
+
+Set-StrictMode -Version Latest
+$ErrorActionPreference = 'Stop'
+. (Join-Path $PSScriptRoot 'TextEncoding.Common.ps1')
+try {
+    if ($Apply -and $DryRun) { throw 'Apply and DryRun cannot be combined.' }
+    $manifestFile = [IO.Path]::GetFullPath($ManifestPath)
+    $utf8 = New-Object Text.UTF8Encoding($false, $true)
+    $records = New-Object 'Collections.Generic.List[object]'
+    $failures = 0
+    $normalizedSources = @($Path | ForEach-Object { [IO.Path]::GetFullPath($_) })
+    if (@($normalizedSources | Sort-Object -Unique).Count -ne $normalizedSources.Count) {
+        throw 'DuplicateSourcePath: the same source path was supplied more than once.'
+    }
+    if ($normalizedSources -contains $manifestFile) { throw 'ManifestPath cannot overwrite a source file.' }
+    foreach ($sourcePath in $normalizedSources) {
+        $fullPath = [IO.Path]::GetFullPath($sourcePath)
+        if ($fullPath.Equals($manifestFile, [StringComparison]::OrdinalIgnoreCase)) { throw 'ManifestPath cannot overwrite a source file.' }
+        $record = [ordered]@{ path = $fullPath; originalEncoding = $null; beforeSha256 = $null; afterSha256 = $null; chineseStringLiteralCount = 0; chineseStringLiteralsUnchanged = $false; stringLiteralsUnchanged = $false; status = 'failed'; error = $null }
+        $temporary = $null
+        try {
+            $bytes = [IO.File]::ReadAllBytes($fullPath)
+            $record.beforeSha256 = Get-TextSha256 -Bytes $bytes
+            $skip = 0
+            if ($bytes.Length -ge 4 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE -and $bytes[2] -eq 0 -and $bytes[3] -eq 0) {
+                $encoding = New-Object Text.UTF32Encoding($false, $false, $true); $skip = 4; $record.originalEncoding = 'utf-32le-bom'
+            }
+            elseif ($bytes.Length -ge 4 -and $bytes[0] -eq 0 -and $bytes[1] -eq 0 -and $bytes[2] -eq 0xFE -and $bytes[3] -eq 0xFF) {
+                $encoding = New-Object Text.UTF32Encoding($true, $false, $true); $skip = 4; $record.originalEncoding = 'utf-32be-bom'
+            }
+            elseif ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
+                $encoding = $utf8; $skip = 3; $record.originalEncoding = 'utf-8-bom'
+            }
+            elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
+                $encoding = New-Object Text.UnicodeEncoding($false, $false, $true); $skip = 2; $record.originalEncoding = 'utf-16le-bom'
+            }
+            elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
+                $encoding = New-Object Text.UnicodeEncoding($true, $false, $true); $skip = 2; $record.originalEncoding = 'utf-16be-bom'
+            }
+            else {
+                try { [void]$utf8.GetString($bytes); $encoding = $utf8; $record.originalEncoding = 'utf-8' }
+                catch [Text.DecoderFallbackException] {
+                    $encoding = [Text.Encoding]::GetEncoding(950, [Text.EncoderFallback]::ExceptionFallback, [Text.DecoderFallback]::ExceptionFallback)
+                    $record.originalEncoding = 'cp950'
+                }
+            }
+            try { $source = $encoding.GetString($bytes, $skip, $bytes.Length - $skip) }
+            catch [Text.DecoderFallbackException] { throw 'InvalidEncoding: source cannot be decoded losslessly.' }
+            if ($source.Contains([string][char]0xFFFD)) { throw 'InvalidEncoding: source already contains a replacement character.' }
+            $candidate = [regex]::Replace($source, '\r\n|\r|\n', "`r`n")
+            if (-not $candidate.EndsWith("`r`n", [StringComparison]::Ordinal)) { $candidate += "`r`n" }
+            $afterBytes = $utf8.GetBytes($candidate)
+            $record.afterSha256 = Get-TextSha256 -Bytes $afterBytes
+            $beforeLiterals = @(Get-TextLiteralSegments -Source $source)
+            $afterLiterals = @(Get-TextLiteralSegments -Source $candidate)
+            $beforeChinese = @($beforeLiterals | Where-Object { $_ -match '[\u3400-\u9FFF\uF900-\uFAFF]' })
+            $afterChinese = @($afterLiterals | Where-Object { $_ -match '[\u3400-\u9FFF\uF900-\uFAFF]' })
+            $record.chineseStringLiteralCount = $beforeChinese.Count
+            $record.chineseStringLiteralsUnchanged = ($beforeChinese.Count -eq $afterChinese.Count)
+            for ($i = 0; $record.chineseStringLiteralsUnchanged -and $i -lt $beforeChinese.Count; $i++) {
+                if (-not $beforeChinese[$i].Equals($afterChinese[$i], [StringComparison]::Ordinal)) { $record.chineseStringLiteralsUnchanged = $false }
+            }
+            $record.stringLiteralsUnchanged = ($beforeLiterals.Count -eq $afterLiterals.Count)
+            for ($i = 0; $record.stringLiteralsUnchanged -and $i -lt $beforeLiterals.Count; $i++) {
+                if (-not $beforeLiterals[$i].Equals($afterLiterals[$i], [StringComparison]::Ordinal)) { $record.stringLiteralsUnchanged = $false }
+            }
+            if (-not $record.chineseStringLiteralsUnchanged -or -not $record.stringLiteralsUnchanged) { throw 'StringLiteralChanged: normalization would change literal content.' }
+            $issues = @(Get-TextByteIssues -Bytes $afterBytes -Extension ([IO.Path]::GetExtension($fullPath).ToLowerInvariant()))
+            if ($issues.Count -gt 0) { throw ('CandidateRejected: ' + ($issues -join ', ')) }
+            if ($Apply) {
+                $temporary = $fullPath + '.encoding-' + [guid]::NewGuid().ToString('N') + '.tmp'
+                [IO.File]::WriteAllBytes($temporary, $afterBytes)
+                # PS 5.1 會把一般空值轉成空字串路徑，須以 NullString 明確傳入沒有備份檔名的原子替換。
+                [IO.File]::Replace($temporary, $fullPath, [NullString]::Value)
+                $record.status = 'applied'
+            }
+            else { $record.status = 'dry-run' }
+        }
+        catch { $record.error = $_.Exception.Message; $failures++ }
+        finally { if ($temporary -and [IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
+        $records.Add([pscustomobject]$record)
+        if ($record.error) { Write-Output ("FAIL $($record.path): " + $record.error) }
+    }
+    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($manifestFile))
+    $manifest = [ordered]@{ dryRun = (-not $Apply); failureCount = $failures; files = @($records.ToArray()) }
+    [IO.File]::WriteAllText($manifestFile, (($manifest | ConvertTo-Json -Depth 8) -replace "`r?`n", "`r`n") + "`r`n", $utf8)
+    Write-Output "Text conversion: files=$($records.Count) dryRun=$(-not $Apply) failures=$failures"
+    if ($failures -gt 0) { exit 1 }
+    exit 0
+}
+catch { Write-Output "FAIL Text conversion: $($_.Exception.Message)"; exit 1 }
```
