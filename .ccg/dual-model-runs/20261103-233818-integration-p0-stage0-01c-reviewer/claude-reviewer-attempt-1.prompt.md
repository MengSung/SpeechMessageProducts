ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: integration-p0-stage0-01c

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# 階段0 第1批 第3/3份：守門工具與 CI

你是唯讀外部審查者。本輪為「單模型（使用者指定 ClaudeOnly）」，不是雙模型或配額降級。只審查，不改檔、不提交、不執行產品／工具／測試，不建立任何網路或 D365／LINE 連線。
審查重點：跨使用者／跨教會隔離、資源釋放、機密外洩、例外責任（設計10.5／10.9）、net48/net10行為一致、守門假綠燈。所有真實連線驗收仍由使用者負責。
機密欄位已遮罩；遮罩值不可用來推斷實際設定正確性。不得讀取 appsettings*、User Secrets、環境變數、Logs、金鑰／授權檔，或以 git show 取未遮罩版本。只以所附安全差異審查；如需上下文，可只查明確的非機密程式來源。不要開啟 .ccg/dual-model-runs 的其他歷史輸出或 research 原始紀錄。
輸出只列可驗證的 Critical／High／Medium／Low，包含檔案、行號、觸發條件、具體影響及最小修法；測試不足須說明可重現的缺口，不把純猜測當發現。沒有問題請明寫。不得聲稱實跑測試。
已核定例外：嚴格 .Tests 測試專案僅 CA1707 放寬；未實作 API 的 CA1716 可改語意近名；Core 不通知，Plugin 僅追蹤，最終告警在 net10 重試用盡／階段5；正式金鑰只程序環境且不讀內容；本階段不存在業務 Rejected。小組頁面由使用者確認恢復，圖表未逐項驗證，之前審查兩次為逾時。

範圍：73b6e6cf409daa7559fd3c11d482e3efc542c0d7..ae29cb40aa5c395f83c169068d7ca40e7094c509
安全差異 SHA256：10bd5bd8f8b445161cc106e7dbb70034eef03155bf8048365c2c3aa0aa81d2f3
同批其他分份須一併審查後才算完成，本份不可代表全批。
專項：檔案掃描邊界只限本 worktree／方案；編碼、重複、分析器、NuGetAudit、覆蓋率、生成檔與 CI 是否可能假綠燈；一般金鑰缺失失敗及 runner 清理；簽章只能明確要求。P0-01 的既有測試修復不得削弱斷言。

```diff
diff --git a/tools/RepoQuality/Find-DuplicateCode.ps1 b/tools/RepoQuality/Find-DuplicateCode.ps1
new file mode 100644
index 000000000..90bd44fc8
--- /dev/null
+++ b/tools/RepoQuality/Find-DuplicateCode.ps1
@@ -0,0 +1,202 @@
+<#
+.SYNOPSIS
+掃描目前方案內的跨專案內容副本與命名空間加型別重名，只拒絕基準之外的新重複。
+.DESCRIPTION
+從指定方案取得專案，絕不向上找父 worktree；排除建置、文件、參考方案、其他 worktree 與產生檔。
+目錄逐層列舉且跳過 reparse point，不沿 junction 讀外部檔。只在區域內處理單一來源文字，
+輸出僅有雜湊、型別與路徑，不保存程式本文、使用者資料或憑證，不啟動產品或外部程序。
+既有 UTF-8／CP950 只做嚴格唯讀解碼，不改寫來源；編碼合法性由專用守門負責。
+.PARAMETER RepositoryPath
+目前 worktree 根目錄，預設為工具上兩層；測試可傳獨立合成方案。
+.PARAMETER SolutionPath
+根目錄內的方案相對路徑，不接受 repo 外路徑。
+.PARAMETER Baseline
+根目錄內的 JSON 基準；預設 tools/RepoQuality/duplicate-baseline.json。
+.PARAMETER WriteBaseline
+明確更新基準；缺少基準時也會建立。平常棘輪檢查不改既有基準。
+#>
+[CmdletBinding()]
+param([string]$RepositoryPath, [string]$SolutionPath = 'SpeechMessageProducts.sln', [string]$Baseline, [switch]$WriteBaseline)
+
+Set-StrictMode -Version Latest
+$ErrorActionPreference = 'Stop'
+. (Join-Path $PSScriptRoot 'TextEncoding.Common.ps1')
+
+function Get-SafeRepositoryPath {
+    <# 在任何讀取前驗證絕對根邊界與每個既有路徑元件，拒絕專案／基準透過 junction 跳到外部。 #>
+    param([string]$Path)
+    $full = [IO.Path]::GetFullPath($Path)
+    if (-not $full.StartsWith($script:rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'UnsafePath: path is outside the repository.' }
+    $cursor = $script:root
+    foreach ($segment in $full.Substring($script:rootPrefix.Length).Split([IO.Path]::DirectorySeparatorChar)) {
+        $cursor = Join-Path $cursor $segment
+        if (([IO.File]::Exists($cursor) -or [IO.Directory]::Exists($cursor)) -and
+            (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'UnsafePath: reparse point is not allowed.' }
+    }
+    return $full
+}
+
+function Get-SourceText {
+    <# 單檔上限十六 MiB；嚴格解碼不使用替代字元，CP950 僅供既有來源唯讀盤點。 #>
+    param([string]$Path)
+    if ((New-Object IO.FileInfo($Path)).Length -gt 16777216) { throw 'SourceTooLarge: source exceeds the bounded scan size.' }
+    $bytes = [IO.File]::ReadAllBytes($Path)
+    try { $text = $script:utf8.GetString($bytes) }
+    catch [Text.DecoderFallbackException] {
+        $cp950 = [Text.Encoding]::GetEncoding(950, [Text.EncoderFallback]::ExceptionFallback, [Text.DecoderFallback]::ExceptionFallback)
+        $text = $cp950.GetString($bytes)
+    }
+    return $text.TrimStart([char]0xFEFF)
+}
+
+function Get-SourceTypeNames {
+    <# 遮罩註解與字串後，追蹤 namespace 大括號與檔案範圍 namespace，避免把訊息文字當型別。
+       此為來源宣告掃描，不載入組件或執行程式；只回傳型別名稱，本文不跨檔保存。 #>
+    param([string]$Source)
+    $maskPattern = @'
+(?s)/\*.*?\*/|//[^\r\n]*|(?:\$@|@\$|@)"(?:""|[^"])*"|(?<raw>"{3,}).*?\k<raw>|\$?"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'
+'@
+    $code = [regex]::Replace($Source, $maskPattern.Trim(), ' ')
+    $identifier = '@?[\p{L}_][\p{L}\p{Nd}_]*'
+    $pattern = '(?<ns>\bnamespace\s+(?<nsname>' + $identifier + '(?:\s*\.\s*' + $identifier + ')*)\s*(?<term>[;{]))|(?<type>\b(?:class|struct|record(?:\s+(?:class|struct))?)\s+(?<typename>' + $identifier + '))|(?<open>\{)|(?<close>\})'
+    $namespace = ''
+    $level = 0
+    $frames = New-Object 'Collections.Generic.List[object]'
+    foreach ($token in [regex]::Matches($code, $pattern)) {
+        if ($token.Groups['ns'].Success) {
+            $name = ($token.Groups['nsname'].Value -replace '\s', '') -replace '@', ''
+            if ($token.Groups['term'].Value -eq '{') {
+                $frames.Add(@{ Level = $level; Parent = $namespace })
+                $level++
+                if ($namespace) { $namespace += '.' + $name } else { $namespace = $name }
+            }
+            else { $namespace = $name }
+        }
+        elseif ($token.Groups['open'].Success) { $level++ }
+        elseif ($token.Groups['close'].Success) {
+            $level--
+            if ($frames.Count -gt 0 -and $frames[$frames.Count - 1].Level -eq $level) {
+                $namespace = $frames[$frames.Count - 1].Parent
+                $frames.RemoveAt($frames.Count - 1)
+            }
+        }
+        elseif ($token.Groups['type'].Success) {
+            $name = $token.Groups['typename'].Value.TrimStart([char]'@')
+            if ($namespace) { $namespace + '.' + $name } else { $name }
+        }
+    }
+}
+
+function Get-ProjectSourceFiles {
+    <# 一層一層走訪指定專案，排除資料夾前就停止往下；不讓外部路徑或 reparse point 進入文字讀取。 #>
+    param([string]$ProjectDirectory)
+    $pending = New-Object 'Collections.Generic.Stack[IO.DirectoryInfo]'
+    $pending.Push((New-Object IO.DirectoryInfo($ProjectDirectory)))
+    while ($pending.Count -gt 0) {
+        foreach ($item in $pending.Pop().EnumerateFileSystemInfos()) {
+            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
+            if (($item.Attributes -band [IO.FileAttributes]::Directory) -ne 0) {
+                if ($item.Name -match '^(bin|obj|docs|\.worktrees|\.ccg|\.\u5176\u4ED6\u89E3\u6C7A\u65B9\u6848)$') { continue }
+                $pending.Push([IO.DirectoryInfo]$item)
+            }
+            elseif ($item.Extension -eq '.cs' -and $item.Name -notmatch '(?i)(\.g\.cs|\.Designer\.cs)$') { $item.FullName }
+        }
+    }
+}
+
+try {
+    if (-not $RepositoryPath) { $RepositoryPath = Join-Path $PSScriptRoot '..\..' }
+    $script:root = (Resolve-Path -LiteralPath $RepositoryPath).Path
+    $script:rootPrefix = [IO.Path]::GetFullPath($script:root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
+    $script:utf8 = New-Object Text.UTF8Encoding($false, $true)
+    if (-not [IO.Path]::IsPathRooted($SolutionPath)) { $SolutionPath = Join-Path $script:root $SolutionPath }
+    $solution = Get-SafeRepositoryPath $SolutionPath
+    if (-not $Baseline) { $Baseline = Join-Path $script:root 'tools/RepoQuality/duplicate-baseline.json' }
+    if (-not [IO.Path]::IsPathRooted($Baseline)) { $Baseline = Join-Path $script:root $Baseline }
+    $Baseline = Get-SafeRepositoryPath $Baseline
+    if ([IO.Path]::GetExtension($Baseline) -ne '.json') { throw 'InvalidBaseline: expected a JSON path.' }
+    $projects = @([regex]::Matches((Get-SourceText $solution), '(?m)^\s*Project\([^)]*\)\s*=\s*"[^"]+",\s*"(?<path>[^"]+\.csproj)"') | ForEach-Object { $_.Groups['path'].Value })
+    if ($projects.Count -eq 0) { throw 'NoProjects: no C# projects are listed in the solution.' }
+    [array]::Sort($projects, [StringComparer]::Ordinal)
+    $groups = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
+    $checked = 0
+    foreach ($project in $projects) {
+        $projectPath = Get-SafeRepositoryPath (Join-Path ([IO.Path]::GetDirectoryName($solution)) $project)
+        if (-not [IO.File]::Exists($projectPath)) { throw 'MissingProject: solution project does not exist.' }
+        $projectName = $projectPath.Substring($script:rootPrefix.Length).Replace([char]92, [char]47)
+        if ($projectName -match '(^|/)(docs|\.ccg|\.worktrees|\.\u5176\u4ED6\u89E3\u6C7A\u65B9\u6848)(/|$)') { continue }
+        [string[]]$files = @(Get-ProjectSourceFiles ([IO.Path]::GetDirectoryName($projectPath)))
+        [array]::Sort($files, [StringComparer]::Ordinal)
+        foreach ($file in $files) {
+            $safePath = Get-SafeRepositoryPath $file
+            $relative = $safePath.Substring($script:rootPrefix.Length).Replace([char]92, [char]47)
+            $source = Get-SourceText $safePath
+            $normalized = [regex]::Replace($source.Replace("`r`n", "`n"), '(?m)^[ \t]*//[^\n]*(\n|$)', '')
+            $normalized = [regex]::Replace($normalized, '\s+', ' ').Trim()
+            $hash = Get-TextSha256 -Bytes $script:utf8.GetBytes($normalized)
+            $identities = @('content|' + $hash) + @(Get-SourceTypeNames $source | ForEach-Object { 'type|' + $_ })
+            foreach ($identity in $identities) {
+                if (-not $groups.ContainsKey($identity)) { $groups[$identity] = New-Object 'Collections.Generic.List[object]' }
+                $groups[$identity].Add(@{ Project = $projectName; Location = ($projectName + '|' + $relative) })
+            }
+            $checked++
+        }
+    }
+    if ($checked -eq 0) { throw 'NoSources: refusing an empty duplicate scan.' }
+    $entries = New-Object 'Collections.Generic.List[object]'
+    [string[]]$groupKeys = @($groups.Keys)
+    [array]::Sort($groupKeys, [StringComparer]::Ordinal)
+    foreach ($identity in $groupKeys) {
+        $members = @($groups[$identity].ToArray())
+        if (@($members | ForEach-Object { $_.Project } | Select-Object -Unique).Count -lt 2) { continue }
+        [string[]]$locations = @($members | ForEach-Object { $_.Location } | Select-Object -Unique)
+        [array]::Sort($locations, [StringComparer]::Ordinal)
+        $split = $identity.IndexOf('|')
+        $entries.Add([ordered]@{ kind = $identity.Substring(0, $split); key = $identity.Substring($split + 1); locations = $locations })
+    }
+    $created = -not [IO.File]::Exists($Baseline)
+    if ($created -or $WriteBaseline) {
+        $document = [ordered]@{ schemaVersion = 1; solution = $solution.Substring($script:rootPrefix.Length).Replace([char]92, [char]47); entries = @($entries.ToArray()) }
+        $json = ($document | ConvertTo-Json -Depth 8) -replace "`r?`n", "`r`n"
+        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Baseline))
+        [IO.File]::WriteAllText($Baseline, $json + "`r`n", (New-Object Text.UTF8Encoding($false)))
+    }
+    $baselineDocument = [IO.File]::ReadAllText($Baseline, $script:utf8) | ConvertFrom-Json
+    if ($baselineDocument.schemaVersion -ne 1) { throw 'InvalidBaseline: unsupported schema version.' }
+    $allowed = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
+    foreach ($entry in @($baselineDocument.entries)) {
+        $identity = $entry.kind + '|' + $entry.key
+        if ($allowed.ContainsKey($identity)) { throw 'DuplicateBaselineIdentity: baseline groups must have unique identities.' }
+        $seenLocations = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
+        foreach ($location in @($entry.locations)) {
+            if ([string]::IsNullOrWhiteSpace($location)) { throw 'InvalidBaseline: empty source location.' }
+            if (-not $seenLocations.Add($location)) { throw 'DuplicateBaselineLocation: baseline locations must be unique.' }
+        }
+        $allowed[$identity] = @($entry.locations)
+    }
+    $new = 0
+    $current = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
+    foreach ($entry in $entries) {
+        $identity = $entry.kind + '|' + $entry.key
+        $current[$identity] = @($entry.locations)
+        foreach ($location in @($entry.locations)) {
+            if (-not $allowed.ContainsKey($identity) -or $allowed[$identity] -cnotcontains $location) {
+                $new++
+                Write-Output ('FAIL NewDuplicate: ' + $entry.kind + ' ' + $entry.key + ' [' + $location + ']')
+            }
+        }
+    }
+    $stale = 0
+    foreach ($identity in $allowed.Keys) {
+        foreach ($location in @($allowed[$identity])) {
+            if (-not $current.ContainsKey($identity) -or $current[$identity] -cnotcontains $location) {
+                $stale++
+                Write-Output ('WARN BaselineStale: ' + $identity + ' [' + $location + ']')
+            }
+        }
+    }
+    Write-Output ('Duplicate scan: projects=' + $projects.Count + ' files=' + $checked + ' groups=' + $entries.Count + ' new=' + $new + ' stale=' + $stale + ' baselineCreated=' + $created)
+    if ($new -gt 0) { exit 1 }
+    exit 0
+}
+catch { Write-Output ('FAIL Duplicate scan: ' + $_.Exception.Message); exit 1 }

