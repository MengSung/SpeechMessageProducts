<#
檔案整體說明

目的與定位：掃描目前方案內的跨專案內容副本與命名空間加型別重名，只拒絕基準之外的新重複。

主要功能與流程：
從指定方案驗證所有 csproj 路徑後建立本次掃描的專案根集合。父專案列舉遇到已列入
方案的子專案根即停止，子專案在自己的回合完整掃描，避免同一來源跨專案重複計入；
不依 PluginHarness 等專案或資料夾名稱豁免來源。內容雜湊與型別身分經基準比對後輸出固定診斷。

使用邊界與維護：單次 CLI 擁有根集合、來源識別與比對結果，僅在該次執行保留中繼資料；
每次只解碼一個不超過十六 MiB 的來源，不執行產品、不連外，也不保留使用者／租戶資料。
所有讀取先驗證工作樹邊界並拒絕 reparse point；只在明確更新或基準不存在時寫基準。
合成測試須同時證明父子來源唯一歸屬與子內真正跨專案重複仍失敗，不得藉擴張排除或改基準消除診斷。
#>
<#
.SYNOPSIS
掃描目前方案內的跨專案內容副本與命名空間加型別重名，只拒絕基準之外的新重複。
.DESCRIPTION
從指定方案取得專案，絕不向上找父 worktree；排除建置、文件、參考方案、其他 worktree 與產生檔。
目錄逐層列舉且跳過 reparse point，不沿 junction 讀外部檔。只在區域內處理單一來源文字，
輸出僅有雜湊、型別與路徑，不保存程式本文、使用者資料或憑證，不啟動產品或外部程序。
既有 UTF-8／CP950 只做嚴格唯讀解碼，不改寫來源；編碼合法性由專用守門負責。
.PARAMETER RepositoryPath
目前 worktree 根目錄，預設為工具上兩層；測試可傳獨立合成方案。
.PARAMETER SolutionPath
根目錄內的方案相對路徑，不接受 repo 外路徑。
.PARAMETER Baseline
根目錄內的 JSON 基準；預設 tools/RepoQuality/duplicate-baseline.json。
.PARAMETER WriteBaseline
明確更新基準；缺少基準時也會建立。平常棘輪檢查不改既有基準。
#>
[CmdletBinding()]
param([string]$RepositoryPath, [string]$SolutionPath = 'SpeechMessageProducts.sln', [string]$Baseline, [switch]$WriteBaseline)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TextEncoding.Common.ps1')

