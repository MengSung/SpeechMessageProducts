<#
.SYNOPSIS
以真實方案依賴與 workflow 執行區塊驗證 DevExtreme 來源未定時的過渡範圍。
.DESCRIPTION
省略篩選檔、漏掉間接依賴、把不相關專案排除、使用完整方案、隱藏摘要或誤啟動 tracer 都必須失敗。
只替換 dotnet 執行邊界以記錄實際命令參數，不連線或讀取設定中的機密。摘要使用合成暫存檔，確定清理。
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$workflow = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/solution-ci.yml'))
$filterName = 'SpeechMessageProducts.CI.slnf'
$script:calls = New-Object 'System.Collections.Generic.List[string[]]'
function dotnet {
    # 只記錄 workflow 真正傳入的命令參數；不執行建置或測試，也不接受任何帳密。
    $script:calls.Add([string[]]$args)
    $global:LASTEXITCODE = 0
}
function Get-RunBlock([string]$Name) {
    # 保留實際 run 內容；除 dotnet 外不替換任何指令，摘要會寫入測試自有檔案。
    $match = [regex]::Match($script:workflow, '(?ms)^      - name: ' + [regex]::Escape($Name) + '\r?\n(?<meta>.*?)        run: \|\r?\n(?<run>.*?)(?=^      - name:|\z)')
    if (-not $match.Success) { throw ('Workflow block missing: ' + $Name) }
    return $match
}
$cases = [ordered]@{
    Filter_ExcludesExactlyDependencyClosure = {
        $filter = [IO.File]::ReadAllText((Join-Path $root $filterName)) | ConvertFrom-Json
        if ($filter.solution.path -ne 'SpeechMessageProducts.sln') { throw 'Unexpected parent solution.' }
        $solution = [IO.File]::ReadAllText((Join-Path $root 'SpeechMessageProducts.sln'))
        $projects = @([regex]::Matches($solution, '(?m)^Project\([^\r\n]+?= "[^"]+", "(?<path>[^"]+\.csproj)"') | ForEach-Object { $_.Groups['path'].Value.Replace('\', '/') })
        $graph = @{}
        $excluded = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($project in $projects) {
            $path = Join-Path $root $project
            [xml]$xml = [IO.File]::ReadAllText($path)
            $graph[$project] = @($xml.SelectNodes('//ProjectReference') | ForEach-Object {
                $include = $_.GetAttribute('Include')
                if ($include.Contains('$')) { throw 'Unresolved dependency cannot be classified.' }
                [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($path)) $include)).Substring($root.Length + 1).Replace('\', '/')
            })
            if (@($xml.SelectNodes('//PackageReference') | Where-Object { $_.GetAttribute('Include') -eq 'DevExtreme.AspNet.Core' }).Count -gt 0) { [void]$excluded.Add($project) }
        }
        do {
            $changed = $false
            foreach ($project in $projects) {
                if (@($graph[$project] | Where-Object { $excluded.Contains($_) }).Count -gt 0) { if ($excluded.Add($project)) { $changed = $true } }
            }
        } while ($changed)
        $known = @('ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj', 'SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj', 'ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj')
        if (@(Compare-Object @($excluded | Sort-Object) ($known | Sort-Object)).Count -gt 0) { throw 'Dependency closure changed; review the transition scope.' }
        $expected = @($projects | Where-Object { -not $excluded.Contains($_) } | Sort-Object)
        $actual = @($filter.solution.projects | ForEach-Object { $_.Replace('\', '/') } | Sort-Object)
        # P1 新增專案仍須全部納入；精確差集與固定三項排除都保留，不再以 P0 的23掩蓋新專案。
        if ($actual.Count -ne $expected.Count -or @($actual | Select-Object -Unique).Count -ne $actual.Count -or @(Compare-Object $expected $actual).Count -gt 0) { throw 'Filter does not preserve exactly the eligible projects.' }
    }
    Coverage_IncludesBothCoreProductTargets = {
        # 評估 workflow 真正使用的矩陣宣告，不以字串存在代替目標／專案配對驗證。
        $run = (Get-RunBlock 'Coverage for each new module and target').Groups['run'].Value -replace '(?m)^          ', ''
        $tokens = $null
        $parseErrors = $null
        $ast = [Management.Automation.Language.Parser]::ParseInput($run, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count -gt 0) { throw 'Coverage run block is invalid.' }
        $assignment = $ast.Find({
            param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
                $node.Left.VariablePath.UserPath -eq 'cases'
        }, $true)
        if ($null -eq $assignment) { throw 'Coverage matrix is missing.' }
        $matrix = & ([scriptblock]::Create($assignment.Extent.Text + '; $cases'))
        $core = @($matrix | Where-Object { $_.Module -ceq 'ToolUtility.Core' })
        if ($core.Count -ne 2 -or @($core | Where-Object { $_.Project -cne 'ToolUtility.Tests' }).Count -gt 0) {
            throw 'Core coverage must use its complete test project for both targets.'
        }
        $frameworks = @($core | ForEach-Object { $_.Framework } | Sort-Object)
        if (@(Compare-Object @('net10.0', 'net48') $frameworks).Count -gt 0) {
            throw 'Core coverage must independently execute net48 and net10.0.'
        }
    }
    Commands_UseFilteredSolution = {
        foreach ($name in @('Restore', 'Build without signing key', 'Release solution tests and coverage')) {
            $script:calls.Clear()
            $run = (Get-RunBlock $name).Groups['run'].Value -replace '(?m)^          ', ''
            & ([scriptblock]::Create($run))
            if ($script:calls.Count -eq 0 -or $script:calls[0][1] -ne $filterName) { throw ('Wrong solution executed by ' + $name) }
        }
    }
    Tracer_DisabledButRetainsExecutableGuard = {
        $block = Get-RunBlock 'Debug tracer contracts'
        if ($block.Groups['meta'].Value -notmatch 'if:.*false' -or $block.Groups['run'].Value -notmatch 'Test-TrxExecution') { throw 'Dependent tracer must remain disabled with its guard preserved.' }
    }
    Summary_DisclosesAllExcludedProjects = {
        $block = Get-RunBlock 'CI transition scope'
        if ($block.Groups['meta'].Value -notmatch 'if: always\(\)') { throw 'Scope must be visible even on failure.' }
        $previous = $env:GITHUB_STEP_SUMMARY
        $temporary = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-ci-summary-' + [Guid]::NewGuid().ToString('N') + '.md')
        try {
            $env:GITHUB_STEP_SUMMARY = $temporary
            & ([scriptblock]::Create(($block.Groups['run'].Value -replace '(?m)^          ', '')))
            $text = [IO.File]::ReadAllText($temporary)
            foreach ($name in @('SpeechMessageProducts.ChurchReport', 'ChurchReport.MemberInfo.Tests', 'ToolUtility.Dataverse.Tests')) { if (-not $text.Contains($name)) { throw 'Excluded project missing from summary.' } }
            if ($text -notmatch 'Transitional' -or $text -notmatch 'not a full solution') { throw 'Partial coverage must be explicit.' }
        } finally {
            $env:GITHUB_STEP_SUMMARY = $previous
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        }
    }
    NugetConfig_DoesNotDeclareMachineSpecificSource = {
        [xml]$config = [IO.File]::ReadAllText((Join-Path $root 'SpeechMessageProducts.ChurchReport/NuGet.config'))
        if (@($config.SelectNodes('//packageSources/add')).Count -ne 0) { throw 'Repository must not declare a private or machine-specific source.' }
    }
}
$previousLocation = (Get-Location).Path
$failed = 0
try {
    Set-Location -LiteralPath $root
    foreach ($case in $cases.GetEnumerator()) {
        try { & $case.Value; Write-Output ('PASS ' + $case.Key) }
        catch { $failed++; Write-Output ('FAIL ' + $case.Key + ': ' + $_.Exception.GetType().Name) }
    }
} finally { Set-Location -LiteralPath $previousLocation }
Write-Output ('CI transition tests: ' + ($cases.Count - $failed) + '/' + $cases.Count)
if ($failed -gt 0) { exit 1 }
exit 0
