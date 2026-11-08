<#
檔案整體說明
目的：本機 P2-04 接續查核，只連 goodshepherdback；以既有部署 connector 借用一次 SDK。
流程：只讀加密狀態、追蹤、LINE 清理計數、組件與工作流程中繼資料；輸出去識別化摘要。
邊界：秘密只在 User Secrets provider 及本次記憶體，不輸出值／fault 原文；不讀取加密金鑰。
資源：finally Dispose lease/config；本檔不啟動背景工作或保存 credential；原始結果不進 Git。
#>
param([string]$Root,[string]$ResultFile,[switch]$Apply)
$ErrorActionPreference='Stop'; $lease=$null; $config=$null
try {
 $assembly=[Reflection.Assembly]::LoadFrom((Join-Path $Root 'SpeechMessage.Dynamics.Deployment/bin/Release/net10.0/SpeechMessage.Dynamics.Deployment.dll'))
 $config=$assembly.GetType('SpeechMessage.Dynamics.Deployment.DeploymentConfiguration').GetMethod('Create').Invoke($null,@($assembly))
 $connector=[Activator]::CreateInstance($assembly.GetType('SpeechMessage.Dynamics.Deployment.UserSecretsOrganizationConnector'),@($config))
 $lease=$connector.Connect('goodshepherdback'); $service=$lease.Service
 $prefix='Deployment:Organizations:goodshepherdback:'
 $tokenPresent=-not [string]::IsNullOrWhiteSpace($config[$prefix+'TestChannelToken']); $userPresent=-not [string]::IsNullOrWhiteSpace($config[$prefix+'TestLineUserId'])
 if(-not ($tokenPresent -and $userPresent)) { throw [InvalidOperationException]::new('missing test keys') }

 function Query-Rows([string]$Entity,[string[]]$Columns,[scriptblock]$Filter) {
  $q=[Microsoft.Xrm.Sdk.Query.QueryExpression]::new($Entity);$q.ColumnSet=[Microsoft.Xrm.Sdk.Query.ColumnSet]::new($Columns);$q.PageInfo.Count=5000;$q.PageInfo.PageNumber=1
  if($Filter){ & $Filter $q }; $all=[Collections.Generic.List[Microsoft.Xrm.Sdk.Entity]]::new()
  do {if($q.PageInfo.PageNumber -gt 20){throw [InvalidOperationException]::new('bounded paging exceeded')};$page=$service.RetrieveMultiple($q);$all.AddRange($page.Entities);$q.PageInfo.PageNumber++;$q.PageInfo.PagingCookie=$page.PagingCookie}while($page.MoreRecords)
  return ,$all
 }
 $source='D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.其他解決方案\Dynamics365Line'
 $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($file in Get-ChildItem -LiteralPath $source -Recurse -Filter '*.csproj') {
  [xml]$xml=[IO.File]::ReadAllText($file.FullName);$found=@($xml.Project.PropertyGroup.AssemblyName|Where-Object{$_});if($found.Count -eq 0){$null=$names.Add($file.BaseName)}else{foreach($name in $found){$null=$names.Add([string]$name)}}
 }
 foreach($name in @('AbundanceLineMessagePlugInDatabase','PresentRecordLinePlugInDatabase','TwoWingsPlugInDatabase','WeeklyReportLinePlugInDatabase')){$null=$names.Add($name)}
 $allAssemblies=Query-Rows 'pluginassembly' @('pluginassemblyid','name') $null
 $legacyAssemblies=@($allAssemblies|Where-Object{$names.Contains($_.GetAttributeValue[string]('name'))})
 $assemblyIds=[object[]]@($legacyAssemblies|ForEach-Object{$_.Id})
 $types=Query-Rows 'plugintype' @('plugintypeid','typename','pluginassemblyid') {param($q)$q.Criteria.AddCondition('pluginassemblyid',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::In,$assemblyIds)}
 $typeIds=[object[]]@($types|ForEach-Object{$_.Id})
 $steps=Query-Rows 'sdkmessageprocessingstep' @('sdkmessageprocessingstepid','statecode','statuscode','eventhandler','mode') {param($q)$q.Criteria.AddCondition('eventhandler',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::In,$typeIds)}
 $enabledSteps=@($steps|Where-Object{$_.GetAttributeValue[Microsoft.Xrm.Sdk.OptionSetValue]('statecode').Value -eq 0})
 $workflows=Query-Rows 'workflow' @('workflowid','xaml','statecode','statuscode','type','category','parentworkflowid') $null
 $pattern='(?<![A-Za-z0-9_])(?:'+((@($names)|ForEach-Object{[regex]::Escape($_)})-join '|')+')(?![A-Za-z0-9_])'
 $legacyWorkflows=@($workflows|Where-Object{$_.GetAttributeValue[string]('xaml') -match $pattern})
 $activeDefinitions=@($legacyWorkflows|Where-Object{$_.GetAttributeValue[Microsoft.Xrm.Sdk.OptionSetValue]('statecode').Value -eq 1 -and $_.GetAttributeValue[Microsoft.Xrm.Sdk.OptionSetValue]('type').Value -eq 1})
 $workflowIds=[object[]]@($legacyWorkflows|ForEach-Object{$_.Id});$ownerIds=[object[]]@(@($steps|ForEach-Object{$_.Id})+$workflowIds)
 $jobs=Query-Rows 'asyncoperation' @('asyncoperationid','statecode','statuscode','owningextensionid','workflowactivationid') {param($q)
  $q.Criteria.AddCondition('statecode',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::NotEqual,3)
  $filter=[Microsoft.Xrm.Sdk.Query.FilterExpression]::new([Microsoft.Xrm.Sdk.Query.LogicalOperator]::Or)
  if($ownerIds.Count -gt 0){$filter.AddCondition('owningextensionid',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::In,$ownerIds)}
  if($workflowIds.Count -gt 0){$filter.AddCondition('workflowactivationid',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::In,$workflowIds)}
  if($filter.Conditions.Count -eq 0){throw [InvalidOperationException]::new('empty legacy identity set')};$q.Criteria.AddFilter($filter)
 }
 $waiting=@($jobs|Where-Object{$_.GetAttributeValue[Microsoft.Xrm.Sdk.OptionSetValue]('statecode').Value -in @(0,1)})
 $running=@($jobs|Where-Object{$_.GetAttributeValue[Microsoft.Xrm.Sdk.OptionSetValue]('statecode').Value -eq 2})
 $backupFile=$null;$backupHash=$null
 if($Apply) {
  $backupRoot=Join-Path $env:LOCALAPPDATA ('SpeechMessageProducts/evidence/p2-04-preflight/'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'));[IO.Directory]::CreateDirectory($backupRoot)|Out-Null
  $backupFile=Join-Path $backupRoot 'legacy-quiesce-backup.dpapi'
  $all=[Microsoft.Xrm.Sdk.EntityCollection]::new();foreach($row in @($steps)+$legacyWorkflows+@($jobs)){$all.Entities.Add($row)}
  $serializer=[Runtime.Serialization.DataContractSerializer]::new([Microsoft.Xrm.Sdk.EntityCollection]);$stream=[IO.MemoryStream]::new()
  try {$serializer.WriteObject($stream,$all);$plain=$stream.ToArray();$protected=[Security.Cryptography.ProtectedData]::Protect($plain,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser);[IO.File]::WriteAllBytes($backupFile,$protected);$check=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($backupFile),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser);if(-not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals([Security.Cryptography.SHA256]::HashData($plain),[Security.Cryptography.SHA256]::HashData($check))){throw [InvalidOperationException]::new('backup verification failed')}}finally{if($plain){[Array]::Clear($plain)};if($check){[Array]::Clear($check)};$stream.Dispose()}
  $backupHash=(Get-FileHash -LiteralPath $backupFile -Algorithm SHA256).Hash
  foreach($row in $enabledSteps){$request=[Microsoft.Xrm.Sdk.OrganizationRequest]::new('SetState');$request['EntityMoniker']=$row.ToEntityReference();$request['State']=[Microsoft.Xrm.Sdk.OptionSetValue]::new(1);$request['Status']=[Microsoft.Xrm.Sdk.OptionSetValue]::new(2);$null=$service.Execute($request)}
  foreach($row in $activeDefinitions){$request=[Microsoft.Xrm.Sdk.OrganizationRequest]::new('SetState');$request['EntityMoniker']=$row.ToEntityReference();$request['State']=[Microsoft.Xrm.Sdk.OptionSetValue]::new(0);$request['Status']=[Microsoft.Xrm.Sdk.OptionSetValue]::new(1);$null=$service.Execute($request)}
  foreach($row in $waiting){$request=[Microsoft.Xrm.Sdk.OrganizationRequest]::new('SetState');$request['EntityMoniker']=$row.ToEntityReference();$request['State']=[Microsoft.Xrm.Sdk.OptionSetValue]::new(3);$request['Status']=[Microsoft.Xrm.Sdk.OptionSetValue]::new(32);$null=$service.Execute($request)}
 }
 $summary=[ordered]@{At=[DateTimeOffset]::Now.ToString('o');Applied=[bool]$Apply;LegacyAssemblies=$legacyAssemblies.Count;LegacyTypes=$types.Count;LegacySteps=$steps.Count;LegacyEnabledSteps=$enabledSteps.Count;LegacyWorkflowRecords=$legacyWorkflows.Count;LegacyActiveDefinitions=$activeDefinitions.Count;LegacyWaitingJobs=$waiting.Count;LegacyRunningJobs=$running.Count;BackupFile=$backupFile;BackupSha256=$backupHash}
 $json=$summary|ConvertTo-Json;[IO.File]::WriteAllText($ResultFile,$json.Replace("`r`n","`n").Replace("`n","`r`n")+"`r`n",[Text.UTF8Encoding]::new($false));$json
} catch {
 $e=$_.Exception;while($e.InnerException){$e=$e.InnerException}
 Write-Output ('AuditFailureLine='+$_.InvocationInfo.ScriptLineNumber)
 Write-Output ('AuditFailureType='+$e.GetType().FullName)
 if($e -is [System.ServiceModel.FaultException[Microsoft.Xrm.Sdk.OrganizationServiceFault]]){Write-Output ('AuditFaultCode=0x'+$e.Detail.ErrorCode.ToString('X8'))}
 exit 1
} finally {if($lease){$lease.Dispose()};if($config -is [IDisposable]){$config.Dispose()}}
