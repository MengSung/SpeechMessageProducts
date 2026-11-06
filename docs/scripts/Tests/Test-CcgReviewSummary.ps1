<#
檔案整體說明

目的與定位：以合成後端結果驗證reviewMode、requestedBackends及dualModelComplete等摘要契約，防止exit code零或單邊成功掩蓋未完成審查。

主要功能與流程：
操作入口包含Assert-Condition；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
param([string]$RunnerPath = (Join-Path $PSScriptRoot '..\Invoke-CcgDualModelWithSelfHealing.ps1'))

$ErrorActionPreference = 'Stop'
# 只載入摘要純函式；不可執行 runner 主流程，以免測試呼叫模型、變更 PATH 或留下子程序。
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path -LiteralPath $RunnerPath).Path, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw 'Runner parsing failed.' }
foreach ($name in @('Get-CcgReviewCompletion', 'ConvertTo-CcgRunMarkdown')) {
    $definition = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $false)
    if (-not $definition) { throw "Missing summary function: $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

# 固定期望值分別保護要求完成、雙模型完成及文字產物，不從被測函式推導期望。
$cases = [ordered]@{
    Dual_AllRequestedCompleted_IsDualComplete = {
        $actual = Get-CcgReviewCompletion -CompletedBackends @('gemini', 'claude')
        Assert-Condition ($actual.ok -and $actual.reviewMode -eq 'dual' -and $actual.dualModelComplete) 'Dual completion contract failed.'
    }
    ClaudeOnly_Completed_IsOkButNotDual = {
        $actual = Get-CcgReviewCompletion -ClaudeOnly -CompletedBackends @('claude')
        Assert-Condition ($actual.ok -and $actual.reviewMode -eq 'claude-only' -and -not $actual.dualModelComplete) 'Claude-only must never imply dual completion.'
    }
    Dual_OneCompleted_IsIncomplete = {
        $actual = Get-CcgReviewCompletion -CompletedBackends @('claude')
        Assert-Condition (-not $actual.ok -and -not $actual.dualModelComplete -and $actual.reviewMode -eq 'dual') 'A partial dual run must remain incomplete.'
    }
    ClaudeOnly_NoOutput_IsIncomplete = {
        $actual = Get-CcgReviewCompletion -ClaudeOnly
        Assert-Condition (-not $actual.ok -and -not $actual.dualModelComplete) 'No output cannot complete a review.'
    }
    Json_ContainsExplicitModeAndCompletion = {
        $actual = Get-CcgReviewCompletion -ClaudeOnly -CompletedBackends @('claude') | ConvertTo-Json | ConvertFrom-Json
        Assert-Condition ($actual.PSObject.Properties.Name -contains 'dualModelComplete') 'Missing dualModelComplete JSON field.'
        Assert-Condition ($actual.reviewMode -eq 'claude-only' -and $actual.ok -and -not $actual.dualModelComplete) 'Serialized review metadata is misleading.'
    }
    Markdown_ContainsModeAndDualCompletion = {
        $sample = [ordered]@{ runId='fixture'; role='reviewer'; requestedBackends=@('claude'); repositoryPath='X:/repo'; taskFile='request.md'; ok=$true; reviewMode='claude-only'; dualModelComplete=$false; degradedFallback=$false; fallbackAccepted=$false; quotaBlocked=$false; completedBackends=@('claude'); failedBackends=@(); runDirectory='X:/audit' }
        $text = ConvertTo-CcgRunMarkdown -Summary $sample
        Assert-Condition ($text.Contains('- ReviewMode: claude-only') -and $text.Contains('- DualModelComplete: False')) 'Markdown must expose explicit completion metadata.'
        Assert-Condition (-not [regex]::IsMatch($text, '(?m)[ \t]+\r?$')) 'Markdown must not contain trailing whitespace.'
    }
}
$failed = 0
foreach ($entry in $cases.GetEnumerator()) {
    try { & $entry.Value; Write-Output ("PASS " + $entry.Key) }
    catch { $failed++; Write-Output ("FAIL " + $entry.Key + ': ' + $_.Exception.Message) }
}
Write-Output ("Summary tests: " + ($cases.Count - $failed) + '/' + $cases.Count)
if ($failed -gt 0) { exit 1 }
