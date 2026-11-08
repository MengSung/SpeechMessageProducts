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
 $q=[Microsoft.Xrm.Sdk.Query.QueryExpression]::new('plugintracelog');$q.TopCount=20;$q.ColumnSet=[Microsoft.Xrm.Sdk.Query.ColumnSet]::new([string[]]@('plugintracelogid','correlationid','messageblock','exceptiondetails','createdon','typename'));$q.Criteria.AddCondition('typename',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal,'SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin');$q.Criteria.AddCondition('createdon',[Microsoft.Xrm.Sdk.Query.ConditionOperator]::GreaterEqual,$start)
 $traces=$service.RetrieveMultiple($q);$rows=@();$matches=0
 $secretValues=@($config[$prefix+'TestChannelToken'],$config[$prefix+'TestLineUserId'],$config[$prefix+'Password'])|Where-Object{-not [string]::IsNullOrEmpty($_)}
 foreach($trace in $traces.Entities){
  $message=$trace.GetAttributeValue[string]('messageblock');$details=$trace.GetAttributeValue[string]('exceptiondetails');$text=$message+"`n"+$details
  foreach($value in $secretValues){if($text.Contains($value,[StringComparison]::Ordinal)){$matches++}}
  $outcome=[regex]::Match($message,'Outcome=([A-Za-z]+)').Groups[1].Value;$status=[regex]::Match($message,'Status=([A-Za-z0-9-]+)').Groups[1].Value;$req=[regex]::Match($message,'RequestId=([A-Za-z0-9-]+)').Groups[1].Value;$parsed=[Guid]::Empty;$valid=[Guid]::TryParse($req,[ref]$parsed)
  $types=@([regex]::Matches($text,'(?:ExceptionType=|Exception: )(?:[A-Za-z0-9_.]+Exception)')|ForEach-Object{$_.Value.Replace('ExceptionType=','').Replace('Exception: ','')}|Select-Object -Unique)
  $known=@{};foreach($tag in @('TLS','SSL','certificate','timed out','NameResolutionFailure','Could not load file or assembly','System.Text.Json','Newtonsoft.Json','Forbidden','Unauthorized','401','400','503')){$known[$tag]=$text.Contains($tag,[StringComparison]::OrdinalIgnoreCase)}
  $rows+=@{TraceId=$trace.Id;CorrelationId=$trace.GetAttributeValue[Guid]('correlationid');CreatedOn=$trace.GetAttributeValue[DateTime]('createdon').ToString('o');Ping=$message.Contains('P0-PING OK');Outcome=$outcome;Status=$status;RequestId=if($valid){$parsed.ToString('D')}else{$null};RequestIdValid=$valid;ExceptionTypes=$types;DiagnosticFlags=$known}
 }
 $summary=@{At=[DateTimeOffset]::Now.ToString('o');TraceCount=$traces.Entities.Count;SecretValuesChecked=$secretValues.Count;SecretMatches=$matches;Rows=$rows}
 $json=$summary|ConvertTo-Json -Depth 8;[IO.File]::WriteAllText($ResultFile,$json.Replace("`r`n","`n").Replace("`n","`r`n")+"`r`n",[Text.UTF8Encoding]::new($false));$json
} catch {
 $e=$_.Exception;while($e.InnerException){$e=$e.InnerException}
 Write-Output ('AuditFailureLine='+$_.InvocationInfo.ScriptLineNumber)
 Write-Output ('AuditFailureType='+$e.GetType().FullName)
 if($e -is [System.ServiceModel.FaultException[Microsoft.Xrm.Sdk.OrganizationServiceFault]]){Write-Output ('AuditFaultCode=0x'+$e.Detail.ErrorCode.ToString('X8'))}
 exit 1
} finally {if($lease){$lease.Dispose()};if($config -is [IDisposable]){$config.Dispose()}}