diff --git a/tools/RepoQuality/Test-AnalyzerProbe.ps1 b/tools/RepoQuality/Test-AnalyzerProbe.ps1
new file mode 100644
index 000000000..c3cb7ad28
--- /dev/null
+++ b/tools/RepoQuality/Test-AnalyzerProbe.ps1
@@ -0,0 +1,235 @@
+<#
+.SYNOPSIS
+以真實建置驗證禁用時間 API 與嚴格模式的載入時點。
+.DESCRIPTION
+預設探測分支故意違規，必須只出現 RS0030；文件與其他故障分支依各自的實際診斷判斷。
+不以命令列強制啟用嚴格模式；同時評估新舊專案屬性，防止 props 載入順序造成假綠燈。
+子程序與重新導向串流由本腳本每次呼叫擁有；停用建置伺服器重用，逾時終止程序樹。
+輸出只存於呼叫者指定的證據目錄，預設放在被忽略的 artifacts，不保存使用者或教會狀態。
+.PARAMETER RepositoryPath
+本工作樹根目錄，預設為腳本上兩層。
+.PARAMETER EvidenceDirectory
+保存建置原始輸出及精簡結果的目錄；每次呼叫由呼叫者決定生命週期。
+#>
+[CmdletBinding()]
+param([string]$RepositoryPath, [string]$EvidenceDirectory)
+
+Set-StrictMode -Version Latest
+$ErrorActionPreference = 'Stop'
+
+function Invoke-AnalyzerDotNet {
+    <#
+    .SYNOPSIS
+    執行單次有界的 dotnet 檢查並回傳原始結果；非零結束碼交由測試判斷。
+    .DESCRIPTION
+    同時讀取兩個輸出串流避免互相阻塞；最長三分鐘，逾時先終止子程序樹再釋放擁有者。
+    #>
+    param([string]$Arguments, [string]$LogName)
+    $start = New-Object System.Diagnostics.ProcessStartInfo
+    $start.FileName = 'dotnet'
+    $start.Arguments = $Arguments
+    $start.WorkingDirectory = $root
+    $start.UseShellExecute = $false
+    $start.CreateNoWindow = $true
+    $start.RedirectStandardOutput = $true
+    $start.RedirectStandardError = $true
+    $start.StandardOutputEncoding = $utf8
+    $start.StandardErrorEncoding = $utf8
+    $start.EnvironmentVariables['DOTNET_CLI_UI_LANGUAGE'] = 'en-US'
+    $start.EnvironmentVariables['VSLANG'] = '1033'
+    $process = New-Object System.Diagnostics.Process
+    $process.StartInfo = $start
+    try {
+        [void]$process.Start()
+        $stdout = $process.StandardOutput.ReadToEndAsync()
+        $stderr = $process.StandardError.ReadToEndAsync()
+        if (-not $process.WaitForExit(180000)) {
+            & taskkill.exe /PID $process.Id /T /F *> $null
+            if (-not $process.WaitForExit(10000)) { $process.Kill() }
+            throw 'Analyzer dotnet probe timed out.'
+        }
+        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
+        [System.IO.File]::WriteAllText((Join-Path $EvidenceDirectory $LogName), $output, $utf8)
+        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output }
+    }
+    finally { $process.Dispose() }
+}
+
+try {
+    if (-not $RepositoryPath) { $RepositoryPath = Join-Path $PSScriptRoot '..\..' }
+    $root = (Resolve-Path -LiteralPath $RepositoryPath).Path
+    if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $root 'artifacts/quality/analyzer-probe' }
+    [void][System.IO.Directory]::CreateDirectory($EvidenceDirectory)
+    $EvidenceDirectory = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
+    $utf8 = New-Object System.Text.UTF8Encoding($false)
+    $failures = New-Object 'System.Collections.Generic.List[string]'
+    $probe = 'tools/RepoQuality/AnalyzerProbe/AnalyzerProbe.csproj'
+    $build = Invoke-AnalyzerDotNet -Arguments ('build "' + $probe + '" -c Release -f net10.0 --nologo --disable-build-servers -t:Rebuild') -LogName 'probe-build.log'
+    $errorCodes = @([regex]::Matches($build.Output, '\berror ([A-Z]+[0-9]+):') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
+    if ($build.ExitCode -eq 0 -or $errorCodes.Count -ne 1 -or $errorCodes[0] -ne 'RS0030') {
+        $failures.Add("BannedTimeApi_MustFailWithRS0030: exit=$($build.ExitCode) errors=$($errorCodes -join ',')")
+    }
+    $properties = 'SpeechMessageStrictCode,EnableNETAnalyzers,AnalysisMode,AnalysisLevel,EffectiveAnalysisLevel,TreatWarningsAsErrors,Nullable,LangVersion,GenerateDocumentationFile,DocumentationFile,TargetDir,WarningsAsErrors'
+    $strict = Invoke-AnalyzerDotNet -Arguments ('msbuild "' + $probe + '" -nologo -p:TargetFramework=net10.0 -p:Configuration=Release -getProperty:' + $properties) -LogName 'strict-properties.log'
+    if ($strict.ExitCode -ne 0) { throw 'Strict project property evaluation failed.' }
+    $settings = ($strict.Output | ConvertFrom-Json).Properties
+    if ($settings.SpeechMessageStrictCode -ne 'true' -or $settings.EnableNETAnalyzers -ne 'true' -or
+        $settings.AnalysisMode -ne 'Recommended' -or $settings.TreatWarningsAsErrors -ne 'true' -or
+        $settings.Nullable -ne 'enable' -or $settings.LangVersion -ne 'latest' -or
+        $settings.GenerateDocumentationFile -ne 'true' -or [string]::IsNullOrWhiteSpace($settings.DocumentationFile) -or
+        $settings.WarningsAsErrors -notmatch 'CA1307' -or
+        $settings.WarningsAsErrors -notmatch 'CA1309' -or $settings.WarningsAsErrors -notmatch 'CA1310') {
+        $failures.Add('StrictOptIn_ProjectBody_EnablesAllRequiredSettings: effective properties do not match.')
+    }
+    $legacy = Invoke-AnalyzerDotNet -Arguments ('msbuild ToolUtility/ToolUtility.csproj -nologo -getProperty:' + $properties) -LogName 'legacy-properties.log'
+    if ($legacy.ExitCode -ne 0) { throw 'Legacy project property evaluation failed.' }
+    $old = ($legacy.Output | ConvertFrom-Json).Properties
+    if ($old.EnableNETAnalyzers -ne 'false' -or $old.AnalysisMode -ne 'None' -or
+        $old.TreatWarningsAsErrors -ne 'false' -or $old.Nullable -ne 'disable') {
+        $failures.Add('LegacyProject_NoOptIn_PreservesExistingSettings: effective properties changed.')
+    }
+    $checks = 3
+    # 還原階段兩個嚴格目標都必須稽核轉移相依；net48 預設只檢查直接相依，會漏掉傳遞套件弱點。
+    foreach ($framework in @('net48', 'net10.0')) {
+        $audit = Invoke-AnalyzerDotNet -Arguments ('msbuild "' + $probe + '" -nologo -p:TargetFramework=' + $framework + ' -getProperty:NuGetAuditMode,TargetFramework') -LogName ($framework + '-nuget-audit-mode.log')
+        $checks++
+        if ($audit.ExitCode -ne 0) { throw "NuGet audit property evaluation failed for $framework." }
+        $auditMode = ($audit.Output | ConvertFrom-Json).Properties.NuGetAuditMode
+        if ($auditMode -ne 'all') {
+            $failures.Add("StrictNuGetAudit_${framework}_MustAuditTransitive: mode=$auditMode")
+        }
+    }
+    # 必須查實際生效層級，而非只看 Recommended 字串；net48 的 SDK 不會自動補此屬性。
+    foreach ($framework in @('net48', 'net10.0')) {
+        $level = Invoke-AnalyzerDotNet -Arguments ('msbuild "' + $probe + '" -nologo -p:TargetFramework=' + $framework + ' -getProperty:AnalysisLevel,EffectiveAnalysisLevel') -LogName ($framework + '-analysis-level.log')
+        $checks++
+        if ($level.ExitCode -ne 0) { throw 'Analysis level property evaluation failed.' }
+        $effective = ($level.Output | ConvertFrom-Json).Properties
+        if ([string]::IsNullOrWhiteSpace($effective.AnalysisLevel) -or [string]::IsNullOrWhiteSpace($effective.EffectiveAnalysisLevel)) {
+            $failures.Add("EffectiveAnalysisLevel_${framework}_MustNotBeEmpty: analysis=$($effective.AnalysisLevel) effective=$($effective.EffectiveAnalysisLevel)")
+        }
+    }
+    foreach ($symbol in @('PROBE_TODAY', 'PROBE_LOCAL_TIME', 'PROBE_TIME_ZONE', 'PROBE_CULTURE', 'PROBE_UI_CULTURE')) {
+        $result = Invoke-AnalyzerDotNet -Arguments ('build "' + $probe + '" -c Release -f net10.0 --nologo --disable-build-servers -t:Rebuild -p:DefineConstants=' + $symbol) -LogName ($symbol + '.log')
+        $checks++
+        if ($result.ExitCode -eq 0 -or $result.Output -notmatch '\berror RS0030:') {
+            $failures.Add("BannedApi_${symbol}_FailsWithRS0030: exit=$($result.ExitCode)")
+        }
+    }
+    foreach ($case in @(@{ Symbol = 'PROBE_COMPARISON'; Codes = @('CA1309') }, @{ Symbol = 'PROBE_SEARCH'; Codes = @('CA1310') }, @{ Symbol = 'PROBE_ORDINAL_OVERLOAD'; Codes = @('CA1307') })) {
+        $result = Invoke-AnalyzerDotNet -Arguments ('build "' + $probe + '" -c Release -f net10.0 --nologo --disable-build-servers -t:Rebuild -p:DefineConstants=' + $case.Symbol) -LogName ($case.Symbol + '.log')
+        $checks++
+        foreach ($code in $case.Codes) {
+            if ($result.ExitCode -eq 0 -or $result.Output -notmatch ("\berror " + $code + ':')) {
+                $failures.Add("StringComparison_$($case.Symbol)_FailsWith${code}: exit=$($result.ExitCode)")
+            }
+        }
+    }
+    $compatibility = Invoke-AnalyzerDotNet -Arguments ('build "' + $probe + '" -c Release -f net48 --nologo --disable-build-servers -t:Rebuild') -LogName 'net48.log'
+    $checks++
+    $compatibilityCodes = @([regex]::Matches($compatibility.Output, '\berror ([A-Z]+[0-9]+):') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
+    if ($compatibility.ExitCode -eq 0 -or $compatibilityCodes.Count -ne 1 -or $compatibilityCodes[0] -ne 'RS0030') {
+        $failures.Add("Net48_PolyfillsCompile_OnlyBannedApiFails: exit=$($compatibility.ExitCode) errors=$($compatibilityCodes -join ',')")
+    }
+    # 用編譯器真正的缺漏診斷驗證文件產生，不能僅相信 GenerateDocumentationFile 開關值。
+    $missingXml = Invoke-AnalyzerDotNet -Arguments ('build "' + $probe + '" -c Release -f net10.0 --nologo --disable-build-servers -t:Rebuild -p:DefineConstants=PROBE_MISSING_XML') -LogName 'missing-xml.log'
+    $checks++
+    if ($missingXml.ExitCode -eq 0 -or $missingXml.Output -notmatch '\berror CS1591:') {
+        $failures.Add("PublicMember_NoXmlComment_FailsWithCS1591: exit=$($missingXml.ExitCode)")
+    }
+    # 先移除舊產物，再用完全有文件且無禁用 API 的分支建置，證明本次真的複製 XML 到輸出目錄。
+    $xmlOutput = Join-Path $settings.TargetDir 'AnalyzerProbe.xml'
+    [System.IO.File]::Delete($xmlOutput)
+    $documented = Invoke-AnalyzerDotNet -Arguments ('build "' + $probe + '" -c Release -f net10.0 --nologo --disable-build-servers -t:Rebuild -p:DefineConstants=PROBE_DOCUMENTED') -LogName 'documented-output.log'
+    $checks++
+    if ($documented.ExitCode -ne 0 -or -not [System.IO.File]::Exists($xmlOutput)) {
+        $failures.Add("DocumentedBuild_OutputContainsXml: exit=$($documented.ExitCode) exists=$([System.IO.File]::Exists($xmlOutput))")
+    }
+    else {
+        $xml = New-Object System.Xml.XmlDocument
+        $xml.XmlResolver = $null
+        $xml.Load($xmlOutput)
+        if (-not $xml.SelectSingleNode("/doc/members/member[@name='T:SpeechMessage.RepoQuality.AnalyzerProbe.ClockProbe']")) {
+            $failures.Add('DocumentedBuild_OutputContainsXml: output does not document the probe type.')
+        }
+    }
+    # 每次呼叫擁有獨立 Plugin 名稱的編譯樣本，測試命名空間禁令後才刪除；不與並行呼叫共用可變目錄。
+    $fixtureBase = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts/quality/analyzer-probe-fixtures'))
+    $fixtureDirectory = Join-Path $fixtureBase ([guid]::NewGuid().ToString('N'))
+    [void][System.IO.Directory]::CreateDirectory($fixtureDirectory)
+    try {
+        $sourcePath = [System.Security.SecurityElement]::Escape((Join-Path $root 'tools/RepoQuality/AnalyzerProbe/ClockProbe.cs'))
+        $fixtureProject = Join-Path $fixtureDirectory 'SpeechMessage.Dynamics.Plugins.csproj'
+        $fixtureXml = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><SpeechMessageStrictCode>true</SpeechMessageStrictCode><GenerateDocumentationFile>true</GenerateDocumentationFile><DefineConstants>PROBE_DRAWING</DefineConstants></PropertyGroup><ItemGroup><Compile Include="' + $sourcePath + '" /></ItemGroup></Project>'
+        [System.IO.File]::WriteAllText($fixtureProject, $fixtureXml + "`r`n", $utf8)
+        $drawing = Invoke-AnalyzerDotNet -Arguments ('build "' + $fixtureProject + '" -c Release --nologo --disable-build-servers') -LogName 'plugin-drawing.log'
+        $checks++
+        if ($drawing.ExitCode -eq 0 -or $drawing.Output -notmatch '\berror RS0030:.*System.Drawing') {
+            $failures.Add("Plugin_SystemDrawing_FailsWithRS0030: exit=$($drawing.ExitCode)")
+        }
+        $missingProject = Join-Path $fixtureDirectory 'StrictWithoutDocumentation.csproj'
+        $missingProjectXml = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><SpeechMessageStrictCode>true</SpeechMessageStrictCode><DefineConstants>PROBE_DOCUMENTED</DefineConstants></PropertyGroup><ItemGroup><Compile Include="' + $sourcePath + '" /></ItemGroup></Project>'
+        [System.IO.File]::WriteAllText($missingProject, $missingProjectXml + "`r`n", $utf8)
+        $missingSetting = Invoke-AnalyzerDotNet -Arguments ('build "' + $missingProject + '" -c Release --nologo --disable-build-servers -t:Rebuild') -LogName 'missing-documentation-setting.log'
+        $checks++
+        if ($missingSetting.ExitCode -eq 0 -or $missingSetting.Output -notmatch '\berror SMQ0001:') {
+            $failures.Add("StrictProject_NoDocumentationSetting_FailsBeforeCompile: exit=$($missingSetting.ExitCode)")
+        }
+        # 三項使用者指定案例加上「只有名稱後綴」的反例，證明 IsTestProject 也不可省略。
+        # 每案獨佔來源輸出目錄，避免相同 obj 檔讓不同條件互相污染；命令結束後統一清理。
+        $namingSource = [System.Security.SecurityElement]::Escape((Join-Path $root 'tools/RepoQuality/AnalyzerProbe/NamingProbe.cs'))
+        $namingCases = @(
+            @{ Name = 'NamingProduct'; IsTest = 'false'; Allowed = $false },
+            @{ Name = 'NamingProbe.Tests'; IsTest = 'true'; Allowed = $true },
+            @{ Name = 'PretendProduct'; IsTest = 'true'; Allowed = $false },
+            @{ Name = 'SuffixOnly.Tests'; IsTest = 'false'; Allowed = $false }
+        )
+        foreach ($case in $namingCases) {
+            $caseDirectory = Join-Path $fixtureDirectory $case.Name
+            [void][System.IO.Directory]::CreateDirectory($caseDirectory)
+            $caseProject = Join-Path $caseDirectory ($case.Name + '.csproj')
+            $caseXml = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><SpeechMessageStrictCode>true</SpeechMessageStrictCode><IsTestProject>' + $case.IsTest + '</IsTestProject><GenerateDocumentationFile>true</GenerateDocumentationFile><DefineConstants>PROBE_NAMING</DefineConstants></PropertyGroup><ItemGroup><Compile Include="' + $namingSource + '" /></ItemGroup></Project>'
+            [System.IO.File]::WriteAllText($caseProject, $caseXml + "`r`n", $utf8)
+            $naming = Invoke-AnalyzerDotNet -Arguments ('build "' + $caseProject + '" -c Release --nologo --disable-build-servers -t:Rebuild') -LogName ($case.Name + '.log')
+            $checks++
+            $namingCodes = @([regex]::Matches($naming.Output, '\berror ([A-Z]+[0-9]+):') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
+            if ($case.Allowed) {
+                if ($naming.ExitCode -ne 0) { $failures.Add("Naming_QualifiedTest_MustBuild: exit=$($naming.ExitCode) errors=$($namingCodes -join ',')") }
+            }
+            elseif ($naming.ExitCode -eq 0 -or $namingCodes.Count -ne 1 -or $namingCodes[0] -ne 'CA1707') {
+                $failures.Add("Naming_$($case.Name)_MustFailOnlyCA1707: exit=$($naming.ExitCode) errors=$($namingCodes -join ',')")
+            }
+        }
+        # 兩個新 net48 樣本必須靠一般 Recommended 規則失敗，不是靠強制的時間／文化禁令。
+        $interfaceSource = [System.Security.SecurityElement]::Escape((Join-Path $root 'tools/RepoQuality/AnalyzerProbe/InterfaceNamingProbe.cs'))
+        foreach ($case in @(
+            @{ Name = 'Net48NamingProduct'; Source = $namingSource; Symbol = 'PROBE_NAMING'; Code = 'CA1707' },
+            @{ Name = 'Net48InterfaceProduct'; Source = $interfaceSource; Symbol = 'PROBE_INTERFACE_KEYWORD'; Code = 'CA1716' }
+        )) {
+            $caseDirectory = Join-Path $fixtureDirectory $case.Name
+            [void][System.IO.Directory]::CreateDirectory($caseDirectory)
+            $caseProject = Join-Path $caseDirectory ($case.Name + '.csproj')
+            $caseXml = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><SpeechMessageStrictCode>true</SpeechMessageStrictCode><GenerateDocumentationFile>true</GenerateDocumentationFile><DefineConstants>' + $case.Symbol + '</DefineConstants></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" /><Compile Include="' + $case.Source + '" /></ItemGroup></Project>'
+            [System.IO.File]::WriteAllText($caseProject, $caseXml + "`r`n", $utf8)
+            $result = Invoke-AnalyzerDotNet -Arguments ('build "' + $caseProject + '" -c Release --nologo --disable-build-servers -t:Rebuild') -LogName ($case.Name + '.log')
+            $checks++
+            $codes = @([regex]::Matches($result.Output, '\berror ([A-Z]+[0-9]+):') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
+            if ($result.ExitCode -eq 0 -or $codes.Count -ne 1 -or $codes[0] -ne $case.Code) {
+                $failures.Add("Recommended_$($case.Name)_MustFailOnly$($case.Code): exit=$($result.ExitCode) errors=$($codes -join ',')")
+            }
+        }
+    }
+    finally {
+        $resolvedFixture = [System.IO.Path]::GetFullPath($fixtureDirectory)
+        $allowedPrefix = $fixtureBase.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
+        if (-not $resolvedFixture.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
+            throw 'Refusing to clean a fixture outside its owned directory.'
+        }
+        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
+    }
+    foreach ($failure in $failures) { Write-Output "FAIL $failure" }
+    Write-Output "Analyzer probe: checks=$checks failures=$($failures.Count) buildExit=$($build.ExitCode) errors=$($errorCodes -join ',')"
+    if ($failures.Count -gt 0) { exit 1 }
+    exit 0
+}
+catch { Write-Output "FAIL Analyzer probe: $($_.Exception.Message)"; exit 1 }

diff --git a/tools/RepoQuality/Test-Coverage.ps1 b/tools/RepoQuality/Test-Coverage.ps1
new file mode 100644
index 000000000..d87749c79
--- /dev/null
+++ b/tools/RepoQuality/Test-Coverage.ps1
@@ -0,0 +1,37 @@
+<#
+.SYNOPSIS
+各專案各框架獨立檢查 Coverlet 的模組行覆蓋率，門檻固定為 80%。
+.DESCRIPTION
+呼叫端必須提供當次測試的單一 Cobertura 報告，不合併不同框架或不同測試專案的結果。
+缺少報告、模組重複、無效比例均拒絕；比例採固定文化解析，判斷前不四捨五入。
+XML 禁用 DTD 與外部實體，最多 32 MiB 字元；reader 由單次呼叫擁有並在解析後確定釋放。
+不連外、不保存任何使用者或教會資料，不輸出解析例外中的檔案內容。
+#>
+[CmdletBinding()]
+param([Parameter(Mandatory=$true)][string]$ReportPath, [Parameter(Mandatory=$true)][string]$Module)
+Set-StrictMode -Version Latest
+$ErrorActionPreference = 'Stop'
+if (-not [IO.File]::Exists($ReportPath)) { Write-Output 'Coverage report is missing.'; exit 1 }
+try {
+    $settings = New-Object Xml.XmlReaderSettings
+    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
+    $settings.XmlResolver = $null
+    $settings.MaxCharactersInDocument = 32 * 1024 * 1024
+    $document = New-Object Xml.XmlDocument
+    $document.XmlResolver = $null
+    $reader = [Xml.XmlReader]::Create([IO.Path]::GetFullPath($ReportPath), $settings)
+    try { $document.Load($reader) } finally { $reader.Dispose() }
+} catch { Write-Output 'Coverage report is invalid.'; exit 1 }
+$packages = @($document.SelectNodes('/coverage/packages/package') | Where-Object {
+    [string]::Equals($_.GetAttribute('name'), $Module, [StringComparison]::Ordinal)
+})
+if ($packages.Count -ne 1) { Write-Output 'Expected exactly one required module.'; exit 1 }
+$rate = [decimal]0
+if (-not [decimal]::TryParse($packages[0].GetAttribute('line-rate'), [Globalization.NumberStyles]::AllowDecimalPoint,
+        [Globalization.CultureInfo]::InvariantCulture, [ref]$rate) -or $rate -lt 0 -or $rate -gt 1) {
+    Write-Output 'Coverage rate is invalid.'
+    exit 1
+}
+if ($rate -lt [decimal]0.8) { Write-Output 'Coverage below 80 percent.'; exit 1 }
+Write-Output ('Coverage ' + $Module + ': ' + ($rate * 100).ToString('F2', [Globalization.CultureInfo]::InvariantCulture) + '%')
+exit 0

diff --git a/tools/RepoQuality/Test-PackageVersions.ps1 b/tools/RepoQuality/Test-PackageVersions.ps1
new file mode 100644
index 000000000..5715c007c
--- /dev/null
+++ b/tools/RepoQuality/Test-PackageVersions.ps1
@@ -0,0 +1,145 @@
+<#
+.SYNOPSIS
+檢查實際專案的中央套件版本契約，拒絕內嵌版本與套件降版警告抑制。
+.DESCRIPTION
+以 Git 的已追蹤與未忽略新檔清單為界，不走訪父工作樹、建置產物或參考舊方案。
+文件快照與 CCG 任務封存不在套件管理範圍；XML 註解也不是有效依賴。
+僅唯讀解析專案，不載入或執行 MSBuild、不還原套件、不改寫舊編碼。
+舊 Trace 專案尚有 CP950 註解，因此只在嚴格 UTF-8 解碼失敗後唯讀使用 CP950；
+編碼合法性由專用編碼守門負責，本檢查不會以替代字元掩蓋解碼錯誤。
+Git 程序與重新導向串流由單次呼叫擁有；三十秒內須完成，逾時終止並確定釋放。
+.PARAMETER RepositoryPath
+要檢查的工作樹根目錄；預設為本腳本上兩層，測試可指定隔離的暫存 Git 目錄。
+#>
+[CmdletBinding()]
+param(
+    [string]$RepositoryPath
+)
+
+Set-StrictMode -Version Latest
+$ErrorActionPreference = 'Stop'
+
+try {
+    if (-not $RepositoryPath) { $RepositoryPath = Join-Path $PSScriptRoot '..\..' }
+    $root = (Resolve-Path -LiteralPath $RepositoryPath).Path
+    $utf8 = New-Object System.Text.UTF8Encoding($false, $true)
+    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
+    $startInfo.FileName = 'git'
+    $startInfo.Arguments = '-c core.longpaths=true -c core.quotepath=false ls-files --cached --others --exclude-standard -z -- "*.csproj"'
+    $startInfo.WorkingDirectory = $root
+    $startInfo.UseShellExecute = $false
+    $startInfo.CreateNoWindow = $true
+    $startInfo.RedirectStandardOutput = $true
+    $startInfo.RedirectStandardError = $true
+    $startInfo.StandardOutputEncoding = $utf8
+    $startInfo.StandardErrorEncoding = $utf8
+    $process = New-Object System.Diagnostics.Process
+    $process.StartInfo = $startInfo
+    try {
+        [void]$process.Start()
+        $stdout = $process.StandardOutput.ReadToEndAsync()
+        $stderr = $process.StandardError.ReadToEndAsync()
+        if (-not $process.WaitForExit(30000)) {
+            $process.Kill()
+            $process.WaitForExit()
+            throw 'Git project enumeration timed out.'
+        }
+        $fileList = $stdout.GetAwaiter().GetResult()
+        $gitError = $stderr.GetAwaiter().GetResult()
+        if ($process.ExitCode -ne 0) { throw "Git project enumeration failed: $gitError" }
+    }
+    finally {
+        $process.Dispose()
+    }
+
+    $failures = New-Object 'System.Collections.Generic.List[string]'
+    $checked = 0
+    $references = 0
+    $excluded = 0
+    $files = @($fileList.Split([char]0) | Where-Object { $_ } | Sort-Object -Unique)
+    foreach ($relativePath in $files) {
+        if ($relativePath -match '^(docs/|\.ccg/tasks/)') { $excluded++; continue }
+        $path = Join-Path $root $relativePath
+        if (-not [System.IO.File]::Exists($path)) { continue }
+        $bytes = [System.IO.File]::ReadAllBytes($path)
+        try { $source = $utf8.GetString($bytes) }
+        catch [System.Text.DecoderFallbackException] {
+            $legacyEncoding = [System.Text.Encoding]::GetEncoding(
+                950, [System.Text.EncoderFallback]::ExceptionFallback,
+                [System.Text.DecoderFallback]::ExceptionFallback)
+            $source = $legacyEncoding.GetString($bytes)
+        }
+        $document = New-Object System.Xml.XmlDocument
+        $document.XmlResolver = $null
+        $document.LoadXml($source.TrimStart([char]0xFEFF))
+        $checked++
+        foreach ($reference in $document.SelectNodes("//*[local-name()='PackageReference']")) {
+            $references++
+            if ($reference.HasAttribute('Version') -or $reference.SelectSingleNode("*[local-name()='Version']")) {
+                $identity = $reference.GetAttribute('Include')
+                if (-not $identity) { $identity = $reference.GetAttribute('Update') }
+                $failures.Add("InlineVersion: $relativePath [$identity]")
+            }
+        }
+        foreach ($suppression in $document.SelectNodes("//*[local-name()='NoWarn'] | //@NoWarn")) {
+            if ($suppression.InnerText -match '(?i)(^|[;,\s])NU1605($|[;,\s])') {
+                $failures.Add("SuppressedNU1605: $relativePath")
+            }
+        }
+    }
+    $centralPath = Join-Path $root 'Directory.Packages.props'
+    if (-not [System.IO.File]::Exists($centralPath)) { throw 'Central package version table is missing.' }
+    $central = New-Object System.Xml.XmlDocument
+    $central.XmlResolver = $null
+    $central.Load($centralPath)
+    foreach ($package in @('System.Security.Cryptography.Xml', 'System.Security.Cryptography.Pkcs')) {
+        $version = $central.SelectSingleNode("//*[local-name()='PackageVersion' and @Include='$package']")
+        if ($null -eq $version -or $version.GetAttribute('Version') -ne '10.0.14') {
+            $failures.Add("CryptographySecurityFix: Directory.Packages.props [$package] must be 10.0.14")
+        }
+    }
+    $connectionProjectPath = Join-Path $root 'PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj'
+    if (-not [System.IO.File]::Exists($connectionProjectPath)) { throw 'On-premises connection project is missing.' }
+    $connectionProject = New-Object System.Xml.XmlDocument
+    $connectionProject.XmlResolver = $null
+    $connectionProject.Load($connectionProjectPath)
+    foreach ($package in @('System.Security.Cryptography.Xml', 'System.Security.Cryptography.Pkcs')) {
+        $reference = $connectionProject.SelectSingleNode("//*[local-name()='PackageReference' and @Include='$package']")
+        if ($null -eq $reference -or $reference.HasAttribute('Version') -or
+            $reference.SelectSingleNode("*[local-name()='Version']")) {
+            $failures.Add("CryptographyDirectDependency: PowerPlatform.Dataverse.Client [$package] must directly reference its centrally managed version")
+        }
+    }
+    if ($central.SelectSingleNode("//*[local-name()='PropertyGroup']/*[local-name()='CentralPackageTransitivePinningEnabled' and translate(text(),'TRUE','true')='true']")) {
+        $failures.Add('CryptographyCentralPinning: CentralPackageTransitivePinningEnabled must not be enabled.')
+    }
+    $systemTextJsonVersion = $central.SelectSingleNode("//*[local-name()='PackageVersion' and @Include='System.Text.Json']")
+    if ($null -eq $systemTextJsonVersion -or $systemTextJsonVersion.GetAttribute('Version') -ne '6.0.11') {
+        $failures.Add('PluginJsonSecurityFix: Directory.Packages.props [System.Text.Json] must be 6.0.11')
+    }
+    foreach ($pluginProjectRelativePath in @(
+        'SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj',
+        'SpeechMessage.Dynamics.Plugins.Tests/SpeechMessage.Dynamics.Plugins.Tests.csproj'
+    )) {
+        $pluginProjectPath = Join-Path $root $pluginProjectRelativePath
+        if (-not [System.IO.File]::Exists($pluginProjectPath)) { throw "Dynamics plugin project is missing: $pluginProjectRelativePath" }
+        $pluginProject = New-Object System.Xml.XmlDocument
+        $pluginProject.XmlResolver = $null
+        $pluginProject.Load($pluginProjectPath)
+        $pluginJsonReference = $pluginProject.SelectSingleNode("//*[local-name()='PackageReference' and @Include='System.Text.Json']")
+        if ($null -eq $pluginJsonReference -or $pluginJsonReference.HasAttribute('Version') -or
+            $pluginJsonReference.SelectSingleNode("*[local-name()='Version']") -or
+            $pluginJsonReference.GetAttribute('PrivateAssets') -ne 'all') {
+            $failures.Add("PluginJsonPrivateReference: $pluginProjectRelativePath must directly reference centrally managed System.Text.Json with PrivateAssets=all")
+        }
+    }
+    foreach ($failure in $failures) { Write-Output "FAIL $failure" }
+    Write-Output "Package guard: projects=$checked references=$references excluded=$excluded failures=$($failures.Count)"
+    if ($checked -eq 0) { throw 'No program projects found; refusing an empty package check.' }
+    if ($failures.Count -gt 0) { exit 1 }
+    exit 0
+}
+catch {
+    Write-Output "FAIL Package guard: $($_.Exception.Message)"
+    exit 1
+}

diff --git a/tools/RepoQuality/Test-TextEncoding.ps1 b/tools/RepoQuality/Test-TextEncoding.ps1
new file mode 100644
index 000000000..c8de92600
--- /dev/null
+++ b/tools/RepoQuality/Test-TextEncoding.ps1
@@ -0,0 +1,84 @@
+<#
+.SYNOPSIS
+檢查差異檔案的 UTF-8 無 BOM、CRLF 與 PowerShell 非 ASCII 邊界，或唯讀產生全量盤點。
+.DESCRIPTION
+Changed 只採 git diff 的新增／修改／重新命名路徑；未追蹤新檔須先暫存，避免把本機診斷封包混入發布差異。
+All 只讀取此工作樹的 Git 追蹤檔，不沿 reparse point 讀取外部內容；只輸出舊問題報告。
+報告只保存路徑與原因，不保存來源文字、個資或憑證；每檔讀取完成即釋放區域參照。
+.PARAMETER Scope
+Changed 作為發布守門，All 僅供盤點。
+.PARAMETER Base
+Changed 的 Git 比對起點，不可省略；與階段分支起點比較。
+.PARAMETER RepositoryPath
+目前工作樹根目錄，預設為本腳本上兩層，不能指向父工作樹作為後備來源。
+.PARAMETER ReportPath
+可選的 Markdown 報告路徑；All 必須明確指定。
+#>
+[CmdletBinding()]
+param([ValidateSet('Changed','All')][string]$Scope = 'Changed', [string]$Base, [string]$RepositoryPath, [string]$ReportPath)
+
+Set-StrictMode -Version Latest
+$ErrorActionPreference = 'Stop'
+. (Join-Path $PSScriptRoot 'TextEncoding.Common.ps1')
+try {
+    if (-not $RepositoryPath) { $RepositoryPath = Join-Path $PSScriptRoot '..\..' }
+    $root = (Resolve-Path -LiteralPath $RepositoryPath).Path
+    $actualRoot = (Invoke-TextQualityGit -Root $root -Arguments @('rev-parse','--show-toplevel')).Trim()
+    if (-not [IO.Path]::GetFullPath($actualRoot).Equals([IO.Path]::GetFullPath($root), [StringComparison]::OrdinalIgnoreCase)) { throw 'RepositoryPath must identify this Git worktree root.' }
+    if ($Scope -eq 'Changed') {
+        if ([string]::IsNullOrWhiteSpace($Base) -or $Base.StartsWith('-')) { throw 'Changed scope requires a valid Base revision.' }
+        [string]$listing = Invoke-TextQualityGit -Root $root -Arguments @('diff','--name-only','--diff-filter=AMR','-z',$Base,'--')
+    }
+    else {
+        if (-not $ReportPath) { throw 'All scope requires ReportPath.' }
+        # All 只代表版控中的來源快照；未追蹤檔可能是外部工具輸出或機密，禁止被全量盤點吸入。
+        [string]$listing = Invoke-TextQualityGit -Root $root -Arguments @('ls-files','--cached','-z')
+    }
+    [string[]]$paths = @($listing.Split([char]0) | Where-Object { $_ })
+    [array]::Sort($paths, [StringComparer]::Ordinal)
+    $extensions = @('.cs','.cshtml','.csproj','.props','.targets','.json','.md','.ps1','.resx','.xml','.yml')
+    $violations = New-Object 'Collections.Generic.List[object]'
+    $checked = 0
+    $excluded = 0
+    foreach ($relative in $paths) {
+        $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()
+        if ($extensions -notcontains $extension) { continue }
+        # 即使誤被追蹤，也不讀取其他工作樹、參考舊方案或建置產物。
+        if ($relative -match '(^|/)(\.worktrees|\.\u5176\u4ED6\u89E3\u6C7A\u65B9\u6848|bin|obj)(/|$)') { $excluded++; continue }
+        # 僅排除具時間戳記的 CCG 原始執行目錄；手寫提示詞與審查包仍須接受編碼守門。
+        # 機器產出的 prompt/stdout/stderr 保留原始位元組，不以修格式改寫診斷證據。
+        if ($relative -match '^\.ccg/dual-model-runs/[0-9]{8}-[0-9]{6}-[^/]+/') { $excluded++; continue }
+        $path = Join-Path $root $relative
+        if (-not [IO.File]::Exists($path)) { continue }
+        $checked++
+        $rootPrefix = [IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
+        $safe = [IO.Path]::GetFullPath($path).StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)
+        $cursor = $root
+        foreach ($segment in $relative.Split([char]'/')) {
+            if (-not $safe) { break }
+            $cursor = Join-Path $cursor $segment
+            if (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $safe = $false }
+        }
+        if (-not $safe) { $issues = @('UnsafePath') }
+        else { $issues = @(Get-TextByteIssues -Bytes ([IO.File]::ReadAllBytes($path)) -Extension $extension) }
+        if ($issues.Count -gt 0) { $violations.Add([pscustomobject]@{ Path = $relative; Issues = $issues }) }
+    }
+    if ($ReportPath) {
+        $report = New-Object 'Collections.Generic.List[string]'
+        $report.Add('# Text encoding inventory')
+        $report.Add('')
+        $report.Add("Scope: $Scope; checked=$checked; excluded=$excluded; filesWithIssues=$($violations.Count).")
+        $report.Add('')
+        $report.Add('| File | Issues |')
+        $report.Add('| --- | --- |')
+        foreach ($violation in $violations) { $report.Add('| ' + $violation.Path.Replace('|','\|') + ' | ' + ($violation.Issues -join ', ') + ' |') }
+        $destination = [IO.Path]::GetFullPath($ReportPath)
+        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
+        [IO.File]::WriteAllText($destination, ($report -join "`r`n") + "`r`n", (New-Object Text.UTF8Encoding($false)))
+    }
+    if ($Scope -eq 'Changed') { foreach ($violation in $violations) { Write-Output ("FAIL $($violation.Path): " + ($violation.Issues -join ', ')) } }
+    Write-Output "Text encoding: scope=$Scope checked=$checked excluded=$excluded filesWithIssues=$($violations.Count)"
+    if ($Scope -eq 'Changed' -and $violations.Count -gt 0) { exit 1 }
+    exit 0
+}
+catch { Write-Output "FAIL Text encoding: $($_.Exception.Message)"; exit 1 }

diff --git a/tools/RepoQuality/Tests/Find-DuplicateCode.Tests.ps1 b/tools/RepoQuality/Tests/Find-DuplicateCode.Tests.ps1
new file mode 100644
index 000000000..84dadb5b4
--- /dev/null
+++ b/tools/RepoQuality/Tests/Find-DuplicateCode.Tests.ps1
@@ -0,0 +1,164 @@
+<#
+.SYNOPSIS
+以合成方案驗證跨專案副本、型別重名、基準棘輪及掃描邊界。
+.DESCRIPTION
+每案只擁有自己的暫存方案，不執行產品來源或連外；清理前確認絕對路徑。
+拒絕案例必須比對具體診斷，不接受任意解析失敗冒充安全拒絕。
+#>
+Describe 'Solution duplicate code contracts' {
+    $script:tool = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Find-DuplicateCode.ps1'))
+    $script:fixtureBase = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'Fixtures/.runtime'))
+    $script:utf8 = New-Object Text.UTF8Encoding($false, $true)
+    $script:engine = Join-Path $PSHOME 'powershell.exe'
+    if (-not [IO.File]::Exists($script:engine)) { $script:engine = Join-Path $PSHOME 'pwsh.exe' }
+    function Set-Source {
+        <# 寫入當案固定合成來源；不借用被測工具計算期望內容。 #>
+        param([string]$Relative, [string]$Content)
+        $path = Join-Path $script:caseRepo $Relative
+        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
+        [IO.File]::WriteAllText($path, $Content + "`r`n", $script:utf8)
+    }
+    function Set-Solution {
+        <# 只列指定的假專案，未列入的資料夾不得被走訪。 #>
+        param([string[]]$Projects = @('A','B'))
+        $lines = @($Projects | ForEach-Object { 'Project("{FAKE}") = "' + $_ + '", "' + $_ + '/' + $_ + '.csproj", "{' + $_ + '}"' })
+        [IO.File]::WriteAllText((Join-Path $script:caseRepo 'SpeechMessageProducts.sln'), ($lines -join "`r`n") + "`r`n", $script:utf8)
+        foreach ($project in $Projects) { Set-Source ($project + '/' + $project + '.csproj') '<Project Sdk="Microsoft.NET.Sdk" />' }
+    }
+    function Invoke-DuplicateScan {
+        <# 使用一次性子程序；等候完成後才查結果，不保留程序或串流。 #>
+        param([switch]$WriteBaseline)
+        $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$script:tool,'-RepositoryPath',$script:caseRepo,'-Baseline',$script:baseline)
+        if ($WriteBaseline) { $arguments += '-WriteBaseline' }
+        $output = & $script:engine @arguments 2>&1
+        return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
+    }
+    BeforeEach {
+        $script:caseDirectory = Join-Path $script:fixtureBase ([guid]::NewGuid().ToString('N'))
+        $script:caseRepo = Join-Path $script:caseDirectory 'repo'
+        $script:baseline = Join-Path $script:caseRepo 'baseline.json'
+        [void][IO.Directory]::CreateDirectory($script:caseRepo)
+        Set-Solution
+        Set-Source 'A/A.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
+        Set-Source 'B/B.cs' 'namespace N { internal class Beta { internal int Value = 2; } }'
+    }
+    AfterEach {
+        $actual = [IO.Path]::GetFullPath($script:caseDirectory)
+        $prefix = $script:fixtureBase.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
+        if (-not $actual.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
+        Remove-Item -LiteralPath $actual -Recurse -Force
+    }
+    It 'SameContent_DifferentProjects_IsRecorded' {
+        Set-Source 'B/B.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 0
+        $baseline = Get-Content -LiteralPath $script:baseline -Raw -Encoding UTF8 | ConvertFrom-Json
+        @($baseline.entries | Where-Object { $_.kind -eq 'content' }).Count | Should Be 1
+        [IO.File]::ReadAllText($script:baseline) | Should Not Match 'internal int Value'
+    }
+    It 'CommentsAndWhitespace_DifferentFormatting_IsRecorded' {
+        Set-Source 'A/A.cs' ("// explanation`r`nnamespace N {`r`n internal class Alpha { }`r`n}")
+        Set-Source 'B/B.cs' ("/// docs`r`nnamespace N {`r`n    internal class Alpha { }   `r`n}")
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 0
+        $baseline = Get-Content -LiteralPath $script:baseline -Raw -Encoding UTF8 | ConvertFrom-Json
+        @($baseline.entries | Where-Object { $_.kind -eq 'content' }).Count | Should Be 1
+    }
+    It 'ExistingBaseline_Duplicate_DoesNotFail' {
+        Set-Source 'B/B.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
+        (Invoke-DuplicateScan).ExitCode | Should Be 0
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 0
+        $result.Output | Should Match 'new=0'
+    }
+    It 'NewDuplicate_OutsideBaseline_Fails' {
+        (Invoke-DuplicateScan).ExitCode | Should Be 0
+        Set-Source 'B/B.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'NewDuplicate'
+    }
+    It 'ExistingGroup_AdditionalProject_Fails' {
+        Set-Source 'B/B.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
+        (Invoke-DuplicateScan).ExitCode | Should Be 0
+        Set-Solution @('A','B','C')
+        Set-Source 'C/C.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'NewDuplicate'
+    }
+    It 'ResolvedDuplicate_RemindsWithoutFailure' {
+        Set-Source 'B/B.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
+        (Invoke-DuplicateScan).ExitCode | Should Be 0
+        Set-Source 'B/B.cs' 'namespace N { internal class Beta { } }'
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 0
+        $result.Output | Should Match 'BaselineStale'
+    }
+    It 'SameNamespaceType_DifferentContent_IsRecorded' {
+        Set-Source 'B/B.cs' 'namespace N { internal class Alpha { internal int Value = 9; } }'
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 0
+        $baseline = Get-Content -LiteralPath $script:baseline -Raw -Encoding UTF8 | ConvertFrom-Json
+        @($baseline.entries | Where-Object { $_.kind -eq 'type' -and $_.key -eq 'N.Alpha' }).Count | Should Be 1
+        @($baseline.entries | Where-Object { $_.kind -eq 'content' }).Count | Should Be 0
+    }
+    It 'SameProjectOnly_Duplicates_AreNotRecorded' {
+        Set-Source 'A/Other.cs' 'namespace N { internal class Alpha { internal int Value = 1; } }'
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 0
+        $baseline = Get-Content -LiteralPath $script:baseline -Raw -Encoding UTF8 | ConvertFrom-Json
+        @($baseline.entries).Count | Should Be 0
+    }
+    It 'Scope_OutsideProjectsGeneratedAndExcludedPaths_AreNotRead' {
+        foreach ($file in @('Outside/old.cs','A/bin/old.cs','A/obj/old.cs','A/.worktrees/old.cs','A/docs/old.cs','A/.ccg/tasks/old.cs','A/old.g.cs','A/old.Designer.cs')) {
+            Set-Source $file 'namespace N { internal class Alpha { internal int Value = 1; } }'
+        }
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 0
+        $result.Output | Should Match 'files=2 '
+    }
+    It 'Scope_ProjectOutsideRepository_IsRejected' {
+        [IO.File]::WriteAllText((Join-Path $script:caseRepo 'SpeechMessageProducts.sln'), 'Project("{FAKE}") = "Other", "../other.csproj", "{OTHER}"', $script:utf8)
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'UnsafePath'
+    }
+    It 'Scope_Junction_IsNotTraversed' {
+        $external = Join-Path $script:caseDirectory 'external'
+        [void][IO.Directory]::CreateDirectory($external)
+        [IO.File]::WriteAllText((Join-Path $external 'foreign.cs'), 'namespace N { internal class Alpha { internal int Value = 1; } }', $script:utf8)
+        $link = Join-Path $script:caseRepo 'A/linked'
+        try {
+            [void](New-Item -ItemType Junction -Path $link -Target $external)
+            $result = Invoke-DuplicateScan
+            $result.ExitCode | Should Be 0
+            $result.Output | Should Match 'files=2 '
+        }
+        finally {
+            if ([IO.Path]::GetFullPath($link) -ne [IO.Path]::GetFullPath((Join-Path $script:caseRepo 'A/linked'))) { throw 'Unsafe junction cleanup path.' }
+            if ([IO.Directory]::Exists($link)) { [IO.Directory]::Delete($link) }
+        }
+    }
+    It 'Scope_EmptySolution_FailsInsteadOfFalseGreen' {
+        [IO.File]::WriteAllText((Join-Path $script:caseRepo 'SpeechMessageProducts.sln'), '', $script:utf8)
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'NoProjects'
+    }
+    # 基準本身也是具穩定識別的集合；重複群組或位置不可被字典覆寫後默默接受。
+    It 'Baseline_DuplicateGroupIdentity_IsRejected' {
+        $json = '{"schemaVersion":1,"entries":[{"kind":"type","key":"N.Alpha","locations":["A|A.cs"]},{"kind":"type","key":"N.Alpha","locations":["B|B.cs"]}]}'
+        [IO.File]::WriteAllText($script:baseline, $json, $script:utf8)
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'DuplicateBaselineIdentity'
+    }
+    It 'Baseline_DuplicateLocation_IsRejected' {
+        $json = '{"schemaVersion":1,"entries":[{"kind":"type","key":"N.Alpha","locations":["A|A.cs","A|A.cs"]}]}'
+        [IO.File]::WriteAllText($script:baseline, $json, $script:utf8)
+        $result = Invoke-DuplicateScan
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'DuplicateBaselineLocation'
+    }
+}

diff --git a/tools/RepoQuality/Tests/Test-Coverage.Tests.ps1 b/tools/RepoQuality/Tests/Test-Coverage.Tests.ps1
new file mode 100644
index 000000000..aeffcbc40
--- /dev/null
+++ b/tools/RepoQuality/Tests/Test-Coverage.Tests.ps1
@@ -0,0 +1,51 @@
+<#
+使用固定合成 Cobertura 資料驗證 80% 邊界、缺失／重複模組及損壞 XML 拒絕。
+每個案例用獨立檔案和子程序；XML 不可解析外部實體，finally 逐一刪除已知合成檔案。
+斷言結束碼與指定診斷，不把任意執行錯誤當成成功的負向測試。
+#>
+[CmdletBinding()]
+param()
+$ErrorActionPreference = 'Stop'
+$tool = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Test-Coverage.ps1'))
+$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
+$fixtureRoot = Join-Path $tempRoot ('speechmessage-coverage-' + [Guid]::NewGuid().ToString('N'))
+$null = New-Item -ItemType Directory -Path $fixtureRoot
+$utf8 = New-Object Text.UTF8Encoding($false)
+$package = '<package name="New.Module" line-rate="{0}" />'
+$cases = @(
+    @{ Name='Exactly80_Passes'; Xml=('<coverage><packages>' + ($package -f '0.80') + '</packages></coverage>'); Code=0; Expected='Coverage New.Module: 80.00%' },
+    @{ Name='Above80_Passes'; Xml=('<coverage><packages>' + ($package -f '0.95') + '</packages></coverage>'); Code=0; Expected='Coverage New.Module: 95.00%' },
+    @{ Name='Below80_Fails'; Xml=('<coverage><packages>' + ($package -f '0.79999') + '</packages></coverage>'); Code=1; Expected='Coverage below 80 percent.' },
+    @{ Name='WrongModule_Fails'; Xml='<coverage><packages><package name="Legacy" line-rate="1" /></packages></coverage>'; Code=1; Expected='Expected exactly one required module.' },
+    @{ Name='DuplicateModule_Fails'; Xml=('<coverage><packages>' + ($package -f '1') + ($package -f '1') + '</packages></coverage>'); Code=1; Expected='Expected exactly one required module.' },
+    @{ Name='MissingReport_Fails'; Xml=$null; Code=1; Expected='Coverage report is missing.' },
+    @{ Name='MalformedXml_Fails'; Xml='<coverage>'; Code=1; Expected='Coverage report is invalid.' },
+    @{ Name='ExternalEntity_Fails'; Xml='<!DOCTYPE coverage [<!ENTITY x SYSTEM "file:///never-read">]><coverage>&x;</coverage>'; Code=1; Expected='Coverage report is invalid.' },
+    @{ Name='NaN_Fails'; Xml=('<coverage><packages>' + ($package -f 'NaN') + '</packages></coverage>'); Code=1; Expected='Coverage rate is invalid.' },
+    @{ Name='Over100_Fails'; Xml=('<coverage><packages>' + ($package -f '1.01') + '</packages></coverage>'); Code=1; Expected='Coverage rate is invalid.' },
+    @{ Name='CommaRate_Fails'; Xml=('<coverage><packages>' + ($package -f '0,8') + '</packages></coverage>'); Code=1; Expected='Coverage rate is invalid.' }
+)
+$failures = 0
+try {
+    foreach ($case in $cases) {
+        $file = Join-Path $fixtureRoot ($case.Name + '.xml')
+        if ($null -ne $case.Xml) { [IO.File]::WriteAllText($file, $case.Xml, $utf8) }
+        $output = @(& powershell -NoProfile -File $tool -ReportPath $file -Module 'New.Module' 2>&1)
+        $code = $LASTEXITCODE
+        if ($code -eq $case.Code -and ($output -join "`n").Contains($case.Expected)) { Write-Output ('PASS ' + $case.Name) }
+        else { $failures++; Write-Output ('FAIL ' + $case.Name + ': coverage contract not satisfied') }
+    }
+} finally {
+    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
+    $boundary = $tempRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
+    if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase) -or
+        -not ([IO.Path]::GetFileName($resolved)).StartsWith('speechmessage-coverage-', [StringComparison]::Ordinal)) { throw 'Unsafe fixture cleanup.' }
+    foreach ($case in $cases) {
+        $file = Join-Path $resolved ($case.Name + '.xml')
+        if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
+    }
+    [IO.Directory]::Delete($resolved, $false)
+}
+Write-Output ('Coverage tests: total=' + $cases.Count + ' failed=' + $failures)
+if ($failures -gt 0) { exit 1 }
+exit 0

