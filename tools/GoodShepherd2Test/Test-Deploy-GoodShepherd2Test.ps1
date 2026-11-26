<#
.SYNOPSIS
檔案整體說明：離線驗證好牧人二代測試部署的拒絕邊界。
.DESCRIPTION
目的與定位：本檔屬於線Ｂ的Ｆ２部署工具測試，只使用合成憑證與站台快照，保護乾跑不連線、
憑證精確選取、固定內容路徑、端口獨占與壓縮檔輸入驗證契約，並不代表任何實機結果。
主要功能與流程：先解析部署腳本，從語法樹載入純驗證函式，再注入同名不同身分、過期、
無私鑰、歧義、越界與端口衝突等案例；每例以接受或固定拒絕結果為決定性斷言。
使用邊界與維護：不執行套用、不啟動產品、不建立遠端連線或存取秘密。測試僅保留呼叫區域
的合成值，無檔案、背景工作或遠端資源需要清理；乾跑以會拋錯的遠端命令替身證明沒有連線。
腳本與測試必須同時維持可執行語彙為純英文、繁中僅在註解，以及無ＢＯＭ的統一換行。
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$runnerPath = Join-Path $PSScriptRoot 'Deploy-GoodShepherd2Test.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($runnerPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'Deployment parser failed.' }
foreach ($name in @('Select-Gs2Certificate', 'Assert-Gs2Topology', 'Assert-Gs2ArchiveName', 'Assert-Gs2CrmUrl')) {
    $definition = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $definition) { throw "Missing validator: $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}

function Assert-Condition {
    # 合成斷言只輸出固定測試名稱，不能將輸入憑證或快照全文帶進失敗訊息。
    param([bool]$Condition)
    if (-not $Condition) { throw 'Assertion failed.' }
}

function Assert-Rejected {
    # 拒絕必須來自被測函式；函式意外接受時，在 catch 區塊以外拋錯避免假陽性。
    param([scriptblock]$Action)
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Assert-Condition $rejected
}

function New-CertificateFixture {
    # 每次建立獨立快照，故障案例不共用可變陣列，也不存取本機憑證存放區。
    param([string]$Id = ('A' * 40))
    [pscustomobject]@{
        Thumbprint = $Id
        DnsNames = @('*.speechmessage.com.tw')
        HasSan = $true
        HasPrivateKey = $true
        ServerAuthentication = $true
        NotBeforeUtc = [datetime]::UtcNow.AddDays(-1)
        NotAfterUtc = [datetime]::UtcNow.AddDays(30)
        Hash = [byte[]]@(1, 2, 3)
    }
}

function New-SiteFixture {
    # 測試資料明列正式身分與顯示名稱，確保同名不能取代固定路徑與集區的所有權檢查。
    param()
    [pscustomobject]@{
        Name = 'GoodShepherd2Test'
        ApplicationCount = 1
        VirtualDirectories = @([pscustomobject]@{
            ApplicationPath = '/'
            Path = '/'
            Pool = 'GoodShepherd2Test'
            PhysicalPath = 'C:\inetpub\GoodShepherd2Test\releases\20261126T000000Z-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        })
        Bindings = @([pscustomobject]@{
            Protocol = 'https'
            Port = 9443
            Information = '*:9443:gs2-test.speechmessage.com.tw'
            SslFlags = 1
        })
    }
}

$cases = [ordered]@{
    DryRun_NoRemoteOrPublish = {
        function New-PSSession { throw 'Dry-run attempted remoting.' }
        function Invoke-Command { throw 'Dry-run attempted remote execution.' }
        function Copy-Item { throw 'Dry-run attempted transfer.' }
        $result = & $runnerPath
        Assert-Condition ($result.Mode -eq 'DryRun' -and $result.Port -eq 9443)
        Assert-Condition ($result.SiteName -eq 'GoodShepherd2Test' -and $result.RemoteValidation -eq 'Pending')
        Assert-Condition ($result.Environment -eq 'Development' -and $result.BackupRequired)
    }
    WhatIf_NoRemoteOrPublish = {
        function New-PSSession { throw 'WhatIf attempted remoting.' }
        function Invoke-Command { throw 'WhatIf attempted remote execution.' }
        function Copy-Item { throw 'WhatIf attempted transfer.' }
        $result = & $runnerPath -WhatIf
        Assert-Condition ($result.Mode -eq 'DryRun' -and $result.RemoteValidation -eq 'Pending')
    }
    CrmUrl_ExactTestHost = {
        Assert-Gs2CrmUrl 'https://goodshepherd20.speechmessage.com.tw/XRMServices/2011/Organization.svc'
        foreach ($url in @(
            'https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc',
            'https://goodshepherd20.speechmessage.com.tw.evil.example/XRMServices/2011/Organization.svc',
            'http://goodshepherd20.speechmessage.com.tw/XRMServices/2011/Organization.svc',
            'https://goodshepherd20.speechmessage.com.tw:9443/XRMServices/2011/Organization.svc',
            'https://user@goodshepherd20.speechmessage.com.tw/XRMServices/2011/Organization.svc',
            'https://goodshepherd20.speechmessage.com.tw/XRMServices/2011/Organization.svc?x=1',
            'https://goodshepherd20.speechmessage.com.tw/'
        )) { Assert-Rejected { Assert-Gs2CrmUrl $url } }
    }
    Certificate_ExactWildcard = {
        $cert = New-CertificateFixture
        Assert-Condition ((Select-Gs2Certificate @($cert) '').Thumbprint -eq ('A' * 40))
        $cert.DnsNames = @('gs2-test.speechmessage.com.tw')
        Assert-Rejected { Select-Gs2Certificate @($cert) '' }
        $cert.DnsNames = @('*.speechmessage.com.tw.evil.example')
        Assert-Rejected { Select-Gs2Certificate @($cert) '' }
    }
    Certificate_AmbiguityRejected_ExplicitSelectionAllowed = {
        $a = New-CertificateFixture
        $b = New-CertificateFixture ('B' * 40)
        Assert-Rejected { Select-Gs2Certificate @($a, $b) '' }
        Assert-Condition ((Select-Gs2Certificate @($a, $b) ('b' * 40)).Thumbprint -eq ('B' * 40))
        Assert-Rejected { Select-Gs2Certificate @($a, $b) ('C' * 40) }
        Assert-Rejected { Select-Gs2Certificate @() '' }
    }
    Certificate_ExplicitThumbprintCannotBypassValidation = {
        foreach ($field in @('HasSan', 'HasPrivateKey', 'ServerAuthentication')) {
            $cert = New-CertificateFixture
            $cert.$field = $false
            Assert-Rejected { Select-Gs2Certificate @($cert) ('A' * 40) }
        }
        $cert = New-CertificateFixture
        $cert.NotAfterUtc = [datetime]::UtcNow.AddSeconds(-1)
        Assert-Rejected { Select-Gs2Certificate @($cert) ('A' * 40) }
        $cert = New-CertificateFixture
        $cert.NotBeforeUtc = [datetime]::UtcNow.AddDays(1)
        Assert-Rejected { Select-Gs2Certificate @($cert) ('A' * 40) }
    }
    Topology_NewOrOwnedSiteAllowed = {
        Assert-Gs2Topology @() $null @()
        $pool = [pscustomobject]@{ IdentityType = 4; Runtime = ''; Pipeline = 0; Enable32Bit = $false }
        Assert-Gs2Topology @((New-SiteFixture)) $pool @(9443)
    }
    Topology_ConflictingPortOrListenerRejected = {
        Assert-Rejected { Assert-Gs2Topology @() $null @(9443) }
        $site = New-SiteFixture
        $site.Name = 'CRM'
        Assert-Rejected { Assert-Gs2Topology @($site) $null @() }
        $site.Bindings[0].Port = 443
        $site.Bindings[0].Information = '*:443:crm.example'
        $site.VirtualDirectories[0].PhysicalPath = 'C:\CRM'
        $site.VirtualDirectories[0].Pool = 'CRMAppPool'
        Assert-Gs2Topology @($site) $null @(443)
    }
    Topology_SameNameDoesNotAuthorizeForeignContent = {
        $site = New-SiteFixture
        $site.VirtualDirectories[0].PhysicalPath = 'C:\CRM'
        Assert-Rejected { Assert-Gs2Topology @($site) $null @() }
        $site = New-SiteFixture
        $site.VirtualDirectories[0].PhysicalPath = 'C:\inetpub\GoodShepherd2Test\releases-evil\anything'
        Assert-Rejected { Assert-Gs2Topology @($site) $null @() }
    }
    Topology_SharedPoolAndOverlappingContentRejected = {
        $site = New-SiteFixture
        $site.Name = 'OtherSite'
        $site.Bindings = @()
        $site.VirtualDirectories[0].PhysicalPath = 'C:\OtherSite'
        Assert-Rejected { Assert-Gs2Topology @($site) $null @() }
        $site.VirtualDirectories[0].Pool = 'OtherPool'
        $site.VirtualDirectories[0].PhysicalPath = 'C:\inetpub'
        Assert-Rejected { Assert-Gs2Topology @($site) $null @() }
    }
    Topology_ExtraApplicationsOrBindingsRejected = {
        $site = New-SiteFixture
        $site.ApplicationCount = 2
        Assert-Rejected { Assert-Gs2Topology @($site) $null @() }
        $site = New-SiteFixture
        $site.Bindings += [pscustomobject]@{ Protocol='https'; Port=443; Information='*:443:example'; SslFlags=1 }
        Assert-Rejected { Assert-Gs2Topology @($site) $null @() }
        $pool = [pscustomobject]@{ IdentityType = 3; Runtime = ''; Pipeline = 0; Enable32Bit = $false }
        Assert-Rejected { Assert-Gs2Topology @((New-SiteFixture)) $pool @() }
    }
    Archive_TraversalAndSecretFilesRejected = {
        foreach ($name in @('../escape', '/absolute', 'C:/file', 'folder\file', 'folder/../escape', 'folder/./file', 'secrets.json', 'key.pfx', 'folder/key.pem', 'x:y', 'a//b')) {
            Assert-Rejected { Assert-Gs2ArchiveName $name }
        }
        Assert-Gs2ArchiveName 'SpeechMessageProducts.GoodShepherd2.Web.dll'
        Assert-Gs2ArchiveName 'runtimes/win-x64/native/library.dll'
        Assert-Gs2ArchiveName 'wwwroot/'
    }
    WebConfig_DevelopmentAndIntegratedSqlAreExplicit = {
        $template = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
            $node.Value.StartsWith('<?xml version=')
        }, $true)
        Assert-Condition ($null -ne $template)
        $document = [xml]$template.Value
        $hosting = $document.configuration.location.'system.webServer'.aspNetCore
        Assert-Condition ($hosting.processPath -ceq 'dotnet' -and $hosting.hostingModel -ceq 'inprocess')
        Assert-Condition ($hosting.stdoutLogEnabled -ceq 'false')
        $variables = @{}
        foreach ($variable in $hosting.environmentVariables.environmentVariable) { $variables.Add($variable.name, $variable.value) }
        Assert-Condition ($variables['DOTNET_ENVIRONMENT'] -ceq 'Development' -and $variables['ASPNETCORE_ENVIRONMENT'] -ceq 'Development')
        Assert-Condition ($variables['CrmConnection__ServerUrl'] -ceq '__GS2_CRM_URL__')
        Assert-Condition ($variables['GoodShepherd2__AllowedDevelopmentOrganizations__0'] -ceq 'goodshepherd20.speechmessage.com.tw')
        $sql = $variables['ConnectionStrings__Platform']
        Assert-Condition ($sql -ceq 'Server=D365APP01;Database=GoodShepherd2Platform;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;')
    }
    SessionRemoval_IsInsideFinally = {
        $tryStatement = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.TryStatementAst] -and
            $null -ne $node.Finally -and $node.Finally.Extent.Text -match 'Remove-PSSession'
        }, $true)
        Assert-Condition ($null -ne $tryStatement)
        $opening = $tryStatement.Body.Find({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'New-PSSession'
        }, $true)
        Assert-Condition ($null -ne $opening)
    }
    Source_NoUnsafeCommandsOrTlsBypass = {
        $commands = @($ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst]
        }, $true) | ForEach-Object { $_.GetCommandName() })
        foreach ($forbidden in @('Remove-Item', 'Remove-Website', 'Remove-WebAppPool', 'Set-ExecutionPolicy', 'Set-ItemProperty', 'Start-Job', 'Start-ThreadJob', 'Invoke-Sqlcmd', 'Set-PSSessionConfiguration')) {
            Assert-Condition ($commands -notcontains $forbidden)
        }
        $code = ($tokens | Where-Object Kind -ne Comment | ForEach-Object Text) -join ' '
        Assert-Condition ($code -notmatch 'SkipCertificateCheck|ServerCertificateValidationCallback|TrustServerCertificate=True|CredSSP|TrustedHosts')
        Assert-Condition ($code -match 'Remove-PSSession')
        foreach ($token in $tokens) {
            if ($token.Kind -ne 'Comment') { Assert-Condition ($token.Text -notmatch '[^\x00-\x7F]') }
        }
    }
}

$failed = 0
foreach ($entry in $cases.GetEnumerator()) {
    try { & $entry.Value; Write-Output ('PASS ' + $entry.Key) }
    catch { $failed++; Write-Output ('FAIL ' + $entry.Key) }
}
Write-Output ('Deployment offline tests: ' + ($cases.Count - $failed) + '/' + $cases.Count)
if ($failed -ne 0) { exit 1 }
