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
 $start=[DateTimeOffset]::Parse([IO.File]::ReadAllText((Join-Path (Split-Path $ResultFile) 'smoke-start-utc.txt'))).UtcDateTime
 $query=[Microsoft.Xrm.Sdk.Query.QueryExpression]::new('task');$query.TopCount=2;$query.ColumnSet=[Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('activityid'));$query.Criteria.AddCondition('subject',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::BeginsWith,'[smoke] P0-');$query.Criteria.AddCondition('createdon',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::GreaterEqual,$start)
 $tasks=$service.RetrieveMultiple($query);$rows=@();foreach($task in $tasks.Entities){$q=[Microsoft.Xrm.Sdk.Query.QueryExpression]::new('asyncoperation');$q.ColumnSet=[Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('asyncoperationid','correlationid'));$q.Criteria.AddCondition('regardingobjectid',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal,$task.Id);$jobs=$service.RetrieveMultiple($q);$rows+=@{TaskId=$task.Id;JobIds=@($jobs.Entities|ForEach-Object{$_.Id});CorrelationIds=@($jobs.Entities|ForEach-Object{$_.GetAttributeValue[Guid]('correlationid')})}}
 $json=@{At=[DateTimeOffset]::Now.ToString('o');SmokeTaskCount=$tasks.Entities.Count;Rows=$rows}|ConvertTo-Json -Depth 6;[IO.File]::WriteAllText($ResultFile,$json.Replace("`r`n","`n").Replace("`n","`r`n")+"`r`n",[Text.UTF8Encoding]::new($false));$json
} catch {
 $e=$_.Exception;while($e.InnerException){$e=$e.InnerException}
 Write-Output ('AuditFailureLine='+$_.InvocationInfo.ScriptLineNumber)
 Write-Output ('AuditFailureType='+$e.GetType().FullName)
 if($e -is [System.ServiceModel.FaultException[Microsoft.Xrm.Sdk.OrganizationServiceFault]]){Write-Output ('AuditFaultCode=0x'+$e.Detail.ErrorCode.ToString('X8'))}
 exit 1
} finally {if($lease){$lease.Dispose()};if($config -is [IDisposable]){$config.Dispose()}}