diff --git a/tools/RepoQuality/Tests/Test-P002Settings.ps1 b/tools/RepoQuality/Tests/Test-P002Settings.ps1
new file mode 100644
index 000000000..d907cc109
--- /dev/null
+++ b/tools/RepoQuality/Tests/Test-P002Settings.ps1
@@ -0,0 +1,35 @@
+param([string]$RepositoryPath = (Get-Location).Path)
+$ErrorActionPreference = 'Stop'
+Set-Location -LiteralPath $RepositoryPath
+# 直接驗證 SDK 選取與 Git 忽略行為，不掃描或建立真實金鑰、舊方案及建置產物。
+$cases = [ordered]@{
+    SdkSettings_SelectInstalledNet10WithLatestFeature = {
+        $path = Join-Path $RepositoryPath 'global.json'
+        if (-not (Test-Path -LiteralPath $path)) { throw 'global.json is missing.' }
+        $config = [IO.File]::ReadAllText($path, (New-Object Text.UTF8Encoding($false))) | ConvertFrom-Json
+        if ($config.sdk.version -notmatch '^10\.0\.\d+$' -or $config.sdk.rollForward -ne 'latestFeature') { throw 'SDK settings do not satisfy the phase contract.' }
+        $installed = @(& dotnet --list-sdks | ForEach-Object { ($_ -split ' ')[0] })
+        if ($installed -notcontains $config.sdk.version) { throw 'Pinned SDK is not installed.' }
+        $selected = (& dotnet --version | Out-String).Trim()
+        if ($LASTEXITCODE -ne 0 -or $selected -notmatch '^10\.0\.\d+$') { throw 'Repository did not select a .NET 10 SDK.' }
+    }
+    IgnoreRules_ExcludeReferenceArtifactsKeysAndRawTrx = {
+        # 以字碼產生非 ASCII 目錄資料，保持 PowerShell 可執行內容為 ASCII。
+        $legacyName = '.' + (-join (@(0x5176,0x4ed6,0x89e3,0x6c7a,0x65b9,0x6848) | ForEach-Object { [char]$_ }))
+        $probes = @(($legacyName + '/p0-ignore-probe.txt'), 'artifacts/p0-ignore-probe.txt', 'p0-ignore-probe.snk', 'p0-ignore-probe.trx')
+        if ($probes.Count -ne 4) { throw 'The test fixture must exercise all four independent paths.' }
+        $missing = @()
+        foreach ($probe in $probes) {
+            $null = & git -c core.longpaths=true check-ignore --no-index -- $probe
+            if ($LASTEXITCODE -ne 0) { $missing += $probe }
+        }
+        if ($missing.Count -gt 0) { throw ('Missing ignore coverage: ' + ($missing -join ', ')) }
+    }
+}
+$failed = 0
+foreach ($entry in $cases.GetEnumerator()) {
+    try { & $entry.Value; Write-Output ('PASS ' + $entry.Key) }
+    catch { $failed++; Write-Output ('FAIL ' + $entry.Key + ': ' + $_.Exception.Message) }
+}
+Write-Output ('P0-02 settings tests: ' + ($cases.Count - $failed) + '/' + $cases.Count)
+if ($failed -gt 0) { exit 1 }

