<#
檔案整體說明
目的：本機 P2-04 接續查核，只連 goodshepherdback；以既有部署 connector 借用一次 SDK。
流程：只讀加密狀態、追蹤、LINE 清理計數、組件與工作流程中繼資料；輸出去識別化摘要。
邊界：秘密只在 User Secrets provider 及本次記憶體，不輸出值／fault 原文；不讀取加密金鑰。
資源：finally Dispose lease/config；本檔不啟動背景工作或保存 credential；原始結果不進 Git。
#>
param([string]$Root,[string]$ResultFile,[ValidateSet('Audit','TraceAll','TraceException')][string]$Operation='Audit')
$ErrorActionPreference='Stop'; $lease=$null; $config=$null
try {
 $assembly=[Reflection.Assembly]::LoadFrom((Join-Path $Root 'SpeechMessage.Dynamics.Deployment/bin/Release/net10.0/SpeechMessage.Dynamics.Deployment.dll'))
 $config=$assembly.GetType('SpeechMessage.Dynamics.Deployment.DeploymentConfiguration').GetMethod('Create').Invoke($null,@($assembly))
 $connector=[Activator]::CreateInstance($assembly.GetType('SpeechMessage.Dynamics.Deployment.UserSecretsOrganizationConnector'),@($config))
 $lease=$connector.Connect('goodshepherdback'); $service=$lease.Service
 $prefix='Deployment:Organizations:goodshepherdback:'
 $tokenPresent=-not [string]::IsNullOrWhiteSpace($config[$prefix+'TestChannelToken']); $userPresent=-not [string]::IsNullOrWhiteSpace($config[$prefix+'TestLineUserId'])
 if(-not ($tokenPresent -and $userPresent)) { throw [InvalidOperationException]::new('missing test keys') }
 $identity=$service.Execute([Microsoft.Xrm.Sdk.OrganizationRequest]::new('WhoAmI'))
 $orgId=[Guid]$identity.Results['OrganizationId']
 $encryption=$service.Execute([Microsoft.Xrm.Sdk.OrganizationRequest]::new('IsDataEncryptionActive'))
 if(-not $encryption.Results.Contains('IsActive') -or $encryption.Results['IsActive'] -isnot [bool]){throw [InvalidOperationException]::new('missing encryption status')}
 $org=$service.Retrieve('organization',$orgId,[Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('plugintracelogsetting')))
 $before=$org.GetAttributeValue[Microsoft.Xrm.Sdk.OptionSetValue]('plugintracelogsetting').Value
 if($Operation -ne 'Audit') {
  $expected=if($Operation -eq 'TraceAll'){2}else{1}
  $update=[Microsoft.Xrm.Sdk.Entity]::new('organization',$orgId);$update['plugintracelogsetting']=[Microsoft.Xrm.Sdk.OptionSetValue]::new($expected); $service.Update($update)
  $org=$service.Retrieve('organization',$orgId,[Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('plugintracelogsetting')))
  $after=$org.GetAttributeValue[Microsoft.Xrm.Sdk.OptionSetValue]('plugintracelogsetting').Value; if($after -ne $expected){throw [InvalidOperationException]::new('trace readback mismatch')}
  $summary=[ordered]@{At=[DateTimeOffset]::Now.ToString('o');Operation=$Operation;TraceBefore=$before;TraceAfter=$after}
 } else {
  function Query-Rows([string]$Entity,[string[]]$Columns,[scriptblock]$Filter) {
   $q=[Microsoft.Xrm.Sdk.Query.QueryExpression]::new($Entity);$q.ColumnSet=[Microsoft.Xrm.Sdk.Query.ColumnSet]::new($Columns);$q.PageInfo.Count=5000;$q.PageInfo.PageNumber=1
   if($Filter){ & $Filter $q }; $all=[Collections.Generic.List[Microsoft.Xrm.Sdk.Entity]]::new()
   do {$page=$service.RetrieveMultiple($q);$all.AddRange($page.Entities);$q.PageInfo.PageNumber++;$q.PageInfo.PagingCookie=$page.PagingCookie}while($page.MoreRecords)
   return ,$all
  }
  $contacts=Query-Rows 'contact' @('contactid','new_lineid') {param($q) $q.Criteria.AddCondition('new_lineid',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::NotNull)}
  $test=0;$nonTest=0;foreach($row in $contacts){$id=$row.GetAttributeValue[string]('new_lineid');if(-not [string]::IsNullOrEmpty($id)){if($id -ceq $config[$prefix+'TestLineUserId']){$test++}else{$nonTest++}}}
  $channels=Query-Rows 'new_linechannel' @('new_linechannelid') $null
  Write-Output 'AuditStage=letter-token-count'
  $letters=Query-Rows 'letter' @('activityid') {param($q) $q.Criteria.AddCondition('new_channel_access_token',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::NotNull)}
  $tasks=Query-Rows 'task' @('activityid') {param($q) $q.Criteria.AddCondition('subject',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::BeginsWith,'[smoke] P0-')}
  $assemblies=Query-Rows 'pluginassembly' @('pluginassemblyid','name','ismanaged') $null
  $assemblyNames=@($assemblies | ForEach-Object { $_.GetAttributeValue[string]('name') })
  $summary=[ordered]@{At=[DateTimeOffset]::Now.ToString('o');Organization='goodshepherdback';TestTokenPresent=$tokenPresent;TestUserPresent=$userPresent;EncryptionActive=[bool]$encryption.Results['IsActive'];Trace=$before;TestLineIdRows=$test;NonTestLineIdRows=$nonTest;ChannelRows=$channels.Count;LetterTokenRows=$letters.Count;P0TaskRows=$tasks.Count;AssemblyNames=$assemblyNames}
 }
 $json=$summary|ConvertTo-Json -Depth 8
 [IO.File]::WriteAllText($ResultFile,($json.Replace("`r`n","`n").Replace("`n","`r`n")+"`r`n"),[Text.UTF8Encoding]::new($false))
 $json
} catch {
 $e=$_.Exception;while($e.InnerException){$e=$e.InnerException}
 Write-Output ('AuditFailureLine='+$_.InvocationInfo.ScriptLineNumber)
 Write-Output ('AuditFailureType='+$e.GetType().FullName)
 if($e -is [System.ServiceModel.FaultException[Microsoft.Xrm.Sdk.OrganizationServiceFault]]){Write-Output ('AuditFaultCode=0x'+$e.Detail.ErrorCode.ToString('X8'))}
 exit 1
} finally {if($lease){$lease.Dispose()};if($config -is [IDisposable]){$config.Dispose()}}
