<#
.SYNOPSIS
檔案整體說明：離線保護F7公開圖片部署設定及專用根的存取邊界。
.DESCRIPTION
目的與定位：本檔從既有F2部署入口載入純函式，以合成SID及ACL驗證固定公開網址、
跨版本保留的圖片根、精確三個主體與最小修改權限；不呼叫遠端、不讀秘密、不連D365或LINE。
主要功能與流程：先以PowerShell語法樹載入真實設定／ACL函式，再驗證正常候選與故障注入；
額外主體、缺主體、繼承開啟及過大權限必須拒絕，輸入ACL不被驗證器更改。
使用邊界與維護：每個案例只持有區域DirectorySecurity及SID，不改磁碟或使用者權限。
失敗只輸出固定測試名及例外型別，不輸出原始設定。這些離線結果不代表VM ACL或公開HTTPS通過。
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Deploy-GoodShepherd2Test.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'F7_DEPLOY_PARSE_FAILED' }
foreach ($name in @('Assert-Gs2CrmUrl', 'Get-Gs2PlatformConnectionString', 'New-Gs2DevelopmentSettings')) {
    $node = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name }, $true)
    if ($null -eq $node) { throw 'F7_DEPLOY_FUNCTION_MISSING' }
    . ([scriptblock]::Create($node.Extent.Text))
}
function Assert-Condition {
    # 固定結果拒絕，避免測試失敗時發布來源設定或識別資料。
    param([bool]$Condition)
    if (-not $Condition) { throw 'F7_ASSERTION_FAILED' }
}
$failures = 0
$cases = [ordered]@{
    PublicAssets_FixedSettingsAndIndependentCandidates = {
        $first = New-Gs2DevelopmentSettings 'https://goodshepherd20.speechmessage.com.tw/XRMServices/2011/Organization.svc'
        Assert-Condition ($first.Contains('PublicAssets'))
        Assert-Condition ($first.PublicAssets.RootPath -ceq 'C:\inetpub\GoodShepherd2Test\public-assets')
        Assert-Condition ($first.PublicAssets.PublicBaseUrl -ceq 'https://gs2-test.speechmessage.com.tw/assets/')
        $first.PublicAssets.RootPath = 'changed'
        $second = New-Gs2DevelopmentSettings 'https://goodshepherd20.speechmessage.com.tw/XRMServices/2011/Organization.svc'
        Assert-Condition ($second.PublicAssets.RootPath -ceq 'C:\inetpub\GoodShepherd2Test\public-assets')
    }
    PublicAssets_AclAcceptsOnlyExactOwnerAndRights = {
        foreach ($name in @('New-Gs2PublicAssetsAcl', 'Assert-Gs2PublicAssetsAcl')) {
            $node = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name }, $true)
            Assert-Condition ($null -ne $node)
            . ([scriptblock]::Create($node.Extent.Text))
        }
        $sid = [Security.Principal.SecurityIdentifier]::new('S-1-5-82-123-456-789-123-456')
        $acl = New-Gs2PublicAssetsAcl $sid
        Assert-Gs2PublicAssetsAcl $acl $sid
        $before = $acl.GetSecurityDescriptorSddlForm('All')
        $extra = [Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new('S-1-1-0'), 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($extra)
        $rejected = $false
        try { Assert-Gs2PublicAssetsAcl $acl $sid } catch { $rejected = $true }
        Assert-Condition $rejected
        $acl = New-Gs2PublicAssetsAcl $sid
        $acl.SetAccessRuleProtection($false, $true)
        $rejected = $false
        try { Assert-Gs2PublicAssetsAcl $acl $sid } catch { $rejected = $true }
        Assert-Condition $rejected
        $acl = New-Gs2PublicAssetsAcl $sid
        $acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
        $rejected = $false
        try { Assert-Gs2PublicAssetsAcl $acl $sid } catch { $rejected = $true }
        Assert-Condition $rejected
        $acl = New-Gs2PublicAssetsAcl $sid
        $acl.PurgeAccessRules($sid)
        $rejected = $false
        try { Assert-Gs2PublicAssetsAcl $acl $sid } catch { $rejected = $true }
        Assert-Condition $rejected
        Assert-Condition ($before -ceq (New-Gs2PublicAssetsAcl $sid).GetSecurityDescriptorSddlForm('All'))
    }
}
foreach ($entry in $cases.GetEnumerator()) {
    try { & $entry.Value; Write-Output ('PASS ' + $entry.Key) }
    catch { $failures++; Write-Output ('FAIL ' + $entry.Key + ' type=' + $_.Exception.GetType().Name) }
}
Write-Output ('F7 deployment offline: ' + ($cases.Count - $failures) + '/' + $cases.Count)
if ($failures -ne 0) { exit 1 }
