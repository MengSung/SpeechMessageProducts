<#
檔案整體說明
目的與定位：提供 P5-02 正式 generate 管線的離線重現檢查，使用操作者核定舊來源與合成情境，不把原型 attendance 當作黃金。
主要功能與流程：在輸出目錄建立本次唯一子目錄，兩次執行同一入口，核對 SHA-256、Generated、案例名稱及筆數，以及至少一筆寫入、LINE 記錄或活動輸出；秘密掃描委派正式 GoldenWriter。
使用邊界與維護：不連 D365 或 LINE，不表示工作包已驗收。子程序輸出由同步呼叫排空，程序生命週期由 harness 自己結束；finally 只清除此呼叫擁有的兩個候選與空目錄，不刪操作者既有輸出。此腳本用於已核定可信來源，不是敵意程式沙箱；逾時及域卸載由正式 runner 執行。
#>
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][Alias('SourceRoot')][string]$LegacyRoot,
 [Parameter(Mandatory=$true)][string]$Module,
 [Parameter(Mandatory=$true)][string]$Scenarios,
 [Parameter(Mandatory=$true)][string]$HarnessPath,
 [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$work=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('plugin-harness-check-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work)|Out-Null
$a=Join-Path $work 'candidate-a.json'
$b=Join-Path $work 'candidate-b.json'
try {
 foreach($destination in @($a,$b)) {
  & $HarnessPath generate --legacy-root $LegacyRoot --module $Module --scenarios $Scenarios --out $destination | Out-Null
  if($LASTEXITCODE -ne 0){throw 'Harness execution failed.'}
 }
 if((Get-FileHash -LiteralPath $a -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $b -Algorithm SHA256).Hash){throw 'Harness output is not deterministic.'}
 $utf8=New-Object Text.UTF8Encoding($false,$true)
 $candidate=[IO.File]::ReadAllText($a,$utf8)|ConvertFrom-Json
 $inputDocument=[IO.File]::ReadAllText($Scenarios,$utf8)|ConvertFrom-Json
 $cases=@($candidate.cases)
 $expectedCases=@($inputDocument.cases)
 if($candidate.status -ne 'Generated' -or $cases.Count -eq 0 -or $cases.Count -ne $expectedCases.Count){throw 'Generated case contract failed.'}
 $effects=0
 for($index=0;$index -lt $cases.Count;$index++) {
  $case=$cases[$index]
  if($case.name -cne $expectedCases[$index].name){throw 'Generated case identity failed.'}
  $effects+=@($case.writes).Count+@($case.lineCalls).Count
  if($null -ne $case.outputs){$effects+=@($case.outputs.PSObject.Properties).Count}
 }
 if($effects -eq 0){throw 'No observable legacy effect was recorded.'}
 [pscustomobject]@{deterministic=$true;caseCount=$cases.Count;observableEffects=$effects}|ConvertTo-Json -Compress
} finally {
 foreach($candidatePath in @($a,$b)){if(Test-Path -LiteralPath $candidatePath){[IO.File]::Delete($candidatePath)}}
 [IO.Directory]::Delete($work,$false)
}
