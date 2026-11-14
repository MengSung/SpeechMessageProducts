<#
檔案整體說明
目的與定位：P5-02 PluginHarness 的離線驗收腳本；只使用合成輸出與操作者指定的舊方案根目錄。
主要功能與流程：執行兩次 harness、比較候選 SHA-256，驗證三筆 attendance、秘密掃描、網路禁止摘要及輸出檔邊界。
維護與驗證要求：完成後執行兩次輸出比對與文件／位元組檢查。
使用邊界與維護：本檔不連線、不修改舊來源，不讀取或顯示秘密；暫存候選由本次測試擁有，finally 清理自有檔案，失敗不發布部分結果。
#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$HarnessPath,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$dir=[IO.Path]::GetFullPath($OutputDirectory);[IO.Directory]::CreateDirectory($dir)|Out-Null
$a=Join-Path $dir 'candidate-a.json';$b=Join-Path $dir 'candidate-b.json'
try {
 foreach($out in @($a,$b)){& $HarnessPath --source-root $SourceRoot --output $out | Out-Null;if($LASTEXITCODE -ne 0){throw 'Harness execution failed.'}}
 $ha=(Get-FileHash $a -Algorithm SHA256).Hash;$hb=(Get-FileHash $b -Algorithm SHA256).Hash
 if($ha -ne $hb){throw 'Harness output is not deterministic.'}
 $json=Get-Content $a -Raw | ConvertFrom-Json
 if($json.externalServicesExecuted -ne $false -or $json.secretScan -ne 'passed' -or $json.networkApiPolicy -ne 'refuse-before-compile'){throw 'Safety contract failed.'}
 if(@($json.attendance).Count -lt 3){throw 'Attendance golden count is below three.'}
 if([regex]::IsMatch((Get-Content $a -Raw),'(?i)(bearer\s+|access[_ -]?token|channel[_ -]?secret|(?<![A-Za-z0-9])[UCR][0-9a-f]{32}(?![A-Za-z0-9]))')){throw 'Secret pattern reached output.'}
 [pscustomobject]@{deterministic=$true;attendanceCount=@($json.attendance).Count;generated=@($json.projects|? classification -eq Generated).Count;compileFailed=@($json.projects|? classification -eq CompileFailed).Count;blockedByNetworkApi=@($json.projects|? classification -eq BlockedByNetworkApi).Count} | ConvertTo-Json -Compress
} finally {Remove-Item -LiteralPath $a,$b -Force -ErrorAction SilentlyContinue}