function Get-SafeRepositoryPath {
    <# 在任何讀取前驗證絕對根邊界與每個既有路徑元件，拒絕專案／基準透過 junction 跳到外部。 #>
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($script:rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'UnsafePath: path is outside the repository.' }
    $cursor = $script:root
    foreach ($segment in $full.Substring($script:rootPrefix.Length).Split([IO.Path]::DirectorySeparatorChar)) {
        $cursor = Join-Path $cursor $segment
        if (([IO.File]::Exists($cursor) -or [IO.Directory]::Exists($cursor)) -and
            (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'UnsafePath: reparse point is not allowed.' }
    }
    return $full
}

function Get-SourceText {
    <# 單檔上限十六 MiB；嚴格解碼不使用替代字元，CP950 僅供既有來源唯讀盤點。 #>
    param([string]$Path)
    if ((New-Object IO.FileInfo($Path)).Length -gt 16777216) { throw 'SourceTooLarge: source exceeds the bounded scan size.' }
    $bytes = [IO.File]::ReadAllBytes($Path)
    try { $text = $script:utf8.GetString($bytes) }
    catch [Text.DecoderFallbackException] {
        $cp950 = [Text.Encoding]::GetEncoding(950, [Text.EncoderFallback]::ExceptionFallback, [Text.DecoderFallback]::ExceptionFallback)
        $text = $cp950.GetString($bytes)
    }
    return $text.TrimStart([char]0xFEFF)
}

function Get-SourceTypeNames {
    <# 遮罩註解與字串後，追蹤 namespace 與外層型別作用域，避免把訊息或不同 owner 當同一型別。
       此為來源宣告掃描，不載入組件或執行程式；只回傳型別名稱，本文不跨檔保存。 #>
    param([string]$Source)
    $maskPattern = @'
(?s)/\*.*?\*/|//[^\r\n]*|(?:\$@|@\$|@)"(?:""|[^"])*"|(?<raw>"{3,}).*?\k<raw>|\$?"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'
'@
    $code = [regex]::Replace($Source, $maskPattern.Trim(), ' ')
    $identifier = '@?[\p{L}_][\p{L}\p{Nd}_]*'
    $pattern = '(?<ns>\bnamespace\s+(?<nsname>' + $identifier + '(?:\s*\.\s*' + $identifier + ')*)\s*(?<term>[;{]))|(?<type>\b(?:class|struct|record(?:\s+(?:class|struct))?)\s+(?<typename>' + $identifier + '))|(?<open>\{)|(?<close>\})|(?<semicolon>;)'
    $namespace = ''
    $level = 0
    $frames = New-Object 'Collections.Generic.List[object]'
    $typeFrames = New-Object 'Collections.Generic.List[object]'
    $owners = New-Object 'Collections.Generic.List[string]'
    $pendingType = $null
    foreach ($token in [regex]::Matches($code, $pattern)) {
        if ($token.Groups['ns'].Success) {
            $name = ($token.Groups['nsname'].Value -replace '\s', '') -replace '@', ''
            if ($token.Groups['term'].Value -eq '{') {
                $frames.Add(@{ Level = $level; Parent = $namespace })
                $level++
                if ($namespace) { $namespace += '.' + $name } else { $namespace = $name }
            }
            else { $namespace = $name }
        }
        elseif ($token.Groups['open'].Success) {
            if ($null -ne $pendingType) {
                $typeFrames.Add(@{ Level = $level; OwnerCount = $owners.Count })
                $owners.Add($pendingType)
                $pendingType = $null
            }
            $level++
        }
        elseif ($token.Groups['close'].Success) {
            $level--
            if ($typeFrames.Count -gt 0 -and $typeFrames[$typeFrames.Count - 1].Level -eq $level) {
                $count = $typeFrames[$typeFrames.Count - 1].OwnerCount
                $owners.RemoveRange($count, $owners.Count - $count)
                $typeFrames.RemoveAt($typeFrames.Count - 1)
            }
            if ($frames.Count -gt 0 -and $frames[$frames.Count - 1].Level -eq $level) {
                $namespace = $frames[$frames.Count - 1].Parent
                $frames.RemoveAt($frames.Count - 1)
            }
        }
        elseif ($token.Groups['type'].Success) {
            $name = $token.Groups['typename'].Value.TrimStart([char]'@')
            $parts = @()
            if ($namespace) { $parts += $namespace }
            $parts += @($owners.ToArray())
            $parts += $name
            $parts -join '.'
            $pendingType = $name
        }
        # 無本體 record／struct 的分號終止宣告，不能讓下一個大括號錯歸 owner。
        elseif ($token.Groups['semicolon'].Success) { $pendingType = $null }
    }
}

function Get-ProjectSourceFiles {
    <# 一層一層走訪指定專案；僅在下降前略過方案內其他專案根，當前根仍完整列舉。
       根集合由已驗證的方案路徑建立且僅屬當次 CLI；未列入方案的巢狀 csproj 不取得排除權限。
       先排除既有建置／參考目錄與 reparse point，再讀來源，不向工作樹外延伸。 #>
    param([string]$ProjectDirectory, [Collections.Generic.HashSet[string]]$ProjectRoots)
    $pending = New-Object 'Collections.Generic.Stack[IO.DirectoryInfo]'
    $pending.Push((New-Object IO.DirectoryInfo($ProjectDirectory)))
    while ($pending.Count -gt 0) {
        foreach ($item in $pending.Pop().EnumerateFileSystemInfos()) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            if (($item.Attributes -band [IO.FileAttributes]::Directory) -ne 0) {
                if ($item.Name -match '^(bin|obj|docs|\.worktrees|\.ccg|\.\u5176\u4ED6\u89E3\u6C7A\u65B9\u6848)$') { continue }
                if ($ProjectRoots.Contains($item.FullName)) { continue }
                $pending.Push([IO.DirectoryInfo]$item)
            }
            elseif ($item.Extension -eq '.cs' -and $item.Name -notmatch '(?i)(\.g\.cs|\.Designer\.cs)$') { $item.FullName }
        }
    }
}