diff --git a/tools/RepoQuality/Tests/Test-ProgressPage.cjs b/tools/RepoQuality/Tests/Test-ProgressPage.cjs
new file mode 100644
index 000000000..4732b3ce6
--- /dev/null
+++ b/tools/RepoQuality/Tests/Test-ProgressPage.cjs
@@ -0,0 +1,64 @@
+// 驗證真實進度頁的渲染程式；小型 DOM 測試替身只記錄可見內容與事件，不執行外部連線。
+// 注入無警示、無提示詞與正常提示詞三種資料，保護空值容錯與既有複製功能。
+const { test } = require('node:test');
+const assert = require('node:assert/strict');
+const fs = require('node:fs');
+const path = require('node:path');
+const vm = require('node:vm');
+
+const root = path.resolve(__dirname, '../../..');
+const html = fs.readFileSync(path.join(root, '文件/規劃設計書/實作計畫-3.3/實作進度.html'), 'utf8');
+const script = html.slice(html.lastIndexOf('<script>') + 8, html.lastIndexOf('</script>'));
+const dataStart = script.indexOf('const DATA = {');
+const dataEnd = script.indexOf('\n};', dataStart) + 3;
+const original = vm.runInNewContext(script.slice(dataStart, dataEnd) + '\nDATA;');
+
+// 每個案例持有自己的 DOM 與資料副本，避免警示事件或內容從上一個案例滲入。
+function render(alert) {
+  const elements = new Map();
+  class Element {
+    constructor(id) { this.id = id; this.hidden = false; this.listeners = new Map(); this.children = []; this.content = ''; }
+    set innerHTML(value) {
+      for (const id of this.children) elements.delete(id);
+      this.children = [];
+      this.content = String(value);
+      for (const match of this.content.matchAll(/\bid="([^"]+)"/g)) {
+        elements.set(match[1], new Element(match[1]));
+        this.children.push(match[1]);
+      }
+    }
+    get innerHTML() { return this.content; }
+    addEventListener(name, listener) { this.listeners.set(name, listener); }
+  }
+  for (const match of html.slice(0, html.lastIndexOf('<script>')).matchAll(/\bid="([^"]+)"/g)) {
+    elements.set(match[1], new Element(match[1]));
+  }
+  const data = JSON.parse(JSON.stringify(original));
+  data.alert = alert;
+  const source = script.slice(0, dataStart) + 'const DATA = ' + JSON.stringify(data) + ';' + script.slice(dataEnd);
+  vm.runInNewContext(source, { document: { getElementById: id => elements.get(id) || null }, navigator: {} });
+  return elements;
+}
+
+test('No alert hides the warning region and still renders the page', () => {
+  const elements = render(null);
+  assert.equal(elements.get('alert').hidden, true);
+  assert.equal(elements.has('copyBtn'), false);
+  assert.match(elements.get('meta').innerHTML, /1\.0\.0\.11\.Integration\.P0/);
+  assert.match(elements.get('wps').innerHTML, /P0-05/);
+});
+
+test('Alert without prompt omits copy button and full prompt area', () => {
+  const elements = render({ title: 'Pending input', body: 'Local check only', steps: [] });
+  assert.equal(elements.get('alert').hidden, false);
+  assert.match(elements.get('alert').innerHTML, /Pending input/);
+  assert.equal(elements.has('copyBtn'), false);
+  assert.equal(elements.has('promptText'), false);
+});
+
+test('Alert with prompt preserves escaped prompt and copy handler', () => {
+  const elements = render({ title: 'Review', body: 'Synthetic text', steps: [], prompt: '<sample>' });
+  assert.equal(elements.get('alert').hidden, false);
+  assert.equal(elements.get('copyBtn').listeners.has('click'), true);
+  assert.match(elements.get('alert').innerHTML, /&lt;sample&gt;/);
+});

