<#
檔案整體說明

目的與定位：各專案各框架獨立檢查 Coverlet 的模組行覆蓋率，門檻固定為 80%。

主要功能與流程：
依下方原生宣告執行本檔所述工作；入口、條件及順序須與實際呼叫端一致，不能把示範或預覽視為已套用。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
<#
.SYNOPSIS
各專案各框架獨立檢查 Coverlet 的模組行覆蓋率，門檻固定為 80%。
.DESCRIPTION
呼叫端必須提供當次測試的單一 Cobertura 報告，不合併不同框架或不同測試專案的結果。
缺少報告、模組重複、無效比例均拒絕；比例採固定文化解析，判斷前不四捨五入。
XML 禁用 DTD 與外部實體，最多 32 MiB 字元；reader 由單次呼叫擁有並在解析後確定釋放。
不連外、不保存任何使用者或教會資料，不輸出解析例外中的檔案內容。
#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReportPath, [Parameter(Mandatory=$true)][string]$Module)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not [IO.File]::Exists($ReportPath)) { Write-Output 'Coverage report is missing.'; exit 1 }
try {
    $settings = New-Object Xml.XmlReaderSettings
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = 32 * 1024 * 1024
    $document = New-Object Xml.XmlDocument
    $document.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create([IO.Path]::GetFullPath($ReportPath), $settings)
    try { $document.Load($reader) } finally { $reader.Dispose() }
} catch { Write-Output 'Coverage report is invalid.'; exit 1 }
$packages = @($document.SelectNodes('/coverage/packages/package') | Where-Object {
    [string]::Equals($_.GetAttribute('name'), $Module, [StringComparison]::Ordinal)
})
if ($packages.Count -ne 1) { Write-Output 'Expected exactly one required module.'; exit 1 }
$rate = [decimal]0
if (-not [decimal]::TryParse($packages[0].GetAttribute('line-rate'), [Globalization.NumberStyles]::AllowDecimalPoint,
        [Globalization.CultureInfo]::InvariantCulture, [ref]$rate) -or $rate -lt 0 -or $rate -gt 1) {
    Write-Output 'Coverage rate is invalid.'
    exit 1
}
if ($rate -lt [decimal]0.8) { Write-Output 'Coverage below 80 percent.'; exit 1 }
Write-Output ('Coverage ' + $Module + ': ' + ($rate * 100).ToString('F2', [Globalization.CultureInfo]::InvariantCulture) + '%')
exit 0