try {
    if (-not $RepositoryPath) { $RepositoryPath = Join-Path $PSScriptRoot '..\..' }
    $script:root = (Resolve-Path -LiteralPath $RepositoryPath).Path
    $script:rootPrefix = [IO.Path]::GetFullPath($script:root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $script:utf8 = New-Object Text.UTF8Encoding($false, $true)
    if (-not [IO.Path]::IsPathRooted($SolutionPath)) { $SolutionPath = Join-Path $script:root $SolutionPath }
    $solution = Get-SafeRepositoryPath $SolutionPath
    if (-not $Baseline) { $Baseline = Join-Path $script:root 'tools/RepoQuality/duplicate-baseline.json' }
    if (-not [IO.Path]::IsPathRooted($Baseline)) { $Baseline = Join-Path $script:root $Baseline }
    $Baseline = Get-SafeRepositoryPath $Baseline
    if ([IO.Path]::GetExtension($Baseline) -ne '.json') { throw 'InvalidBaseline: expected a JSON path.' }
    $projects = @([regex]::Matches((Get-SourceText $solution), '(?m)^\s*Project\([^)]*\)\s*=\s*"[^"]+",\s*"(?<path>[^"]+\.csproj)"') | ForEach-Object { $_.Groups['path'].Value })
    if ($projects.Count -eq 0) { throw 'NoProjects: no C# projects are listed in the solution.' }
    [array]::Sort($projects, [StringComparer]::Ordinal)
    # 先驗證完整方案再掃描，讓父專案即使先出現也能辨認子專案邊界；集合查詢不逐檔重掃方案。
    $projectRoots = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $projectPaths = @(foreach ($project in $projects) {
        $projectPath = Get-SafeRepositoryPath (Join-Path ([IO.Path]::GetDirectoryName($solution)) $project)
        if (-not [IO.File]::Exists($projectPath)) { throw 'MissingProject: solution project does not exist.' }
        [void]$projectRoots.Add([IO.Path]::GetDirectoryName($projectPath))
        $projectPath
    })
    $groups = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    $checked = 0
    foreach ($projectPath in $projectPaths) {
        $projectName = $projectPath.Substring($script:rootPrefix.Length).Replace([char]92, [char]47)
        if ($projectName -match '(^|/)(docs|\.ccg|\.worktrees|\.\u5176\u4ED6\u89E3\u6C7A\u65B9\u6848)(/|$)') { continue }
        [string[]]$files = @(Get-ProjectSourceFiles ([IO.Path]::GetDirectoryName($projectPath)) $projectRoots)
        [array]::Sort($files, [StringComparer]::Ordinal)
        foreach ($file in $files) {
            $safePath = Get-SafeRepositoryPath $file
            $relative = $safePath.Substring($script:rootPrefix.Length).Replace([char]92, [char]47)
            $source = Get-SourceText $safePath
            $normalized = [regex]::Replace($source.Replace("`r`n", "`n"), '(?m)^[ \t]*//[^\n]*(\n|$)', '')
            $normalized = [regex]::Replace($normalized, '\s+', ' ').Trim()
            $hash = Get-TextSha256 -Bytes $script:utf8.GetBytes($normalized)
            $identities = @('content|' + $hash) + @(Get-SourceTypeNames $source | ForEach-Object { 'type|' + $_ })
            foreach ($identity in $identities) {
                if (-not $groups.ContainsKey($identity)) { $groups[$identity] = New-Object 'Collections.Generic.List[object]' }
                $groups[$identity].Add(@{ Project = $projectName; Location = ($projectName + '|' + $relative) })
            }
            $checked++
        }
    }
    if ($checked -eq 0) { throw 'NoSources: refusing an empty duplicate scan.' }
    $entries = New-Object 'Collections.Generic.List[object]'
    [string[]]$groupKeys = @($groups.Keys)
    [array]::Sort($groupKeys, [StringComparer]::Ordinal)
    foreach ($identity in $groupKeys) {
        $members = @($groups[$identity].ToArray())
        if (@($members | ForEach-Object { $_.Project } | Select-Object -Unique).Count -lt 2) { continue }
        [string[]]$locations = @($members | ForEach-Object { $_.Location } | Select-Object -Unique)
        [array]::Sort($locations, [StringComparer]::Ordinal)
        $split = $identity.IndexOf('|')
        $entries.Add([ordered]@{ kind = $identity.Substring(0, $split); key = $identity.Substring($split + 1); locations = $locations })
    }
    $created = -not [IO.File]::Exists($Baseline)
    if ($created -or $WriteBaseline) {
        $document = [ordered]@{ schemaVersion = 1; solution = $solution.Substring($script:rootPrefix.Length).Replace([char]92, [char]47); entries = @($entries.ToArray()) }
        $json = ($document | ConvertTo-Json -Depth 8) -replace "`r?`n", "`r`n"
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Baseline))
        [IO.File]::WriteAllText($Baseline, $json + "`r`n", (New-Object Text.UTF8Encoding($false)))
    }
    $baselineDocument = [IO.File]::ReadAllText($Baseline, $script:utf8) | ConvertFrom-Json
    if ($baselineDocument.schemaVersion -ne 1) { throw 'InvalidBaseline: unsupported schema version.' }
    $allowed = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($entry in @($baselineDocument.entries)) {
        $identity = $entry.kind + '|' + $entry.key
        if ($allowed.ContainsKey($identity)) { throw 'DuplicateBaselineIdentity: baseline groups must have unique identities.' }
        $seenLocations = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
        foreach ($location in @($entry.locations)) {
            if ([string]::IsNullOrWhiteSpace($location)) { throw 'InvalidBaseline: empty source location.' }
            if (-not $seenLocations.Add($location)) { throw 'DuplicateBaselineLocation: baseline locations must be unique.' }
        }
        $allowed[$identity] = @($entry.locations)
    }
    $new = 0
    $current = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($entry in $entries) {
        $identity = $entry.kind + '|' + $entry.key
        $current[$identity] = @($entry.locations)
        foreach ($location in @($entry.locations)) {
            if (-not $allowed.ContainsKey($identity) -or $allowed[$identity] -cnotcontains $location) {
                $new++
                Write-Output ('FAIL NewDuplicate: ' + $entry.kind + ' ' + $entry.key + ' [' + $location + ']')
            }
        }
    }
    $stale = 0
    foreach ($identity in $allowed.Keys) {
        foreach ($location in @($allowed[$identity])) {
            if (-not $current.ContainsKey($identity) -or $current[$identity] -cnotcontains $location) {
                $stale++
                Write-Output ('WARN BaselineStale: ' + $identity + ' [' + $location + ']')
            }
        }
    }
    Write-Output ('Duplicate scan: projects=' + $projects.Count + ' files=' + $checked + ' groups=' + $entries.Count + ' new=' + $new + ' stale=' + $stale + ' baselineCreated=' + $created)
    if ($new -gt 0) { exit 1 }
    exit 0
}
catch { Write-Output ('FAIL Duplicate scan: ' + $_.Exception.Message); exit 1 }
