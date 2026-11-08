<#
檔案整體說明
目的：只在 goodshepherdback 核對本輪兩個 smoke 的正式 trace 身分、診斷組件、同一步驟與殘留資源。
流程：既有 connector 單次 lease；以 evidence 的 trace/correlation GUID 交集核對；備份後才可清除自己已完成的 trace／作業。
邊界：不掃描其他人的作業、不輸出 trace 原文或 secrets；機敏備份使用 CurrentUser DPAPI，放 repo 外並驗證摘要。
生命週期：SDK lease/config、MemoryStream 都由本次 finally Dispose；操作同步、分頁有上限，不留背景工作。
#>
param([string]$Root,[string]$Evidence,[string]$ResultFile,[switch]$Cleanup)
$ErrorActionPreference='Stop';$lease=$null;$config=$null
try {
 $a=[Reflection.Assembly]::LoadFrom((Join-Path $Root 'SpeechMessage.Dynamics.Deployment/bin/Release/net10.0/SpeechMessage.Dynamics.Deployment.dll'))
 $config=$a.GetType('SpeechMessage.Dynamics.Deployment.DeploymentConfiguration').GetMethod('Create').Invoke($null,@($a))
 $connector=[Activator]::CreateInstance($a.GetType('SpeechMessage.Dynamics.Deployment.UserSecretsOrganizationConnector'),@($config));$lease=$connector.Connect('goodshepherdback');$service=$lease.Service
 function Rows([string]$Entity,[string[]]$Columns,[scriptblock]$Filter){
  Write-Output ('AuditQuery='+$Entity) | Out-Host
  $q=[Microsoft.Xrm.Sdk.Query.QueryExpression]::new($Entity);$q.ColumnSet=[Microsoft.Xrm.Sdk.Query.ColumnSet]::new($Columns);$q.PageInfo.Count=5000;$q.PageInfo.PageNumber=1
  if($Filter){& $Filter $q};$result=[Collections.Generic.List[Microsoft.Xrm.Sdk.Entity]]::new()
  do{if($q.PageInfo.PageNumber -gt 20){throw [InvalidOperationException]::new('paging exceeded')};$page=$service.RetrieveMultiple($q);$result.AddRange($page.Entities);$q.PageInfo.PageNumber++;$q.PageInfo.PagingCookie=$page.PagingCookie}while($page.MoreRecords);return ,$result
 }
 $assemblies=Rows 'pluginassembly' @('pluginassemblyid','content','modifiedon','version') {param($q)$q.Criteria.AddCondition('name',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal,'SpeechMessage.Dynamics.Plugins')}
 if($assemblies.Count -ne 1){throw [InvalidOperationException]::new('assembly identity conflict')}
 $image=[Convert]::FromBase64String($assemblies[0].GetAttributeValue[string]('content'));$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($image));[Array]::Clear($image)
 $types=Rows 'plugintype' @('plugintypeid') {param($q)$q.Criteria.AddCondition('pluginassemblyid',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal,$assemblies[0].Id);$q.Criteria.AddCondition('typename',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal,'SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin')}
 if($types.Count -ne 1){throw [InvalidOperationException]::new('type identity conflict')}
 $steps=Rows 'sdkmessageprocessingstep' @('sdkmessageprocessingstepid','statecode','configuration','sdkmessageprocessingstepsecureconfigid') {param($q)$q.Criteria.AddCondition('eventhandler',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal,$types[0].Id)}
 if($steps.Count -ne 1){throw [InvalidOperationException]::new('step identity conflict')};$step=$steps[0]
 $orgId=[Guid]$service.Execute([Microsoft.Xrm.Sdk.OrganizationRequest]::new('WhoAmI')).Results['OrganizationId'];$org=$service.Retrieve('organization',$orgId,[Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('plugintracelogsetting')))
 $traceMode=$org.GetAttributeValue[Microsoft.Xrm.Sdk.OptionSetValue]('plugintracelogsetting').Value
 $safeRows=@();foreach($f in Get-ChildItem -LiteralPath $Evidence -Recurse -Filter 'smoke-safe-trace.json'){$safeRows+=@((Get-Content -LiteralPath $f.FullName -Raw|ConvertFrom-Json).Rows)}
 $traceIds=[object[]]@($safeRows|ForEach-Object{[Guid]$_.TraceId}|Select-Object -Unique);$corrIds=[object[]]@($safeRows|ForEach-Object{[Guid]$_.CorrelationId}|Select-Object -Unique)
 if($traceIds.Count -ne 2 -or $corrIds.Count -ne 2){throw [InvalidOperationException]::new('unexpected owned run count')}
 $traces=Rows 'plugintracelog' @('plugintracelogid','correlationid','typename','messageblock','exceptiondetails','createdon') {param($q)$c=[Microsoft.Xrm.Sdk.Query.ConditionExpression]::new('plugintracelogid',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::In);foreach($id in $traceIds){$c.Values.Add([Guid]::Parse($id.ToString()))};$q.Criteria.AddCondition($c)}
 foreach($t in $traces){if($t.GetAttributeValue[string]('typename') -cne 'SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin' -or $t.GetAttributeValue[Guid]('correlationid') -notin $corrIds){throw [InvalidOperationException]::new('trace ownership mismatch')}}
 $jobs=Rows 'asyncoperation' @('asyncoperationid','statecode','statuscode','correlationid','owningextensionid') {param($q)$c=[Microsoft.Xrm.Sdk.Query.ConditionExpression]::new('correlationid',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::In);foreach($id in $corrIds){$c.Values.Add([Guid]::Parse($id.ToString()))};$q.Criteria.AddCondition($c);$q.Criteria.AddCondition('owningextensionid',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal,$step.Id)}
 $unfinished=@($jobs|Where-Object{$_.GetAttributeValue[Microsoft.Xrm.Sdk.OptionSetValue]('statecode').Value -ne 3})
 $tasks=Rows 'task' @('activityid') {param($q)$q.Criteria.AddCondition('subject',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::BeginsWith,'[smoke] P0-')}
 $backupPath=$null;$backupHash=$null;$deletedTraces=0;$deletedJobs=0
 if($Cleanup){
  if($unfinished.Count -ne 0 -or $tasks.Count -ne 0 -or $traceMode -ne 1 -or $step.GetAttributeValue[Microsoft.Xrm.Sdk.OptionSetValue]('statecode').Value -ne 1){throw [InvalidOperationException]::new('unsafe cleanup precondition')}
  $owned=[Microsoft.Xrm.Sdk.EntityCollection]::new();foreach($row in @($traces)+@($jobs)){$owned.Entities.Add($row)}
  if($owned.Entities.Count -gt 0){
   $dir=Join-Path $env:LOCALAPPDATA ('SpeechMessageProducts/evidence/p2-04-preflight/'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'));[IO.Directory]::CreateDirectory($dir)|Out-Null;$backupPath=Join-Path $dir 'owned-smoke-resources.dpapi'
   $ms=[IO.MemoryStream]::new();$plain=$null;$check=$null
   try{[Runtime.Serialization.DataContractSerializer]::new([Microsoft.Xrm.Sdk.EntityCollection]).WriteObject($ms,$owned);$plain=$ms.ToArray();$protected=[Security.Cryptography.ProtectedData]::Protect($plain,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser);[IO.File]::WriteAllBytes($backupPath,$protected);$check=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($backupPath),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser);if(-not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals([Security.Cryptography.SHA256]::HashData($plain),[Security.Cryptography.SHA256]::HashData($check))){throw [InvalidOperationException]::new('backup check failed')}}finally{if($plain){[Array]::Clear($plain)};if($check){[Array]::Clear($check)};$ms.Dispose()}
   $backupHash=(Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash
   foreach($row in $jobs){$service.Delete('asyncoperation',$row.Id);$deletedJobs++};foreach($row in $traces){$service.Delete('plugintracelog',$row.Id);$deletedTraces++}
  }
 }
 $afterTraces=Rows 'plugintracelog' @('plugintracelogid') {param($q)$c=[Microsoft.Xrm.Sdk.Query.ConditionExpression]::new('plugintracelogid',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::In);foreach($id in $traceIds){$c.Values.Add([Guid]::Parse($id.ToString()))};$q.Criteria.AddCondition($c)}
 $afterJobs=Rows 'asyncoperation' @('asyncoperationid') {param($q)$c=[Microsoft.Xrm.Sdk.Query.ConditionExpression]::new('correlationid',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::In);foreach($id in $corrIds){$c.Values.Add([Guid]::Parse($id.ToString()))};$q.Criteria.AddCondition($c);$q.Criteria.AddCondition('owningextensionid',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal,$step.Id)}
 $r=[ordered]@{At=[DateTimeOffset]::Now.ToString('o');Organization='goodshepherdback';LiveAssemblyHash=$hash;LocalAssemblyHash=(Get-FileHash -LiteralPath (Join-Path $Root 'artifacts/plugins/SpeechMessage.Dynamics.Plugins.dll') -Algorithm SHA256).Hash;AssemblyModifiedOn=$assemblies[0].GetAttributeValue[DateTime]('modifiedon').ToString('o');StepId=$step.Id;StepDisabled=($step.GetAttributeValue[Microsoft.Xrm.Sdk.OptionSetValue]('statecode').Value -eq 1);TraceConfiguration=($step.GetAttributeValue[string]('configuration') -ceq '{"mode":"trace","culture":"zh-TW"}');SecureConfigPresent=$step.Contains('sdkmessageprocessingstepsecureconfigid');OrganizationTrace=$traceMode;SmokeTaskRows=$tasks.Count;UnfinishedOwnedJobs=$unfinished.Count;OwnedTraceRows=$afterTraces.Count;OwnedJobRows=$afterJobs.Count;DeletedTraces=$deletedTraces;DeletedJobs=$deletedJobs;BackupPath=$backupPath;BackupSha256=$backupHash;Cleanup=[bool]$Cleanup}
 $j=$r|ConvertTo-Json;[IO.File]::WriteAllText($ResultFile,$j.Replace("`r`n","`n").Replace("`n","`r`n")+"`r`n",[Text.UTF8Encoding]::new($false));$j
}catch{$e=$_.Exception;while($e.InnerException){$e=$e.InnerException};Write-Output ('AuditFailureLine='+$_.InvocationInfo.ScriptLineNumber);Write-Output ('AuditFailureType='+$e.GetType().FullName);if($e -is [System.ServiceModel.FaultException[Microsoft.Xrm.Sdk.OrganizationServiceFault]]){Write-Output ('AuditFaultCode=0x'+$e.Detail.ErrorCode.ToString('X8'))};exit 1}finally{if($lease){$lease.Dispose()};if($config -is [IDisposable]){$config.Dispose()}}
