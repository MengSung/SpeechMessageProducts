# CCG reviewer Task: fix-dual-model-operation-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts

## Request
# Final review request: fix CCG dual-model operation after regex narrowing

Please review the following changes for correctness, maintainability, and failure-mode handling. Focus on:

- Claude default model handling via CLAUDE_MODEL=sonnet.
- Gemini quota/billing classification using explicit quota/session/billing signals only.
- Health/smoke summary fields and degraded fallback behavior.
- Any remaining risk that provider failures could be misreported as full dual-model success.

Return findings as Critical / Warning / Info.

```diff
diff --git a/docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1 b/docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1
index 86779b6c..b2c46ed2 100644
--- a/docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1
+++ b/docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1
@@ -71,6 +71,9 @@ function Initialize-CcgToolchainEnvironment {
     $env:GEMINI_CLI_TRUST_WORKSPACE = "true"
     $env:CODEAGENT_LITE_MODE = "true"
     $env:PYTHONIOENCODING = "utf-8"
+    if ([string]::IsNullOrWhiteSpace($env:CLAUDE_MODEL)) {
+        $env:CLAUDE_MODEL = "sonnet"
+    }
 
     return [pscustomobject]@{
         ToolPathEntries = $toolPathEntries
@@ -79,6 +82,7 @@ function Initialize-CcgToolchainEnvironment {
         GEMINI_CLI_TRUST_WORKSPACE = $env:GEMINI_CLI_TRUST_WORKSPACE
         CODEAGENT_LITE_MODE = $env:CODEAGENT_LITE_MODE
         PYTHONIOENCODING = $env:PYTHONIOENCODING
+        CLAUDE_MODEL = $env:CLAUDE_MODEL
     }
 }
 
@@ -150,6 +154,9 @@ function Invoke-ProcessCapture {
     $startInfo.Environment["GEMINI_CLI_TRUST_WORKSPACE"] = "true"
     $startInfo.Environment["CODEAGENT_LITE_MODE"] = "true"
     $startInfo.Environment["PYTHONIOENCODING"] = "utf-8"
+    if (-not [string]::IsNullOrWhiteSpace($env:CLAUDE_MODEL)) {
+        $startInfo.Environment["CLAUDE_MODEL"] = $env:CLAUDE_MODEL
+    }
     $startInfo.Environment["Path"] = $env:Path
 
     $process = [System.Diagnostics.Process]::new()
@@ -214,7 +221,7 @@ OUTPUT:
 
 function Test-QuotaBlockedText {
     param([string]$Text)
-    return ($Text -match "(?i)(you'?ve hit your session limit|session limit|rate limit|rate_limit|quota exceeded|insufficient_quota|resource_exhausted|usage limit|http\s*429|\b429\b)")
+    return ($Text -match "(?i)(you'?ve hit your session limit|you'?ve reached your .* limit|fable 5 limit|session limit|rate limit|rate_limit|quota exceeded|insufficient_quota|resource_exhausted|usage limit|http\s*429|\b429\b|insufficient balance|balance insufficient|billing account|enable billing|billing required|payment required.*(quota|balance|billing|account)|\u4f59\u989d\u4e0d\u8db3|\u9918\u984d\u4e0d\u8db3|\u4f59\u989d\u4e0d\u591f)")
 }
 
 function Test-BackendQuotaBlocked {
@@ -265,15 +272,65 @@ function Test-BackendProducedOutput {
     return ($modelOutput.Length -ge 20)
 }
 
-function Invoke-ClaudeDirectQuotaProbe {
-    param([Parameter(Mandatory = $true)][string]$WorkingDirectory)
+function Get-BackendFailureReason {
+    param(
+        [Parameter(Mandatory = $true)]$Result,
+        [bool]$BackendOk,
+        [bool]$QuotaBlocked,
+        [bool]$ProducedOutput
+    )
 
-    $claudeCandidates = @(
-        "C:\Users\Administrator\AppData\Roaming\npm\claude.cmd",
-        "C:\Users\Administrator\.claude\bin\claude.cmd"
+    if ($BackendOk) {
+        return "ok"
+    }
+    if ($QuotaBlocked) {
+        return "provider-quota-or-billing-blocked"
+    }
+    if ($Result.TimedOut) {
+        return "timeout"
+    }
+    if (-not $ProducedOutput) {
+        return "no-usable-output"
+    }
+    return "backend-exit-$($Result.ExitCode)"
+}
+
+function Get-ShortDiagnostic {
+    param(
+        [string]$Text,
+        [string]$Fallback
     )
 
-    $claudePath = $claudeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
+    if ([string]::IsNullOrWhiteSpace($Text)) {
+        return $Fallback
+    }
+
+    $lines = @(
+        ($Text -replace "`r", "") -split "`n" |
+            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
+    )
+    $priorityLines = @(
+        $lines | Where-Object {
+            $_ -match "(?i)(error when talking to gemini api|_apierror|api error|api key not valid|invalid_argument|resource_exhausted|insufficient_quota|quota|billing|payment|required|status\s*:?\s*(400|403|429)|exited with status\s+(400|403|429))"
+        }
+    )
+    $normalized = if ($priorityLines.Count -gt 0) {
+        ($priorityLines | Select-Object -First 3) -join " "
+    } else {
+        $lines -join " "
+    }
+    if ($normalized.Length -gt 500) {
+        return $normalized.Substring(0, 500)
+    }
+    return $normalized
+}
+
+function Invoke-ClaudeDirectQuotaProbe {
+    param([Parameter(Mandatory = $true)][string]$WorkingDirectory)
+
+    $claudePath = Resolve-ExecutablePath `
+        -Name "claude.cmd" `
+        -FallbackPaths @("C:\Users\Administrator\AppData\Roaming\npm\claude.cmd", "C:\Users\Administrator\.claude\bin\claude.cmd")
     if (-not $claudePath) {
         return [pscustomobject]@{
             Ran = $false
@@ -298,6 +355,36 @@ function Invoke-ClaudeDirectQuotaProbe {
     }
 }
 
+function Invoke-GeminiDirectQuotaProbe {
+    param([Parameter(Mandatory = $true)][string]$WorkingDirectory)
+
+    $geminiPath = Resolve-ExecutablePath `
+        -Name "gemini.cmd" `
+        -FallbackPaths @("C:\Users\Administrator\AppData\Roaming\npm\gemini.cmd", "C:\Users\Administrator\.claude\bin\gemini.cmd")
+    if (-not $geminiPath) {
+        return [pscustomobject]@{
+            Ran = $false
+            QuotaBlocked = $false
+            Output = "gemini.cmd not found for direct quota probe."
+        }
+    }
+
+    $probe = Invoke-ProcessCapture `
+        -FilePath $geminiPath `
+        -Arguments @("-o", "stream-json", "-y") `
+        -InputText "Smoke test only. Reply with exactly: GEMINI_DIRECT_QUOTA_PROBE_OK" `
+        -WorkingDirectory $WorkingDirectory `
+        -TimeoutSeconds 120
+
+    $combined = (($probe.StdOut + "`n" + $probe.StdErr) -replace "`r", "").Trim()
+
+    return [pscustomobject]@{
+        Ran = $true
+        QuotaBlocked = (Test-QuotaBlockedText -Text $combined)
+        Output = $combined
+    }
+}
+
 $toolchainEnvironment = Initialize-CcgToolchainEnvironment
 
 $repositoryFullPath = (Resolve-Path -LiteralPath $RepositoryPath).Path
