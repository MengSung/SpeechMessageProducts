<#
檔案整體說明
目的：P2-04 前置的單一寫入者，以既有部署 CLI 進行有備份的 plan/apply/verify/smoke。
流程：保存原 manifest、追蹤開全、使用測試 Secrets 佔位；只允許既有組件與同身分步驟更新。
清理：finally 必定復原 manifest、停用／trace／空 secure，再 plan/apply/verify=0 及追蹤回例外。
邊界：只用 goodshepherdback；不重試失敗推播、不回顯 Secrets；CLI 只有固定摘要與數字 fault。
資源：外部 CLI 同步等候退出才下一命令，沒有背景工作；操作摘要／原始輸出留本機不進 Git。
#>
param([string]$Root,[string]$Evidence)
$ErrorActionPreference='Stop';$BaseEvidence=$Evidence;$Evidence=Join-Path $Evidence ('retry-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'));[IO.Directory]::CreateDirectory($Evidence)|Out-Null;$dll=Join-Path $Root 'SpeechMessage.Dynamics.Deployment/bin/Release/net10.0/SpeechMessage.Dynamics.Deployment.dll';$manifest=Join-Path $Root 'd365/registration/plugins.registration.json';$orgRoot=Join-Path $Root 'artifacts/d365/goodshepherdback';$original=[IO.File]::ReadAllBytes($manifest);$utf8=[Text.UTF8Encoding]::new($false)
$summary=[ordered]@{StartedAt=[DateTimeOffset]::Now.ToString('o');Plan=$null;Apply=$null;VerifyPush=$null;SmokeStartedAt=$null;Smoke=$null;CleanupPlan=$null;CleanupApply=$null;CleanupVerify=$null;TraceRestored=$false;ManifestRestored=$false;FailureType=$null}
function Invoke-Cli([string]$Name,[string[]]$Arguments){
 Write-Output ('P204Stage='+$Name);$log=Join-Path $Evidence ($Name+'.log'); & dotnet $dll @Arguments > $log 2>&1; $script:CliExit=$LASTEXITCODE;Write-Output ($Name+'Exit='+$script:CliExit)
}
function Get-NewPlan([datetime]$Since){
 $plans=@(Get-ChildItem -LiteralPath $orgRoot -Directory | Where-Object {$_.LastWriteTimeUtc -ge $Since.AddSeconds(-2)} | ForEach-Object {$f=Join-Path $_.FullName 'plan.json';if(Test-Path -LiteralPath $f){Get-Item -LiteralPath $f}} | Sort-Object LastWriteTimeUtc -Descending)
 if($plans.Count -eq 0){throw [InvalidOperationException]::new('missing plan')};$file=$plans[0];$plan=[IO.File]::ReadAllText($file.FullName)|ConvertFrom-Json
 if($plan.Organization -cne 'goodshepherdback'){throw [InvalidOperationException]::new('organization mismatch')}
 foreach($op in $plan.Operations){if($op.Kind -notin @('UpdateAssembly','UpdateStepConfiguration','UpdateStepState')){throw [InvalidOperationException]::new('unexpected plan operation')}}
 [Console]::WriteLine('ReviewedOperations='+(@($plan.Operations|ForEach-Object{$_.Kind})-join ','));return $file.FullName
}
try{
 [IO.File]::WriteAllBytes((Join-Path $Evidence 'manifest-before.json'),$original)
 & (Join-Path $BaseEvidence 'resume-live-audit.ps1') -Root $Root -ResultFile (Join-Path $Evidence 'trace-all.json') -Operation TraceAll
 if((Get-Content (Join-Path $Evidence 'trace-all.json') -Raw|ConvertFrom-Json).TraceAfter -ne 2){throw [InvalidOperationException]::new('trace setup failed')}
 $m=[Text.Encoding]::UTF8.GetString($original)|ConvertFrom-Json
 $m.steps[0].unsecureConfig='{"mode":"push","culture":"zh-TW","lineUserId":"${secret:TestLineUserId}"}'
 $m.steps[0].secureConfig='${secret:TestChannelToken}';$m.steps[0].enabledByOrganization.goodshepherdback=$true
 [IO.File]::WriteAllText($manifest,(($m|ConvertTo-Json -Depth 12).Replace("`r`n","`n").Replace("`n","`r`n")+"`r`n"),$utf8)
 $since=[DateTime]::UtcNow;Invoke-Cli 'retry-plan' @('plan','--org','goodshepherdback');$summary.Plan=$script:CliExit;if($script:CliExit -ne 0){throw [InvalidOperationException]::new('plan failed')}
 $plan=Get-NewPlan $since;[IO.File]::WriteAllText((Join-Path $Evidence 'reviewed-push-plan-path.txt'),$plan+"`r`n",$utf8)
 Invoke-Cli 'retry-apply' @('apply','--org','goodshepherdback','--plan',$plan);$summary.Apply=$script:CliExit;if($script:CliExit -ne 0){throw [InvalidOperationException]::new('apply failed')}
 Invoke-Cli 'retry-verify-push' @('verify','--org','goodshepherdback');$summary.VerifyPush=$script:CliExit;if($script:CliExit -ne 3){throw [InvalidOperationException]::new('push metadata mismatch')}
 $summary.SmokeStartedAt=[DateTimeOffset]::Now.ToString('o');[IO.File]::WriteAllText((Join-Path $Evidence 'smoke-start-utc.txt'),[DateTimeOffset]::UtcNow.ToString('o')+"`r`n",$utf8)
 Invoke-Cli 'retry-smoke-push' @('smoke','--org','goodshepherdback','--scenario','p0-ping-push');$summary.Smoke=$script:CliExit
} catch { $summary.FailureType=$_.Exception.GetType().FullName;Write-Output ('P204FailureType='+$summary.FailureType) }
finally {
 [IO.File]::WriteAllBytes($manifest,$original);$summary.ManifestRestored=$true
 try {
  $since=[DateTime]::UtcNow;Invoke-Cli 'cleanup-plan' @('plan','--org','goodshepherdback');$summary.CleanupPlan=$script:CliExit;if($script:CliExit -ne 0){throw [InvalidOperationException]::new('cleanup plan failed')};$plan=Get-NewPlan $since
  Invoke-Cli 'cleanup-apply' @('apply','--org','goodshepherdback','--plan',$plan);$summary.CleanupApply=$script:CliExit;if($script:CliExit -ne 0){throw [InvalidOperationException]::new('cleanup apply failed')}
  Invoke-Cli 'cleanup-verify' @('verify','--org','goodshepherdback');$summary.CleanupVerify=$script:CliExit
 }catch{Write-Output ('P204CleanupFailureType='+$_.Exception.GetType().FullName)}
 try {& (Join-Path $BaseEvidence 'resume-live-audit.ps1') -Root $Root -ResultFile (Join-Path $Evidence 'trace-restored.json') -Operation TraceException; $summary.TraceRestored=((Get-Content (Join-Path $Evidence 'trace-restored.json') -Raw|ConvertFrom-Json).TraceAfter -eq 1)}catch{Write-Output ('P204TraceFailureType='+$_.Exception.GetType().FullName)}
 $summary['CompletedAt']=[DateTimeOffset]::Now.ToString('o');[IO.File]::WriteAllText((Join-Path $Evidence 'retry-summary.json'),(($summary|ConvertTo-Json -Depth 6).Replace("`r`n","`n").Replace("`n","`r`n")+"`r`n"),$utf8);$summary|ConvertTo-Json -Depth 6
}
