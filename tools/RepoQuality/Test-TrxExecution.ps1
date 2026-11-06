<#
檔案整體說明

目的與定位：確認 TRX 確實執行成功測試，拒絕 dotnet test 零測試仍回傳零的假綠燈。

主要功能與流程：
依下方原生宣告執行本檔所述工作；入口、條件及順序須與實際呼叫端一致，不能把示範或預覽視為已套用。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
<#
.SYNOPSIS
確認 TRX 確實執行成功測試，拒絕 dotnet test 零測試仍回傳零的假綠燈。
.DESCRIPTION
只讀取有界 TRX，禁止 DTD／外部解析；reader 由本次呼叫擁有並在成功及例外路徑 Dispose。
計數必須為非負整數、實際執行大於零、全部通過，且與真實結果列數相符。
輸出只有固定診斷與筆數，不輸出 XML、測試內容、帳密或原始例外。
#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReportPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$diagnostic = 'InvalidTrx'
try {
    if ([IO.Path]::GetExtension($ReportPath) -ine '.trx' -or -not [IO.File]::Exists($ReportPath)) { throw 'Invalid TRX path.' }
    $settings = New-Object Xml.XmlReaderSettings
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = 4194304
    $document = New-Object Xml.XmlDocument
    $document.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create([IO.Path]::GetFullPath($ReportPath), $settings)
    try { $document.Load($reader) } finally { $reader.Dispose() }
    $counters = @($document.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']"))
    if ($counters.Count -ne 1) { throw 'Expected exactly one counter set.' }
    $values = @{}
    foreach ($name in @('total','executed','passed','failed')) {
        $value = 0
        if (-not [int]::TryParse($counters[0].GetAttribute($name), [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$value)) { throw 'Invalid counter.' }
        $values[$name] = $value
    }
    if ($values.executed -eq 0) { $diagnostic = 'ZeroExecutedTests'; throw 'No tests executed.' }
    if ($values.executed -gt $values.total -or $values.passed -ne $values.executed -or $values.failed -ne 0) {
        $diagnostic = 'FailedOrInconsistentExecution'
        throw 'Execution did not fully pass.'
    }
    $results = @($document.SelectNodes("/*[local-name()='TestRun']/*[local-name()='Results']/*[local-name()='UnitTestResult']"))
    if ($results.Count -ne $values.executed -or @($results | Where-Object { $_.GetAttribute('outcome') -cne 'Passed' }).Count -ne 0) {
        $diagnostic = 'MissingOrInconsistentResults'
        throw 'Counter and result rows differ.'
    }
    Write-Output ('TRX contract: executed=' + $values.executed + ' passed=' + $values.passed)
    exit 0
} catch {
    Write-Output ('FAIL TRX contract: ' + $diagnostic)
    exit 1
}
