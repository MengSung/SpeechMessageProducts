<#
檔案整體說明

目的與定位：以合成Cobertura驗證每模組每target的80%門檻、缺失／重複模組及損壞XML拒絕；斷言明確原因及結束碼，不把任意工具錯誤當負向成功。

主要功能與流程：
依下方原生宣告執行本檔所述工作；入口、條件及順序須與實際呼叫端一致，不能把示範或預覽視為已套用。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
<#
使用固定合成 Cobertura 資料驗證 80% 邊界、缺失／重複模組及損壞 XML 拒絕。
每個案例用獨立檔案和子程序；XML 不可解析外部實體，finally 逐一刪除已知合成檔案。
斷言結束碼與指定診斷，不把任意執行錯誤當成成功的負向測試。
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$tool = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Test-Coverage.ps1'))
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $tempRoot ('speechmessage-coverage-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixtureRoot
$utf8 = New-Object Text.UTF8Encoding($false)
$package = '<package name="New.Module" line-rate="{0}" />'
$cases = @(
    @{ Name='Exactly80_Passes'; Xml=('<coverage><packages>' + ($package -f '0.80') + '</packages></coverage>'); Code=0; Expected='Coverage New.Module: 80.00%' },
    @{ Name='Above80_Passes'; Xml=('<coverage><packages>' + ($package -f '0.95') + '</packages></coverage>'); Code=0; Expected='Coverage New.Module: 95.00%' },
    @{ Name='Below80_Fails'; Xml=('<coverage><packages>' + ($package -f '0.79999') + '</packages></coverage>'); Code=1; Expected='Coverage below 80 percent.' },
    @{ Name='WrongModule_Fails'; Xml='<coverage><packages><package name="Legacy" line-rate="1" /></packages></coverage>'; Code=1; Expected='Expected exactly one required module.' },
    @{ Name='DuplicateModule_Fails'; Xml=('<coverage><packages>' + ($package -f '1') + ($package -f '1') + '</packages></coverage>'); Code=1; Expected='Expected exactly one required module.' },
    @{ Name='MissingReport_Fails'; Xml=$null; Code=1; Expected='Coverage report is missing.' },
    @{ Name='MalformedXml_Fails'; Xml='<coverage>'; Code=1; Expected='Coverage report is invalid.' },
    @{ Name='ExternalEntity_Fails'; Xml='<!DOCTYPE coverage [<!ENTITY x SYSTEM "file:///never-read">]><coverage>&x;</coverage>'; Code=1; Expected='Coverage report is invalid.' },
    @{ Name='NaN_Fails'; Xml=('<coverage><packages>' + ($package -f 'NaN') + '</packages></coverage>'); Code=1; Expected='Coverage rate is invalid.' },
    @{ Name='Over100_Fails'; Xml=('<coverage><packages>' + ($package -f '1.01') + '</packages></coverage>'); Code=1; Expected='Coverage rate is invalid.' },
    @{ Name='CommaRate_Fails'; Xml=('<coverage><packages>' + ($package -f '0,8') + '</packages></coverage>'); Code=1; Expected='Coverage rate is invalid.' }
)
$failures = 0
try {
    foreach ($case in $cases) {
        $file = Join-Path $fixtureRoot ($case.Name + '.xml')
        if ($null -ne $case.Xml) { [IO.File]::WriteAllText($file, $case.Xml, $utf8) }
        $output = @(& powershell -NoProfile -File $tool -ReportPath $file -Module 'New.Module' 2>&1)
        $code = $LASTEXITCODE
        if ($code -eq $case.Code -and ($output -join "`n").Contains($case.Expected)) { Write-Output ('PASS ' + $case.Name) }
        else { $failures++; Write-Output ('FAIL ' + $case.Name + ': coverage contract not satisfied') }
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $boundary = $tempRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolved)).StartsWith('speechmessage-coverage-', [StringComparison]::Ordinal)) { throw 'Unsafe fixture cleanup.' }
    foreach ($case in $cases) {
        $file = Join-Path $resolved ($case.Name + '.xml')
        if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
    }
    [IO.Directory]::Delete($resolved, $false)
}
Write-Output ('Coverage tests: total=' + $cases.Count + ' failed=' + $failures)
if ($failures -gt 0) { exit 1 }
exit 0