diff --git a/tools/RepoQuality/Tests/Test-SixLaborsCiLicense.Tests.ps1 b/tools/RepoQuality/Tests/Test-SixLaborsCiLicense.Tests.ps1
new file mode 100644
index 000000000..e33818c7f
--- /dev/null
+++ b/tools/RepoQuality/Tests/Test-SixLaborsCiLicense.Tests.ps1
@@ -0,0 +1,129 @@
+<#
+直接執行 workflow 的授權準備與清理區塊，以合成文字驗證內容完整性、缺 secret 失敗、
+發布路徑失敗時回收、下游故障後清理，以及不刪除借用檔案。測試不讀真實授權檔，
+各案例只在唯一暫存目錄內建立假檔；finally 恢復程序環境並逐檔清理，不修改使用者設定。
+#>
+[CmdletBinding()]
+param()
+$ErrorActionPreference = 'Stop'
+$workflowPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../.github/workflows/solution-ci.yml'))
+$workflow = [IO.File]::ReadAllText($workflowPath)
+$utf8 = New-Object Text.UTF8Encoding($false)
+$fakeLicense = "synthetic-license-only`nline-two ::warning:: not-a-command`n"
+$failures = 0
+$checks = 0
+
+# 只擷取明確命名步驟的 run 區塊；缺步驟時回傳空動作，讓契約斷言產生功能紅燈。
+function Get-WorkflowBlock([string]$Name) {
+    $lines = $workflow -split '\r?\n'
+    $step = [Array]::IndexOf($lines, ('      - name: ' + $Name))
+    if ($step -lt 0) { return [scriptblock]::Create('') }
+    $index = $step + 1
+    while ($index -lt $lines.Length -and $lines[$index] -ne '        run: |') {
+        if ($lines[$index].StartsWith('      - name: ', [StringComparison]::Ordinal)) { return [scriptblock]::Create('') }
+        $index++
+    }
+    $body = New-Object 'Collections.Generic.List[string]'
+    for ($index++; $index -lt $lines.Length; $index++) {
+        if ($lines[$index].Length -eq 0) { $body.Add(''); continue }
+        if (-not $lines[$index].StartsWith('          ', [StringComparison]::Ordinal)) { break }
+        $body.Add($lines[$index].Substring(10))
+    }
+    return [scriptblock]::Create(($body -join "`n"))
+}
+
+# 失敗只印固定案例名稱，從不輸出捕捉到的例外、secret 或 GITHUB_ENV 內容。
+function Assert-Contract([bool]$Condition, [string]$Name) {
+    $script:checks++
+    if ($Condition) { Write-Output ('PASS ' + $Name) }
+    else { $script:failures++; Write-Output ('FAIL ' + $Name) }
+}
+
+$prepare = Get-WorkflowBlock 'Prepare Six Labors license'
+$cleanup = Get-WorkflowBlock 'Delete Six Labors license'
+$keys = @('RUNNER_TEMP', 'GITHUB_ENV', 'SIXLABORS_LICENSE', 'SixLaborsLicenseFile')
+$previous = @{}
+foreach ($key in $keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }
+$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
+$fixtureRoot = Join-Path $tempRoot ('speechmessage-license-test-' + [Guid]::NewGuid().ToString('N'))
+$null = New-Item -ItemType Directory -Path $fixtureRoot
+try {
+    $env:RUNNER_TEMP = $fixtureRoot
+    $env:GITHUB_ENV = Join-Path $fixtureRoot 'github-env.txt'
+    $env:SixLaborsLicenseFile = $null
+    $env:SIXLABORS_LICENSE = $fakeLicense
+    $output = @(& $prepare *>&1)
+    $files = @(Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.lic' -File)
+    Assert-Contract ($files.Count -eq 1) 'Configured_CreatesExactlyOneTemporaryFile'
+    $published = if ([IO.File]::Exists($env:GITHUB_ENV)) { [IO.File]::ReadAllText($env:GITHUB_ENV) } else { '' }
+    if ($files.Count -eq 1) {
+        $bytes = [IO.File]::ReadAllBytes($files[0].FullName)
+        Assert-Contract ([Text.Encoding]::UTF8.GetString($bytes) -ceq $fakeLicense) 'Configured_PreservesCompleteSyntheticContent'
+        Assert-Contract (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191)) 'Configured_Utf8WithoutBom'
+        Assert-Contract ($published.TrimEnd() -ceq ('SixLaborsLicenseFile=' + $files[0].FullName)) 'Configured_PublishesOnlyTemporaryPath'
+        $env:SixLaborsLicenseFile = $files[0].FullName
+    } else {
+        Assert-Contract $false 'Configured_PreservesCompleteSyntheticContent'
+        Assert-Contract $false 'Configured_Utf8WithoutBom'
+        Assert-Contract $false 'Configured_PublishesOnlyTemporaryPath'
+    }
+    $visible = ($output -join "`n") + $published
+    Assert-Contract (-not $visible.Contains('synthetic-license-only') -and -not $visible.Contains('line-two')) 'Configured_NeverPrintsLicense'
+    Assert-Contract ([string]::IsNullOrEmpty($env:SIXLABORS_LICENSE)) 'Configured_ClearsSecretFromPreparationProcess'
+    # 模擬下一步建置故障後，runner 仍會執行 always 清理；清理不依賴建置成功旗標。
+    $output = @(& $cleanup *>&1)
+    Assert-Contract (@(Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.lic' -File).Count -eq 0) 'DownstreamFailure_CleanupDeletesLicense'
+    $null = & $cleanup
+    Assert-Contract ([string]::IsNullOrEmpty($env:SixLaborsLicenseFile)) 'RepeatedCleanup_IsSafeAndClearsProcessPath'
+
+    foreach ($secret in @($null, '   ')) {
+        $env:SIXLABORS_LICENSE = $secret
+        $caught = $null
+        try { $output = @(& $prepare *>&1) } catch { $caught = $_.Exception }
+        Assert-Contract ($null -ne $caught -and $caught.Message.Contains('SIXLABORS_LICENSE is required')) 'MissingOrWhitespaceSecret_FailsWithClearMessage'
+        Assert-Contract (@(Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.lic' -File).Count -eq 0) 'MissingSecret_DoesNotCreateLicense'
+    }
+
+    # GITHUB_ENV 指向目錄，迫使「已寫檔但發布路徑失敗」；部分成功也不得留下授權檔。
+    $env:SIXLABORS_LICENSE = $fakeLicense
+    $env:GITHUB_ENV = $fixtureRoot
+    $caught = $null
+    try { $output = @(& $prepare *>&1) } catch { $caught = $_.Exception }
+    Assert-Contract ($null -ne $caught -and $caught.Message.Contains('license preparation failed')) 'PublishFailure_FailsWithFixedMessage'
+    Assert-Contract (@(Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.lic' -File).Count -eq 0) 'PublishFailure_RemovesPartiallyPreparedLicense'
+    Assert-Contract ([string]::IsNullOrEmpty($env:SIXLABORS_LICENSE)) 'PublishFailure_ClearsSecret'
+    $visible = ($output -join "`n") + $(if ($null -ne $caught) { $caught.Message } else { '' })
+    Assert-Contract (-not $visible.Contains('synthetic-license-only') -and -not $visible.Contains('line-two')) 'PublishFailure_NeverPrintsLicense'
+
+    # 不屬於此 workflow 的 runner 檔案不可被清理；不把任意環境路徑當成刪除權限。
+    $borrowed = Join-Path $fixtureRoot 'unrelated.lic'
+    [IO.File]::WriteAllText($borrowed, 'synthetic-borrowed', $utf8)
+    $env:SixLaborsLicenseFile = $borrowed
+    $caught = $null
+    try { $output = @(& $cleanup *>&1) } catch { $caught = $_.Exception }
+    Assert-Contract ($null -ne $caught -and [IO.File]::Exists($borrowed)) 'Cleanup_RejectsUnownedRunnerFile'
+    $env:RUNNER_TEMP = Join-Path $fixtureRoot 'different-runner'
+    $env:SixLaborsLicenseFile = $borrowed
+    $caught = $null
+    try { $output = @(& $cleanup *>&1) } catch { $caught = $_.Exception }
+    Assert-Contract ($null -ne $caught -and [IO.File]::Exists($borrowed)) 'Cleanup_RejectsPathOutsideRunner'
+
+    $prepareIndex = $workflow.IndexOf('      - name: Prepare Six Labors license', [StringComparison]::Ordinal)
+    $buildIndex = $workflow.IndexOf('      - name: Build without signing key', [StringComparison]::Ordinal)
+    Assert-Contract ($prepareIndex -ge 0 -and $prepareIndex -lt $buildIndex) 'Workflow_PreparesLicenseBeforeBuild'
+    Assert-Contract ($workflow.Contains('SIXLABORS_LICENSE: ${{ secrets.SIXLABORS_LICENSE }}')) 'Workflow_SecretUsesStepEnvironment'
+    Assert-Contract ([regex]::IsMatch($workflow, '(?m)^      - name: Delete Six Labors license\r?\n        if: always\(\)')) 'Workflow_CleanupAlwaysRuns'
+    Assert-Contract (-not $prepare.ToString().Contains('${{')) 'Workflow_DoesNotInterpolateSecretIntoScript'
+    Assert-Contract ($workflow.Contains('powershell -NoProfile -File tools/RepoQuality/Tests/Test-SixLaborsCiLicense.Tests.ps1')) 'Workflow_RunsLicenseRegressionTests'
+} finally {
+    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
+    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
+    $boundary = $tempRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
+    if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase) -or
+        -not [IO.Path]::GetFileName($resolved).StartsWith('speechmessage-license-test-', [StringComparison]::Ordinal)) { throw 'Unsafe fixture cleanup.' }
+    foreach ($file in @(Get-ChildItem -LiteralPath $resolved -File)) { Remove-Item -LiteralPath $file.FullName -Force }
+    [IO.Directory]::Delete($resolved, $false)
+}
+Write-Output ('Six Labors CI license tests: total=' + $checks + ' failed=' + $failures)
+if ($failures -gt 0) { exit 1 }
+exit 0

diff --git a/tools/RepoQuality/Tests/Test-TextEncoding.Tests.ps1 b/tools/RepoQuality/Tests/Test-TextEncoding.Tests.ps1
new file mode 100644
index 000000000..5ae797016
--- /dev/null
+++ b/tools/RepoQuality/Tests/Test-TextEncoding.Tests.ps1
@@ -0,0 +1,271 @@
+<#
+.SYNOPSIS
+以實際 Git 差異、位元組樣本與子程序驗證編碼守門及無損轉換契約。
+.DESCRIPTION
+每例擁有獨立暫存 Git 目錄；注入不合法位元組後，斷言結束碼與具體診斷，避免任何失敗都算通過。
+轉換案例獨立比對預期 UTF-8 位元組、原始雜湊與 manifest，不能只相信工具宣告成功。
+測試只使用合成中文字串，不含使用者、教會、憑證或外部網路資料；清理前驗證目錄邊界。
+#>
+
+Describe 'Repository text encoding and lossless conversion contracts' {
+    BeforeAll {
+        $script:repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
+        $script:fixtureBase = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'Fixtures/.runtime'))
+        $script:engine = Join-Path $PSHOME 'powershell.exe'
+        if (-not (Test-Path -LiteralPath $script:engine)) { $script:engine = Join-Path $PSHOME 'pwsh.exe' }
+        $script:checker = Join-Path $script:repoRoot 'tools/RepoQuality/Test-TextEncoding.ps1'
+        $script:converter = Join-Path $script:repoRoot 'tools/RepoQuality/Convert-TextEncoding.ps1'
+        $script:utf8 = New-Object Text.UTF8Encoding($false, $true)
+        $script:cp950 = [Text.Encoding]::GetEncoding(950)
+        $script:chinese = [string][char]0x4E2D + [char]0x6587
+        $script:validSource = 'internal class Fixture { internal string Value = "' + $script:chinese + '"; }' + "`r`n"
+
+        function Invoke-EncodingCheck {
+            <# 檢查真實工作樹差異，回傳結束碼與診斷；子程序結束後才讀取結果。 #>
+            param([string]$Scope = 'Changed')
+            $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script:checker, '-RepositoryPath', $script:caseRepo, '-Scope', $Scope)
+            if ($Scope -eq 'Changed') { $arguments += @('-Base', 'HEAD') }
+            else { $arguments += @('-ReportPath', (Join-Path $script:caseDirectory 'report.md')) }
+            $output = & $script:engine @arguments 2>&1
+            return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
+        }
+
+        function Invoke-EncodingConversion {
+            <# 以公開命令介面轉換單一合成樣本，manifest 位於來源 Git 目錄外，避免干擾掃描。 #>
+            param([switch]$Apply, [switch]$DryRun)
+            $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script:converter, '-Path', $script:sample, '-ManifestPath', $script:manifest)
+            if ($Apply) { $arguments += '-Apply' }
+            if ($DryRun) { $arguments += '-DryRun' }
+            $output = & $script:engine @arguments 2>&1
+            return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
+        }
+
+        function Invoke-EncodingArrayConversion {
+            <# 以編碼命令保留 Unicode 路徑及陣列參數；每個路徑先做單引號引用，不經字串展開執行。 #>
+            param([string[]]$SourcePaths, [string]$AuditPath)
+            $quotedPaths = @($SourcePaths | ForEach-Object { "'" + $_.Replace("'", "''") + "'" })
+            $command = "& '" + $script:converter.Replace("'", "''") + "' -Path @(" + ($quotedPaths -join ',') + ") -ManifestPath '" + $AuditPath.Replace("'", "''") + "' -Apply"
+            $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
+            $output = & $script:engine -NoProfile -EncodedCommand $encoded 2>&1
+            return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
+        }
+    }
+
+    BeforeEach {
+        $script:caseDirectory = Join-Path $script:fixtureBase ([guid]::NewGuid().ToString('N'))
+        $script:caseRepo = Join-Path $script:caseDirectory 'repo'
+        [void][IO.Directory]::CreateDirectory($script:caseRepo)
+        $script:sample = Join-Path $script:caseRepo 'sample.cs'
+        $script:manifest = Join-Path $script:caseDirectory 'manifest.json'
+        [IO.File]::WriteAllText($script:sample, "initial`r`n", $script:utf8)
+        & git -c core.longpaths=true init -q $script:caseRepo
+        & git -c core.longpaths=true -C $script:caseRepo config core.autocrlf false
+        & git -c core.longpaths=true -C $script:caseRepo add sample.cs
+        & git -c core.longpaths=true -C $script:caseRepo -c user.name=EncodingTest -c user.email=encoding-test@invalid.example commit -q -m 'fixture baseline'
+        if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated Git fixture.' }
+    }
+
+    AfterEach {
+        $resolved = [IO.Path]::GetFullPath($script:caseDirectory)
+        $prefix = $script:fixtureBase.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
+        if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup target.' }
+        Remove-Item -LiteralPath $resolved -Recurse -Force
+    }
+
+    # 正常樣本與每一個故障各自比對具體診斷，不能把解析錯誤或找不到工具當成成功拒絕。
+    It 'Utf8NoBomCrlf_ChangedFile_Passes' {
+        [IO.File]::WriteAllText($script:sample, $script:validSource, $script:utf8)
+        $result = Invoke-EncodingCheck
+        $result.ExitCode | Should Be 0
+        $result.Output | Should Match 'checked=1'
+    }
+    It 'Utf8Bom_ChangedFile_FailsWithBom' {
+        [IO.File]::WriteAllBytes($script:sample, ([byte[]]@(0xEF,0xBB,0xBF) + $script:utf8.GetBytes($script:validSource)))
+        $result = Invoke-EncodingCheck
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'Bom'
+    }
+    It 'LfOnly_ChangedFile_FailsWithLineEndings' {
+        [IO.File]::WriteAllText($script:sample, $script:validSource.Replace("`r`n", "`n"), $script:utf8)
+        $result = Invoke-EncodingCheck
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'NonCrlfLineEndings'
+    }
+    It 'Cp950_ChangedFile_FailsWithInvalidUtf8' {
+        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($script:validSource))
+        $result = Invoke-EncodingCheck
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'InvalidUtf8'
+    }
+    It 'MissingFinalNewline_ChangedFile_Fails' {
+        [IO.File]::WriteAllText($script:sample, $script:validSource.TrimEnd(), $script:utf8)
+        $result = Invoke-EncodingCheck
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'MissingFinalCrlf'
+    }
+    It 'ReplacementCharacter_ChangedFile_Fails' {
+        [IO.File]::WriteAllText($script:sample, ([string][char]0xFFFD + "`r`n"), $script:utf8)
+        $result = Invoke-EncodingCheck
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'ReplacementCharacter'
+    }
+    It 'PowerShell_ChineseOnlyInComment_Passes' {
+        $psPath = Join-Path $script:caseRepo 'sample.ps1'
+        [IO.File]::WriteAllText($psPath, ('# ' + $script:chinese + "`r`nWrite-Output 'ok'`r`n"), $script:utf8)
+        & git -c core.longpaths=true -C $script:caseRepo add sample.ps1
+        $result = Invoke-EncodingCheck
+        $result.ExitCode | Should Be 0
+    }
+    It 'PowerShell_ChineseInString_Fails' {
+        $psPath = Join-Path $script:caseRepo 'sample.ps1'
+        [IO.File]::WriteAllText($psPath, ("Write-Output '" + $script:chinese + "'`r`n"), $script:utf8)
+        & git -c core.longpaths=true -C $script:caseRepo add sample.ps1
+        $result = Invoke-EncodingCheck
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'NonAsciiExecutablePowerShell'
+    }
+    It 'AllScope_InvalidFile_ReportsWithoutFailingBuild' {
+        [IO.File]::WriteAllText($script:sample, "bad`n", $script:utf8)
+        $result = Invoke-EncodingCheck -Scope All
+        $result.ExitCode | Should Be 0
+        [IO.File]::ReadAllText((Join-Path $script:caseDirectory 'report.md')) | Should Match 'NonCrlfLineEndings'
+    }
+    It 'AllScope_DoesNotReadOutsideRepository' {
+        $outside = Join-Path $script:fixtureBase ('outside-' + [guid]::NewGuid().ToString('N') + '.cs')
+        try {
+            [IO.File]::WriteAllText($outside, "outside`n", $script:utf8)
+            [IO.File]::WriteAllText($script:sample, "inside`n", $script:utf8)
+            $result = Invoke-EncodingCheck -Scope All
+            $result.ExitCode | Should Be 0
+            $report = [IO.File]::ReadAllText((Join-Path $script:caseDirectory 'report.md'))
+            $report | Should Match 'sample.cs'
+            $report | Should Not Match ([IO.Path]::GetFileName($outside))
+        }
+        finally {
+            if ([IO.File]::Exists($outside)) { [IO.File]::Delete($outside) }
+        }
+    }
+    It 'AllScope_TrackedAndExcludedPaths_OnlyScansAllowedTrackedSource' {
+        [IO.File]::WriteAllText((Join-Path $script:caseRepo 'untracked.cs'), "untracked`n", $script:utf8)
+        $referenceName = '.' + [string][char]0x5176 + [char]0x4ED6 + [char]0x89E3 + [char]0x6C7A + [char]0x65B9 + [char]0x6848
+        foreach ($directory in @('bin','obj','.worktrees',$referenceName)) {
+            $folder = Join-Path $script:caseRepo $directory
+            [void][IO.Directory]::CreateDirectory($folder)
+            [IO.File]::WriteAllText((Join-Path $folder 'excluded.cs'), "excluded`n", $script:utf8)
+            & git -c core.longpaths=true -C $script:caseRepo add -f -- ($directory + '/excluded.cs')
+        }
+        $result = Invoke-EncodingCheck -Scope All
+        $result.ExitCode | Should Be 0
+        $result.Output | Should Match 'checked=1 '
+        $report = [IO.File]::ReadAllText((Join-Path $script:caseDirectory 'report.md'))
+        $report | Should Not Match 'excluded.cs|untracked.cs'
+    }
+    It 'AllScope_TrackedPathCrossesJunction_RejectsBeforeReadingExternalContent' {
+        $linked = Join-Path $script:caseRepo 'linked'
+        $external = Join-Path $script:caseDirectory 'outside'
+        [void][IO.Directory]::CreateDirectory($linked)
+        [void][IO.Directory]::CreateDirectory($external)
+        [IO.File]::WriteAllText((Join-Path $linked 'foreign.cs'), "tracked`r`n", $script:utf8)
+        [IO.File]::WriteAllText((Join-Path $external 'foreign.cs'), "outside`n", $script:utf8)
+        & git -c core.longpaths=true -C $script:caseRepo add linked/foreign.cs
+        [IO.File]::Delete((Join-Path $linked 'foreign.cs'))
+        [IO.Directory]::Delete($linked)
+        try {
+            [void](New-Item -ItemType Junction -Path $linked -Target $external)
+            $result = Invoke-EncodingCheck -Scope All
+            $result.ExitCode | Should Be 0
+            $report = [IO.File]::ReadAllText((Join-Path $script:caseDirectory 'report.md'))
+            $report | Should Match 'UnsafePath'
+            $report | Should Not Match 'NonCrlfLineEndings'
+        }
+        finally {
+            # 只移除已確認屬於本案例的 junction 本身，不遞迴處理它指向的目錄。
+            $expected = [IO.Path]::GetFullPath((Join-Path $script:caseRepo 'linked'))
+            if ([IO.Path]::GetFullPath($linked) -ne $expected) { throw 'Unsafe junction cleanup target.' }
+            if ([IO.Directory]::Exists($linked)) { [IO.Directory]::Delete($linked) }
+        }
+    }
+    It 'ChangedScope_UnchangedBadFile_IsNotScanned' {
+        [IO.File]::WriteAllText($script:sample, "bad`n", $script:utf8)
+        & git -c core.longpaths=true -C $script:caseRepo add sample.cs
+        & git -c core.longpaths=true -C $script:caseRepo -c user.name=EncodingTest -c user.email=encoding-test@invalid.example commit -q -m 'existing invalid fixture'
+        $result = Invoke-EncodingCheck
+        $result.ExitCode | Should Be 0
+        $result.Output | Should Match 'checked=0'
+    }
+    # 轉換必須比對真正輸出位元組；無損與乾跑的斷言都不能只依賴 manifest 的宣告。
+    It 'ConvertCp950_Apply_PreservesChineseAndWritesUtf8' {
+        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($script:validSource))
+        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash.ToLowerInvariant()
+        $result = Invoke-EncodingConversion -Apply
+        $result.ExitCode | Should Be 0
+        [Convert]::ToBase64String([IO.File]::ReadAllBytes($script:sample)) | Should Be ([Convert]::ToBase64String($script:utf8.GetBytes($script:validSource)))
+        $manifest = Get-Content -LiteralPath $script:manifest -Raw -Encoding UTF8 | ConvertFrom-Json
+        $manifest.files[0].originalEncoding | Should Be 'cp950'
+        $manifest.files[0].beforeSha256 | Should Be $before
+        $manifest.files[0].afterSha256 | Should Be ((Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash.ToLowerInvariant())
+        $manifest.files[0].chineseStringLiteralsUnchanged | Should Be $true
+    }
+    It 'Convert_DefaultDryRun_DoesNotWriteSource' {
+        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($script:validSource))
+        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash
+        $result = Invoke-EncodingConversion
+        $result.ExitCode | Should Be 0
+        (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash | Should Be $before
+        $manifest = Get-Content -LiteralPath $script:manifest -Raw -Encoding UTF8 | ConvertFrom-Json
+        $manifest.dryRun | Should Be $true
+        $manifest.files[0].status | Should Be 'dry-run'
+    }
+    It 'Convert_ExplicitDryRun_DoesNotWriteSource' {
+        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($script:validSource))
+        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash
+        $result = Invoke-EncodingConversion -DryRun
+        $result.ExitCode | Should Be 0
+        (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash | Should Be $before
+    }
+    It 'Convert_InvalidEncoding_LeavesOriginalUntouched' {
+        [IO.File]::WriteAllBytes($script:sample, [byte[]]@(0x81))
+        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash
+        $result = Invoke-EncodingConversion -Apply
+        $result.ExitCode | Should Be 1
+        (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash | Should Be $before
+        $manifest = Get-Content -LiteralPath $script:manifest -Raw -Encoding UTF8 | ConvertFrom-Json
+        $manifest.files[0].status | Should Be 'failed'
+        $manifest.files[0].error | Should Match 'InvalidEncoding'
+    }
+    It 'Convert_MultilineChineseLiteralWouldChange_RefusesWrite' {
+        $source = 'internal class Fixture { internal string Value = @"' + $script:chinese + "`n" + $script:chinese + '"; }' + "`n"
+        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($source))
+        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash
+        $result = Invoke-EncodingConversion -Apply
+        $result.ExitCode | Should Be 1
+        (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash | Should Be $before
+        $manifest = Get-Content -LiteralPath $script:manifest -Raw -Encoding UTF8 | ConvertFrom-Json
+        $manifest.files[0].chineseStringLiteralsUnchanged | Should Be $false
+        $manifest.files[0].error | Should Match 'StringLiteralChanged'
+    }
+    It 'Convert_Utf16Bom_ProducesExpectedUtf8Bytes' {
+        $encoding = New-Object Text.UnicodeEncoding($false, $true, $true)
+        [IO.File]::WriteAllBytes($script:sample, ($encoding.GetPreamble() + $encoding.GetBytes($script:validSource)))
+        $result = Invoke-EncodingConversion -Apply
+        $result.ExitCode | Should Be 0
+        [Convert]::ToBase64String([IO.File]::ReadAllBytes($script:sample)) | Should Be ([Convert]::ToBase64String($script:utf8.GetBytes($script:validSource)))
+    }
+    It 'Convert_ManifestMatchesLaterInput_RejectsBeforeAnyWrite' {
+        $second = Join-Path $script:caseRepo 'second.cs'
+        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($script:validSource))
+        [IO.File]::WriteAllBytes($second, $script:cp950.GetBytes($script:validSource))
+        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash
+        $result = Invoke-EncodingArrayConversion -SourcePaths @($script:sample, $second) -AuditPath $second
+        $result.ExitCode | Should Be 1
+        (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash | Should Be $before
+    }
+    It 'Convert_DuplicateStablePath_RejectsBeforeAnyWrite' {
+        [IO.File]::WriteAllBytes($script:sample, $script:cp950.GetBytes($script:validSource))
+        $before = (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash
+        $result = Invoke-EncodingArrayConversion -SourcePaths @($script:sample, $script:sample) -AuditPath $script:manifest
+        $result.ExitCode | Should Be 1
+        $result.Output | Should Match 'DuplicateSourcePath'
+        (Get-FileHash -LiteralPath $script:sample -Algorithm SHA256).Hash | Should Be $before
+    }
+}

diff --git a/tools/RepoQuality/TextEncoding.Common.ps1 b/tools/RepoQuality/TextEncoding.Common.ps1
new file mode 100644
index 000000000..b847746c8
--- /dev/null
+++ b/tools/RepoQuality/TextEncoding.Common.ps1
@@ -0,0 +1,89 @@
+<#
+.SYNOPSIS
+編碼守門與無損轉換共用的唯讀位元組、雜湊及 Git 邊界工具。
+.DESCRIPTION
+所有資料只活在單次腳本呼叫；串流與雜湊演算法都有明確釋放路徑，不保存來源文字到全域快取。
+Git 程序最長三十秒，輸出以 UTF-8 及 NUL 路徑分隔處理，不走訪父工作樹或忽略產物。
+#>
+
+function Invoke-TextQualityGit {
+    <# 執行不經 shell 的 Git 唯讀操作，正確引用每個 Windows 引數並在結束／逾時後釋放資源。 #>
+    param([string]$Root, [string[]]$Arguments)
+    $quoted = foreach ($value in (@('-c','core.longpaths=true','-c','core.quotepath=false') + $Arguments)) {
+        '"' + [regex]::Replace([regex]::Replace($value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
+    }
+    $start = New-Object Diagnostics.ProcessStartInfo
+    $start.FileName = 'git'
+    $start.Arguments = $quoted -join ' '
+    $start.WorkingDirectory = $Root
+    $start.UseShellExecute = $false
+    $start.CreateNoWindow = $true
+    $start.RedirectStandardOutput = $true
+    $start.RedirectStandardError = $true
+    $start.StandardOutputEncoding = New-Object Text.UTF8Encoding($false, $true)
+    $start.StandardErrorEncoding = New-Object Text.UTF8Encoding($false, $true)
+    $process = New-Object Diagnostics.Process
+    $process.StartInfo = $start
+    try {
+        [void]$process.Start()
+        $stdout = $process.StandardOutput.ReadToEndAsync()
+        $stderr = $process.StandardError.ReadToEndAsync()
+        if (-not $process.WaitForExit(30000)) {
+            $process.Kill()
+            $process.WaitForExit()
+            throw 'Git text inventory timed out.'
+        }
+        $output = $stdout.GetAwaiter().GetResult()
+        $errorText = $stderr.GetAwaiter().GetResult()
+        if ($process.ExitCode -ne 0) { throw "Git text inventory failed: $errorText" }
+        return $output
+    }
+    finally { $process.Dispose() }
+}
+
+function Get-TextByteIssues {
+    <# 檢查原始位元組及 PS token；不可讓 BOM 自動剝除或解碼替代字元掩蓋錯誤。 #>
+    param([byte[]]$Bytes, [string]$Extension)
+    $issues = New-Object 'Collections.Generic.List[string]'
+    if (($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) -or
+        ($Bytes.Length -ge 2 -and (($Bytes[0] -eq 0xFF -and $Bytes[1] -eq 0xFE) -or ($Bytes[0] -eq 0xFE -and $Bytes[1] -eq 0xFF))) -or
+        ($Bytes.Length -ge 4 -and $Bytes[0] -eq 0 -and $Bytes[1] -eq 0 -and $Bytes[2] -eq 0xFE -and $Bytes[3] -eq 0xFF)) { $issues.Add('Bom') }
+    $source = $null
+    try { $source = (New-Object Text.UTF8Encoding($false, $true)).GetString($Bytes) }
+    catch [Text.DecoderFallbackException] { $issues.Add('InvalidUtf8') }
+    $lineText = $source
+    if ($null -eq $lineText) { $lineText = [Text.Encoding]::ASCII.GetString($Bytes) }
+    if ($lineText -match '(?<!\r)\n|\r(?!\n)') { $issues.Add('NonCrlfLineEndings') }
+    if ($Bytes.Length -lt 2 -or $Bytes[$Bytes.Length - 2] -ne 13 -or $Bytes[$Bytes.Length - 1] -ne 10) { $issues.Add('MissingFinalCrlf') }
+    if ($null -ne $source -and $source.Contains([string][char]0xFFFD)) { $issues.Add('ReplacementCharacter') }
+    if ($Extension -eq '.ps1' -and $null -ne $source) {
+        $tokens = $null
+        $parseErrors = $null
+        [void][Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
+        if ($parseErrors.Count -gt 0) { $issues.Add('PowerShellParseError') }
+        $executable = $source.ToCharArray()
+        foreach ($token in $tokens) {
+            if ($token.Kind -eq 'Comment') {
+                for ($i = $token.Extent.StartOffset; $i -lt $token.Extent.EndOffset; $i++) { $executable[$i] = ' ' }
+            }
+        }
+        if (($executable -join '') -match '[^\x00-\x7F]') { $issues.Add('NonAsciiExecutablePowerShell') }
+    }
+    return $issues.ToArray()
+}
+
+function Get-TextSha256 {
+    <# 對原始或候選位元組計算雜湊；只回傳摘要，不把來源內容寫入 manifest。 #>
+    param([byte[]]$Bytes)
+    $algorithm = [Security.Cryptography.SHA256]::Create()
+    try { return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
+    finally { $algorithm.Dispose() }
+}
+
+function Get-TextLiteralSegments {
+    <# 保守擷取一般、逐字與 raw 字串；換行會改變常值內容時拒絕寫入，避免編碼轉換偷改行為。 #>
+    param([string]$Source)
+    $pattern = '(?s)(?<raw>"{3,})(?:(?!\k<raw>).)*\k<raw>|@"(?:""|[^"])*"|"(?:\\.|[^"\\\r\n])*"'
+    $expression = New-Object Text.RegularExpressions.Regex($pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant, [TimeSpan]::FromSeconds(2))
+    return @($expression.Matches($Source) | ForEach-Object { $_.Value })
+}
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