@@ -431,6 +518,18 @@ for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
         $diagnostic = $null
         $quotaBlocked = Test-BackendQuotaBlocked -Result $result -Diagnostic $diagnostic
 
+        if ($backend -eq "gemini" -and -not $quotaBlocked -and $result.ExitCode -ne 0) {
+            # Gemini CLI can return a generic 403 through the wrapper while the
+            # direct CLI stderr exposes the provider message, such as balance
+            # exhaustion. Probe directly so quota/billing blocks are not
+            # misclassified as local toolchain failures.
+            $directProbe = Invoke-GeminiDirectQuotaProbe -WorkingDirectory $repositoryFullPath
+            $diagnostic = $directProbe.Output
+            if ($directProbe.QuotaBlocked) {
+                $quotaBlocked = $true
+            }
+        }
+
         if ($backend -eq "claude" -and -not $quotaBlocked -and $result.ExitCode -ne 0) {
             # Claude Code may expose the real provider/session-limit error only
             # when invoked directly. If wrapper stderr only says exit 1, run a
@@ -445,6 +544,10 @@ for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
 
         $producedOutput = Test-BackendProducedOutput -Result $result -Role $Role
         $backendOk = ($result.ExitCode -eq 0 -and -not $result.TimedOut -and -not $quotaBlocked -and $producedOutput)
+        $failureReason = Get-BackendFailureReason -Result $result -BackendOk $backendOk -QuotaBlocked $quotaBlocked -ProducedOutput $producedOutput
+        if ($quotaBlocked -and [string]::IsNullOrWhiteSpace($diagnostic)) {
+            $diagnostic = Get-ShortDiagnostic -Text ($result.StdErr + "`n" + $result.StdOut) -Fallback "Provider quota or billing block detected."
+        }
 
         $attemptRecord.backends += [ordered]@{
             backend = $backend
@@ -452,6 +555,7 @@ for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
             exitCode = $result.ExitCode
             timedOut = $result.TimedOut
             quotaBlocked = $quotaBlocked
+            failureReason = $failureReason
             producedOutput = $producedOutput
             outputLength = (Get-ModelResponseText -StdOut $result.StdOut).Length
             diagnostic = $diagnostic
diff --git a/docs/scripts/Test-CcgDualModelHealth.ps1 b/docs/scripts/Test-CcgDualModelHealth.ps1
index f0679465..0565c2e6 100644
--- a/docs/scripts/Test-CcgDualModelHealth.ps1
+++ b/docs/scripts/Test-CcgDualModelHealth.ps1
@@ -83,6 +83,9 @@ function Invoke-CommandCapture {
         $startInfo.Environment["GEMINI_CLI_TRUST_WORKSPACE"] = "true"
         $startInfo.Environment["CODEAGENT_LITE_MODE"] = "true"
         $startInfo.Environment["PYTHONIOENCODING"] = "utf-8"
+        if (-not [string]::IsNullOrWhiteSpace($env:CLAUDE_MODEL)) {
+            $startInfo.Environment["CLAUDE_MODEL"] = $env:CLAUDE_MODEL
+        }
 
         $process = [System.Diagnostics.Process]::new()
         $process.StartInfo = $startInfo
@@ -145,10 +148,33 @@ OUTPUT: one line only
         -WorkingDirectory $RepositoryPath
 
     $combined = (($result.StdOut + "`n" + $result.StdErr) -replace "`r", "")
-    $quotaBlocked = $combined -match "session limit|rate limit|quota|429|usage limit|You've hit your session limit"
+    $quotaBlockedPattern = "(?i)(you'?ve hit your session limit|you'?ve reached your .* limit|fable 5 limit|session limit|rate limit|rate_limit|quota exceeded|insufficient_quota|resource_exhausted|usage limit|http\s*429|\b429\b|insufficient balance|balance insufficient|billing account|enable billing|billing required|payment required.*(quota|balance|billing|account)|\u4f59\u989d\u4e0d\u8db3|\u9918\u984d\u4e0d\u8db3|\u4f59\u989d\u4e0d\u591f)"
+    $quotaBlocked = $combined -match $quotaBlockedPattern
     $ok = ($result.ExitCode -eq 0 -and $combined -match [regex]::Escape($ExpectedText))
     $diagnostic = $null
 
+    if ($Backend -eq "gemini" -and -not $ok -and -not $quotaBlocked) {
+        # Gemini sometimes reports only a generic wrapper failure while direct
+        # CLI stderr includes the provider reason, such as exhausted balance.
+        $geminiPath = Resolve-ExecutablePath `
+            -Name "gemini.cmd" `
+            -FallbackPaths @("C:\Users\Administrator\AppData\Roaming\npm\gemini.cmd", "C:\Users\Administrator\.claude\bin\gemini.cmd")
+
+        if ($geminiPath) {
+            $directProbe = Invoke-CommandCapture `
+                -FilePath $geminiPath `
+                -Arguments @("-o", "stream-json", "-y") `
+                -InputText "Smoke test only. Reply with exactly: GEMINI_DIRECT_HEALTH_OK" `
+                -TimeoutSeconds 120 `
+                -WorkingDirectory $RepositoryPath
+
+            $diagnostic = (($directProbe.StdOut + "`n" + $directProbe.StdErr) -replace "`r", "").Trim()
+            if ($diagnostic -match $quotaBlockedPattern) {
+                $quotaBlocked = $true
+            }
+        }
+    }
+
     if ($Backend -eq "claude" -and -not $ok -and -not $quotaBlocked) {
         # codeagent-wrapper sometimes collapses Claude provider errors into only
         # "claude exited with status 1". Probe Claude directly so this script can
@@ -165,18 +191,46 @@ OUTPUT: one line only
                 -WorkingDirectory $RepositoryPath
 
             $diagnostic = (($directProbe.StdOut + "`n" + $directProbe.StdErr) -replace "`r", "").Trim()
-            if ($diagnostic -match "session limit|rate limit|quota|429|usage limit|You've hit your session limit") {
+            if ($diagnostic -match $quotaBlockedPattern) {
                 $quotaBlocked = $true
             }
         }
     }
 
+    $failureReason = "ok"
+    if (-not $ok) {
+        if ($quotaBlocked) {
+            $failureReason = "provider-quota-or-billing-blocked"
+        }
+        elseif ($result.TimedOut) {
+            $failureReason = "timeout"
+        }
+        else {
+            $failureReason = "backend-exit-$($result.ExitCode)"
+        }
+    }
+
+    if ($quotaBlocked -and [string]::IsNullOrWhiteSpace($diagnostic)) {
+        $lines = @($combined -split "`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
+        $priorityLines = @(
+            $lines | Where-Object {
+                $_ -match "(?i)(error when talking to gemini api|_apierror|api error|api key not valid|invalid_argument|resource_exhausted|insufficient_quota|quota|billing|payment|required|status\s*:?\s*(400|403|429)|exited with status\s+(400|403|429))"
+            }
+        )
+        $diagnostic = if ($priorityLines.Count -gt 0) {
+            ($priorityLines | Select-Object -First 3) -join " "
+        } else {
+            ($lines | Select-Object -First 6) -join " "
+        }
+    }
+
     [pscustomobject]@{
         Backend = $Backend
         Ok = $ok
         ExitCode = $result.ExitCode
         TimedOut = $result.TimedOut
         QuotaBlocked = $quotaBlocked
+        FailureReason = $failureReason
         Diagnostic = $diagnostic
         Output = $combined.Trim()
     }
@@ -195,6 +249,9 @@ New-DirectoryIfMissing -Path $resolvedOutputDirectory
 $env:GEMINI_CLI_TRUST_WORKSPACE = "true"
 $env:CODEAGENT_LITE_MODE = "true"
 $env:PYTHONIOENCODING = "utf-8"
+if ([string]::IsNullOrWhiteSpace($env:CLAUDE_MODEL)) {
+    $env:CLAUDE_MODEL = "sonnet"
+}
 
 $wantedPathEntries = @(
     "C:\Users\Administrator\AppData\Roaming\npm",
@@ -249,6 +306,7 @@ $summary = [ordered]@{
         GEMINI_CLI_TRUST_WORKSPACE = $env:GEMINI_CLI_TRUST_WORKSPACE
         CODEAGENT_LITE_MODE = $env:CODEAGENT_LITE_MODE
         PYTHONIOENCODING = $env:PYTHONIOENCODING
+        CLAUDE_MODEL = $env:CLAUDE_MODEL
     }
     executables = [ordered]@{
         wrapper = $wrapperPath

```

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.