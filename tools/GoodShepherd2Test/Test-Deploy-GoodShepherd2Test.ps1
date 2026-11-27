<#
.SYNOPSIS
檔案整體說明：離線驗證好牧人二代測試部署的拒絕邊界。
.DESCRIPTION
目的與定位：本檔屬於線Ｂ的Ｆ２部署工具測試，只使用合成憑證與站台快照，保護乾跑不連線、
憑證精確選取、固定內容路徑、端口獨占、壓縮檔輸入，以及Development JSON與web.config
使用Encrypt=Strict和固定ServerCertificate公鑰釘選的契約，並不代表任何實機結果。
另以合成CRM帳密驗證只取用精確組織、不回退舊組織；集合替身驗證專用集區環境的兩鍵
更新與精準回復，其他鍵保持不變且不輸出帳密。替身不代表IIS實際保存或worker繼承成功。
提交生命週期案例直接執行部署切換與失敗回復片段，讓合成IIS物件在Commit後拒絕再次修改，
涵蓋既有站台回復、未提交首次安裝候選不得落盤、既有集區AutoStart與啟動故障排空；
保護切換及回復都先釋放舊快照、重新核驗目標再啟動，不冒稱設定或原狀已恢復。
ACL操作只用無磁碟副作用的替身，測試owner在finally釋放全部合成管理器。
主要功能與流程：先解析部署腳本，從語法樹載入純驗證函式，再注入同名不同身分、過期、
無私鑰、歧義、越界與端口衝突等案例；每例以接受或固定拒絕結果為決定性斷言。
使用邊界與維護：不執行套用、不啟動產品、不建立遠端連線或存取秘密。測試僅保留呼叫區域
的合成值，無檔案、背景工作或遠端資源需要清理；乾跑以會拋錯的遠端命令替身證明沒有連線。
腳本與測試必須同時維持可執行語彙為純英文、繁中僅在註解，以及無ＢＯＭ的統一換行。
PSCredential由當案Dispose其SecureString；失敗只輸出型別與位置，不輸出原例外訊息。
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
foreach ($name in @('Select-Gs2Certificate', 'Assert-Gs2Topology', 'Assert-Gs2ArchiveName', 'Assert-Gs2CrmUrl', 'Get-Gs2CrmEnvironment', 'Test-Gs2CrmEnvironmentSnapshot')) {
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

$expectedPlatformSql = 'Server=D365APP01;Database=GoodShepherd2Platform;Integrated Security=True;Encrypt=Strict;TrustServerCertificate=False;ServerCertificate=C:\ProgramData\SpeechMessage\certs\d365-9.1-sql.cer;'

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
    IisActivation_ReopensCommittedConfigurationBeforeChangingAutoStart = {
        $assignment = $ast.Find({
            param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left.Extent.Text -ceq '$remoteDeployment'
        }, $true)
        $source = $assignment.Right.Extent.Text
        $start = $source.IndexOf('$manager.CommitChanges()' + "`r`n" + '        $committed = $true',
            [StringComparison]::Ordinal)
        $end = $source.IndexOf('$remotePhase = ''Liveness''', $start, [StringComparison]::Ordinal)
        Assert-Condition ($start -ge 0 -and $end -gt $start)
        $release = 'C:\inetpub\GoodShepherd2Test\releases\20261126T000000Z-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        $owners = [Collections.Generic.List[object]]::new()
        $savedAuto = @{ Pool=$false; Site=$false }
        function New-ActivationManagerFixture {
            # Commit後的組態只能讀；替身重現實機第二輪的失效快照，不建立站台或檔案。
            $poolFixture = [pscustomobject]@{ Committed=$false; AutoValue=$savedAuto.Pool; State='Stopped'; WorkerProcesses=@() }
            $poolFixture | Add-Member ScriptProperty AutoStart { $this.AutoValue } {
                param($value)
                if ($this.Committed) { throw 'COMMITTED_CONFIGURATION_IS_READ_ONLY' }
                $this.AutoValue = $value
            }
            $poolFixture | Add-Member ScriptMethod Start { $this.State='Started' }
            $siteFixture = [pscustomobject]@{
                Committed=$false; AutoValue=$savedAuto.Site; State='Stopped'
                Applications=@{ '/'=@{ VirtualDirectories=@{ '/'=@{ PhysicalPath=$release } } } }
            }
            $siteFixture | Add-Member ScriptProperty ServerAutoStart { $this.AutoValue } {
                param($value)
                if ($this.Committed) { throw 'COMMITTED_CONFIGURATION_IS_READ_ONLY' }
                $this.AutoValue = $value
            }
            $siteFixture | Add-Member ScriptMethod Start { $this.State='Started' }
            $fixture = [pscustomobject]@{
                ApplicationPools=@{ GoodShepherd2Test=$poolFixture }
                Sites=@{ GoodShepherd2Test=$siteFixture }
                Disposed=$false
            }
            $fixture | Add-Member ScriptMethod CommitChanges {
                $savedAuto.Pool=$this.ApplicationPools.GoodShepherd2Test.AutoValue
                $savedAuto.Site=$this.Sites.GoodShepherd2Test.AutoValue
                $this.ApplicationPools.GoodShepherd2Test.Committed=$true
                $this.Sites.GoodShepherd2Test.Committed=$true
            }
            $fixture | Add-Member ScriptMethod Dispose { $this.Disposed=$true }
            $owners.Add($fixture)
            return $fixture
        }
        function New-Object {
            param([string]$TypeName)
            if ($TypeName -cne 'Microsoft.Web.Administration.ServerManager') { throw 'UNEXPECTED_FACTORY' }
            New-ActivationManagerFixture
        }
        function Get-Gs2Topology { param($Manager) Assert-Condition (-not $Manager.Disposed) }
        function Get-Acl {
            param($LiteralPath)
            $aclFixture = [pscustomobject]@{}
            $aclFixture | Add-Member ScriptMethod AddAccessRule { param($Rule) }
            return $aclFixture
        }
        function Set-Acl { param($LiteralPath, $AclObject) }
        function Test-Path { param($LiteralPath) return $false }
        function New-Gs2PrivateDirectory { param($Path) }
        $manager = New-ActivationManagerFixture
        $pool = $manager.ApplicationPools['GoodShepherd2Test']
        $site = $manager.Sites['GoodShepherd2Test']
        try {
            . ([scriptblock]::Create($source.Substring($start, $end - $start)))
            Assert-Condition ($pool.State -ceq 'Started' -and $site.State -ceq 'Started')
            Assert-Condition ($pool.AutoStart -and $site.ServerAutoStart)
            Assert-Condition ($owners.Count -gt 1 -and $owners[0].Disposed)
        }
        finally {
            foreach ($owner in $owners) { $owner.Dispose() }
        }
    }
    Rollback_RestoresPreviousStartedStateFromFreshManager = {
        # 直接執行部署腳本的真實失敗catch；只有IIS管理器邊界以合成owner替代。
        $remoteAssignment = $ast.Find({
            param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left.Extent.Text -ceq '$remoteDeployment'
        }, $true)
        Assert-Condition ($null -ne $remoteAssignment)
        $rollbackCatch = $remoteAssignment.Right.Find({
            param($node)
            $node -is [Management.Automation.Language.CatchClauseAst] -and
            $node.Body.Extent.Text.Contains('$failureType') -and
            $node.Body.Extent.Text.Contains('GS2_REMOTE_FAILED')
        }, $true)
        Assert-Condition ($null -ne $rollbackCatch)
        $rollbackScenario = [scriptblock]::Create(
            "try { throw [System.InvalidOperationException]::new('Synthetic liveness failure.') }`r`n" +
            $rollbackCatch.Extent.Text)
        foreach ($failPoolStart in @($false, $true)) {
        $script:rollbackFixtureState = @{
            SitePath='C:\inetpub\GoodShepherd2Test\releases\20261127T000000Z-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
            SiteState='Started'; PoolState='Started'; Managers=[Collections.Generic.List[object]]::new()
            Events=[Collections.Generic.List[string]]::new()
            CommittedManagers=[Collections.Generic.List[int]]::new()
            CertificateHash=[byte[]]@(1,2,3); CertificateStoreName='My'
            SiteAutoStart=$true; PoolAutoStart=$true
            FailPoolStart=$failPoolStart
        }
        $iis = $script:rollbackFixtureState
        $events = $iis.Events
        function New-RollbackManagerFixture {
            $managerNumber = $script:rollbackFixtureState.Managers.Count + 1
            $virtualDirectory = [pscustomobject]@{}
            $virtualDirectory | Add-Member ScriptProperty PhysicalPath { $script:rollbackFixtureState.SitePath } { param($value) $script:rollbackFixtureState.SitePath=$value }
            $binding = [pscustomobject]@{ ManagerNumber=$managerNumber }
            $binding | Add-Member ScriptProperty CertificateHash { [byte[]]$script:rollbackFixtureState.CertificateHash } { param($value) if ($script:rollbackFixtureState.CommittedManagers.Contains($this.ManagerNumber)) { throw 'COMMITTED_CONFIGURATION_IS_READ_ONLY' }; $script:rollbackFixtureState.CertificateHash=[byte[]]$value }
            $binding | Add-Member ScriptProperty CertificateStoreName { $script:rollbackFixtureState.CertificateStoreName } { param($value) if ($script:rollbackFixtureState.CommittedManagers.Contains($this.ManagerNumber)) { throw 'COMMITTED_CONFIGURATION_IS_READ_ONLY' }; $script:rollbackFixtureState.CertificateStoreName=$value }
            $site = [pscustomobject]@{
                Id=501; ManagerNumber=$managerNumber
                Applications=@{ '/'=@{ VirtualDirectories=@{ '/'=$virtualDirectory } } }
                Bindings=@($binding)
            }
            $site | Add-Member ScriptProperty ServerAutoStart { [bool]$script:rollbackFixtureState.SiteAutoStart } { param($value) if ($script:rollbackFixtureState.CommittedManagers.Contains($this.ManagerNumber)) { throw 'COMMITTED_CONFIGURATION_IS_READ_ONLY' }; $script:rollbackFixtureState.SiteAutoStart=[bool]$value }
            $site | Add-Member ScriptProperty State {
                if ($script:rollbackFixtureState.CommittedManagers.Contains($this.ManagerNumber)) { throw 'COMMITTED_CONFIGURATION_IS_READ_ONLY' }
                $script:rollbackFixtureState.SiteState
            } { param($value) $script:rollbackFixtureState.SiteState=$value }
            $site | Add-Member ScriptMethod Stop { $script:rollbackFixtureState.SiteState='Stopped'; $script:rollbackFixtureState.Events.Add('site.stop') }
            $site | Add-Member ScriptMethod Start {
                if ($script:rollbackFixtureState.CommittedManagers.Contains($this.ManagerNumber)) { throw 'COMMITTED_CONFIGURATION_IS_READ_ONLY' }
                $script:rollbackFixtureState.SiteState='Started'; $script:rollbackFixtureState.Events.Add('site.start')
            }
            $pool = [pscustomobject]@{ ManagerNumber=$managerNumber; WorkerProcesses=@() }
            $pool | Add-Member ScriptProperty AutoStart { [bool]$script:rollbackFixtureState.PoolAutoStart } { param($value) if ($script:rollbackFixtureState.CommittedManagers.Contains($this.ManagerNumber)) { throw 'COMMITTED_CONFIGURATION_IS_READ_ONLY' }; $script:rollbackFixtureState.PoolAutoStart=[bool]$value }
            $pool | Add-Member ScriptProperty State {
                if ($script:rollbackFixtureState.CommittedManagers.Contains($this.ManagerNumber)) { throw 'COMMITTED_CONFIGURATION_IS_READ_ONLY' }
                $script:rollbackFixtureState.PoolState
            } { param($value) $script:rollbackFixtureState.PoolState=$value }
            $pool | Add-Member ScriptMethod Stop { $script:rollbackFixtureState.PoolState='Stopped'; $script:rollbackFixtureState.Events.Add('pool.stop') }
            $pool | Add-Member ScriptMethod Start {
                if ($script:rollbackFixtureState.CommittedManagers.Contains($this.ManagerNumber)) { throw 'COMMITTED_CONFIGURATION_IS_READ_ONLY' }
                if ($script:rollbackFixtureState.FailPoolStart -and $this.ManagerNumber -eq 3) { throw 'SYNTHETIC_POOL_START_FAILURE' }
                $script:rollbackFixtureState.PoolState='Started'; $script:rollbackFixtureState.Events.Add(('pool.start.manager-' + $this.ManagerNumber))
            }
            $manager = [pscustomobject]@{
                Id=$managerNumber; Disposed=$false
                Sites=@{ GoodShepherd2Test=$site }; ApplicationPools=@{ GoodShepherd2Test=$pool }
            }
            $manager | Add-Member ScriptMethod CommitChanges {
                $script:rollbackFixtureState.CommittedManagers.Add($this.Id)
                $script:rollbackFixtureState.Events.Add(('commit.manager-' + $this.Id))
            }
            $manager | Add-Member ScriptMethod Dispose { $this.Disposed=$true; $script:rollbackFixtureState.Events.Add(('dispose.manager-' + $this.Id)) }
            $script:rollbackFixtureState.Managers.Add($manager)
            return $manager
        }
        function New-Object {
            param([string]$TypeName)
            if ($TypeName -cne 'Microsoft.Web.Administration.ServerManager') { throw 'UNEXPECTED_MANAGER_TYPE' }
            New-RollbackManagerFixture
        }
        function Get-Gs2Topology { param($Manager) if ($Manager.Disposed) { throw 'DISPOSED_MANAGER_USED' } }
        function Wait-Gs2PoolStopped { if ($script:rollbackFixtureState.PoolState -cne 'Stopped') { throw 'POOL_NOT_DRAINED' } }
        function Set-Gs2CrmEnvironment { param($Variables,$Values) }

        $manager = New-RollbackManagerFixture
        $site = $manager.Sites['GoodShepherd2Test']
        $pool = $manager.ApplicationPools['GoodShepherd2Test']
        $changed = $true
        $committed = $true
        $release = $iis.SitePath
        $previousCrmEnvironment = $null
        $previousPath = 'C:\inetpub\GoodShepherd2Test\releases\20261126T000000Z-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
        $previous = [ordered]@{
            SiteExists=$true; PoolExists=$true; SiteStarted=$true; PoolStarted=$true
            PhysicalPath=$previousPath; Certificate=[Convert]::ToBase64String([byte[]]@(4,5,6)); Store='My'
            SiteAutoStart=$true; PoolAutoStart=$true; SiteId=501
        }
        $hasRecord = $false
        $recordData = [ordered]@{ RunId='fixture'; Phase='Liveness'; Result='Pending'; Rollback='Failed' }
        $remotePhase = 'Liveness'
        $id = '20261127T000000Z-cccccccccccccccccccccccccccccccc'
        $remoteFailure = ''
        $expectedRollback = if ($failPoolStart) { 'PreviousConfigurationRestoredActivationIncomplete' } else { 'PreviousStateRestored' }
        try { & $rollbackScenario }
        catch { $remoteFailure = $_.Exception.Message }
        try {
            Assert-Condition ($remoteFailure -match ('rollback=' + $expectedRollback))
            Assert-Condition ($iis.SitePath -ceq $previousPath)
            if ($failPoolStart) {
                Assert-Condition ($iis.SiteState -ceq 'Stopped' -and $iis.PoolState -ceq 'Stopped')
            }
            else { Assert-Condition ($iis.SiteState -ceq 'Started' -and $iis.PoolState -ceq 'Started') }
            Assert-Condition ($iis.Managers.Count -eq 3)
            Assert-Condition ($iis.Managers[0].Disposed -and $iis.Managers[1].Disposed -and -not $iis.Managers[2].Disposed)
            Assert-Condition ([Convert]::ToBase64String([byte[]]$iis.CertificateHash) -ceq $previous.Certificate -and $iis.CertificateStoreName -ceq $previous.Store)
            Assert-Condition ($iis.SiteAutoStart -eq $previous.SiteAutoStart -and $iis.PoolAutoStart -eq $previous.PoolAutoStart)
            if (-not $failPoolStart) { Assert-Condition ($events.Contains('pool.start.manager-3') -and $events.Contains('site.start')) }
            Assert-Condition ($events.IndexOf('commit.manager-2') -lt $events.IndexOf('dispose.manager-2'))
        }
        finally {
            try { foreach ($owner in $iis.Managers) { $owner.Dispose() } }
            finally { $script:rollbackFixtureState=$null }
        }
        }
    }
    Rollback_UncommittedFirstInstallDoesNotCommitCandidate = {
        # 第一次安裝的站台／集區候選只存在失敗管理器；重開後應看不到它們且不得Commit。
        $remoteAssignment = $ast.Find({
            param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left.Extent.Text -ceq '$remoteDeployment'
        }, $true)
        Assert-Condition ($null -ne $remoteAssignment)
        $rollbackCatch = $remoteAssignment.Right.Find({
            param($node)
            $node -is [Management.Automation.Language.CatchClauseAst] -and
            $node.Body.Extent.Text.Contains('$failureType') -and
            $node.Body.Extent.Text.Contains('GS2_REMOTE_FAILED')
        }, $true)
        Assert-Condition ($null -ne $rollbackCatch)
        $rollbackScenario = [scriptblock]::Create(
            "try { throw [System.InvalidOperationException]::new('Synthetic pre-commit failure.') }`r`n" +
            $rollbackCatch.Extent.Text)
        $script:uncommittedRollbackState = @{
            Managers=[Collections.Generic.List[object]]::new()
            Events=[Collections.Generic.List[string]]::new()
        }
        function New-UncommittedRollbackManager {
            $number=$script:uncommittedRollbackState.Managers.Count+1
            $sites=@{}; $pools=@{}
            if($number -eq 1){
                $sites['GoodShepherd2Test']=[pscustomobject]@{ Candidate=$true }
                $pools['GoodShepherd2Test']=[pscustomobject]@{ Candidate=$true }
            }
            $manager=[pscustomobject]@{ Id=$number; Disposed=$false; Sites=$sites; ApplicationPools=$pools }
            $manager | Add-Member ScriptMethod CommitChanges { $script:uncommittedRollbackState.Events.Add(('commit.manager-'+$this.Id)) }
            $manager | Add-Member ScriptMethod Dispose { $this.Disposed=$true; $script:uncommittedRollbackState.Events.Add(('dispose.manager-'+$this.Id)) }
            $script:uncommittedRollbackState.Managers.Add($manager)
            return $manager
        }
        function New-Object { param([string]$TypeName) if($TypeName -cne 'Microsoft.Web.Administration.ServerManager'){throw 'UNEXPECTED_MANAGER_TYPE'}; New-UncommittedRollbackManager }
        function Get-Gs2Topology { param($Manager) if($Manager.Disposed){throw 'DISPOSED_MANAGER_USED'} }
        function Wait-Gs2PoolStopped { $script:uncommittedRollbackState.Events.Add('pool.drain.confirmed') }
        $manager=New-UncommittedRollbackManager
        $changed=$true; $committed=$false; $release='C:\inetpub\GoodShepherd2Test\releases\synthetic-release'
        $previous=[ordered]@{ SiteExists=$false; PoolExists=$false; SiteStarted=$false; PoolStarted=$false; PhysicalPath=''; Certificate=''; Store='My'; SiteAutoStart=$false; PoolAutoStart=$false; SiteId=0 }
        $previousCrmEnvironment=$null; $hasRecord=$false
        $recordData=[ordered]@{ RunId='fixture'; Phase='Switch'; Result='Pending'; Rollback='Failed' }
        $remotePhase='Switch'; $id='20261127T000000Z-dddddddddddddddddddddddddddddd'; $failureType=''; $failureLine=0
        $remoteFailure=''
        try { & $rollbackScenario } catch { $remoteFailure=$_.Exception.Message }
        try {
            Assert-Condition ($remoteFailure -match 'rollback=NoChangeCommitted')
            Assert-Condition ($recordData.Rollback -ceq 'NoChangeCommitted')
            Assert-Condition ($script:uncommittedRollbackState.Managers.Count -eq 2)
            Assert-Condition ($script:uncommittedRollbackState.Managers[0].Disposed -and -not $script:uncommittedRollbackState.Managers[1].Disposed)
            Assert-Condition ($script:uncommittedRollbackState.Events -notcontains 'commit.manager-2')
        }
        finally {
            try { foreach($owner in $script:uncommittedRollbackState.Managers){$owner.Dispose()} }
            finally { $script:uncommittedRollbackState=$null }
        }
    }
    Rollback_UncommittedFirstInstallRestoresExistingPoolWithoutCommit = {
        # 站台未存在但專用集區原已存在時，只還原被停止的集區，不提交新站台候選或改變AutoStart。
        $remoteAssignment = $ast.Find({
            param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left.Extent.Text -ceq '$remoteDeployment'
        }, $true)
        Assert-Condition ($null -ne $remoteAssignment)
        $rollbackCatch = $remoteAssignment.Right.Find({
            param($node)
            $node -is [Management.Automation.Language.CatchClauseAst] -and
            $node.Body.Extent.Text.Contains('$failureType') -and
            $node.Body.Extent.Text.Contains('GS2_REMOTE_FAILED')
        }, $true)
        Assert-Condition ($null -ne $rollbackCatch)
        $rollbackScenario = [scriptblock]::Create(
            "try { throw [System.InvalidOperationException]::new('Synthetic pre-commit pool failure.') }`r`n" +
            $rollbackCatch.Extent.Text)
        $script:uncommittedPoolRollbackState=@{
            PoolState='Stopped'; PoolAutoStart=$true; Managers=[Collections.Generic.List[object]]::new()
            Events=[Collections.Generic.List[string]]::new()
        }
        function New-UncommittedPoolRollbackManager {
            $number=$script:uncommittedPoolRollbackState.Managers.Count+1
            $pool=[pscustomobject]@{ ManagerNumber=$number; WorkerProcesses=@() }
            $pool | Add-Member ScriptProperty AutoStart { [bool]$script:uncommittedPoolRollbackState.PoolAutoStart } { param($value) $script:uncommittedPoolRollbackState.PoolAutoStart=[bool]$value }
            $pool | Add-Member ScriptProperty State { $script:uncommittedPoolRollbackState.PoolState } { param($value) $script:uncommittedPoolRollbackState.PoolState=$value }
            $pool | Add-Member ScriptMethod Stop { $script:uncommittedPoolRollbackState.PoolState='Stopped'; $script:uncommittedPoolRollbackState.Events.Add('pool.stop') }
            $pool | Add-Member ScriptMethod Start { $script:uncommittedPoolRollbackState.PoolState='Started'; $script:uncommittedPoolRollbackState.Events.Add(('pool.start.manager-'+$this.ManagerNumber)) }
            $pool | Add-Member ScriptMethod GetCollection { param($name) @() }
            $sites=@{}
            if($number -eq 1){$sites['GoodShepherd2Test']=[pscustomobject]@{Candidate=$true}}
            $manager=[pscustomobject]@{Id=$number;Disposed=$false;Sites=$sites;ApplicationPools=@{GoodShepherd2Test=$pool}}
            $manager | Add-Member ScriptMethod CommitChanges { $script:uncommittedPoolRollbackState.Events.Add(('commit.manager-'+$this.Id)) }
            $manager | Add-Member ScriptMethod Dispose { $this.Disposed=$true; $script:uncommittedPoolRollbackState.Events.Add(('dispose.manager-'+$this.Id)) }
            $script:uncommittedPoolRollbackState.Managers.Add($manager)
            return $manager
        }
        function New-Object { param([string]$TypeName) if($TypeName -cne 'Microsoft.Web.Administration.ServerManager'){throw 'UNEXPECTED_MANAGER_TYPE'}; New-UncommittedPoolRollbackManager }
        function Get-Gs2Topology { param($Manager) if($Manager.Disposed){throw 'DISPOSED_MANAGER_USED'} }
        function Wait-Gs2PoolStopped { if($script:uncommittedPoolRollbackState.PoolState -cne 'Stopped'){throw 'POOL_NOT_DRAINED'} }
        $manager=New-UncommittedPoolRollbackManager
        $pool=$manager.ApplicationPools['GoodShepherd2Test']
        $changed=$true; $committed=$false; $release='C:\inetpub\GoodShepherd2Test\releases\synthetic-release'
        $previous=[ordered]@{SiteExists=$false;PoolExists=$true;SiteStarted=$false;PoolStarted=$true;PhysicalPath='';Certificate='';Store='My';SiteAutoStart=$false;PoolAutoStart=$true;SiteId=0}
        $previousCrmEnvironment=@{}; $hasRecord=$false
        $recordData=[ordered]@{RunId='fixture';Phase='BeforeSwitch';Result='Pending';Rollback='Failed'}
        $remotePhase='BeforeSwitch'; $id='20261127T000000Z-ffffffffffffffffffffffffffffffff'; $failureType=''; $failureLine=0
        $remoteFailure=''
        try { & $rollbackScenario } catch { $remoteFailure=$_.Exception.Message }
        try {
            Assert-Condition ($remoteFailure -match 'rollback=PreviousStateRestored')
            Assert-Condition ($recordData.Rollback -ceq 'PreviousStateRestored')
            Assert-Condition ($script:uncommittedPoolRollbackState.PoolState -ceq 'Started' -and $script:uncommittedPoolRollbackState.PoolAutoStart)
            Assert-Condition ($script:uncommittedPoolRollbackState.Managers.Count -eq 2)
            Assert-Condition ($script:uncommittedPoolRollbackState.Managers[0].Disposed -and -not $script:uncommittedPoolRollbackState.Managers[1].Disposed)
            Assert-Condition ($script:uncommittedPoolRollbackState.Events -notcontains 'commit.manager-2')
        }
        finally {
            try { foreach($owner in $script:uncommittedPoolRollbackState.Managers){$owner.Dispose()} }
            finally { $script:uncommittedPoolRollbackState=$null }
        }
    }
    Rollback_NewSitePreservesExistingPoolAutoStart = {
        # 首次安裝已Commit但失敗時，保留停止的新站台並還原先存集區的AutoStart與執行狀態。
        $remoteAssignment = $ast.Find({
            param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left.Extent.Text -ceq '$remoteDeployment'
        }, $true)
        Assert-Condition ($null -ne $remoteAssignment)
        $rollbackCatch = $remoteAssignment.Right.Find({
            param($node)
            $node -is [Management.Automation.Language.CatchClauseAst] -and
            $node.Body.Extent.Text.Contains('$failureType') -and
            $node.Body.Extent.Text.Contains('GS2_REMOTE_FAILED')
        }, $true)
        Assert-Condition ($null -ne $rollbackCatch)
        $rollbackScenario = [scriptblock]::Create(
            "try { throw [System.InvalidOperationException]::new('Synthetic post-commit failure.') }`r`n" +
            $rollbackCatch.Extent.Text)
        $script:newSiteRollbackState=@{
            SitePath='C:\inetpub\GoodShepherd2Test\releases\synthetic-release'; SiteState='Started'; PoolState='Started'
            SiteAutoStart=$false; PoolAutoStart=$false; Managers=[Collections.Generic.List[object]]::new()
            Events=[Collections.Generic.List[string]]::new()
        }
        function New-NewSiteRollbackManager {
            $number=$script:newSiteRollbackState.Managers.Count+1
            $virtual=[pscustomobject]@{}
            $virtual | Add-Member ScriptProperty PhysicalPath { $script:newSiteRollbackState.SitePath } { param($value) $script:newSiteRollbackState.SitePath=$value }
            $site=[pscustomobject]@{ Id=501; ManagerNumber=$number; Applications=@{'/'=@{VirtualDirectories=@{'/'=$virtual}}}; Bindings=@([pscustomobject]@{CertificateHash=[byte[]]@(1,2,3);CertificateStoreName='My'}) }
            $site | Add-Member ScriptProperty ServerAutoStart { [bool]$script:newSiteRollbackState.SiteAutoStart } { param($value) $script:newSiteRollbackState.SiteAutoStart=[bool]$value }
            $site | Add-Member ScriptProperty State { $script:newSiteRollbackState.SiteState } { param($value) $script:newSiteRollbackState.SiteState=$value }
            $site | Add-Member ScriptMethod Stop { $script:newSiteRollbackState.SiteState='Stopped'; $script:newSiteRollbackState.Events.Add('site.stop') }
            $site | Add-Member ScriptMethod Start { $script:newSiteRollbackState.SiteState='Started'; $script:newSiteRollbackState.Events.Add('site.start') }
            $pool=[pscustomobject]@{ ManagerNumber=$number; WorkerProcesses=@() }
            $pool | Add-Member ScriptProperty AutoStart { [bool]$script:newSiteRollbackState.PoolAutoStart } { param($value) $script:newSiteRollbackState.PoolAutoStart=[bool]$value }
            $pool | Add-Member ScriptProperty State { $script:newSiteRollbackState.PoolState } { param($value) $script:newSiteRollbackState.PoolState=$value }
            $pool | Add-Member ScriptMethod Stop { $script:newSiteRollbackState.PoolState='Stopped'; $script:newSiteRollbackState.Events.Add('pool.stop') }
            $pool | Add-Member ScriptMethod Start { $script:newSiteRollbackState.PoolState='Started'; $script:newSiteRollbackState.Events.Add(('pool.start.manager-'+$this.ManagerNumber)) }
            $pool | Add-Member ScriptMethod GetCollection { param($name) @() }
            $manager=[pscustomobject]@{ Id=$number; Disposed=$false; Sites=@{GoodShepherd2Test=$site}; ApplicationPools=@{GoodShepherd2Test=$pool} }
            $manager | Add-Member ScriptMethod CommitChanges { $script:newSiteRollbackState.Events.Add(('commit.manager-'+$this.Id)) }
            $manager | Add-Member ScriptMethod Dispose { $this.Disposed=$true; $script:newSiteRollbackState.Events.Add(('dispose.manager-'+$this.Id)) }
            $script:newSiteRollbackState.Managers.Add($manager)
            return $manager
        }
        function New-Object { param([string]$TypeName) if($TypeName -cne 'Microsoft.Web.Administration.ServerManager'){throw 'UNEXPECTED_MANAGER_TYPE'}; New-NewSiteRollbackManager }
        function Get-Gs2Topology { param($Manager) if($Manager.Disposed){throw 'DISPOSED_MANAGER_USED'} }
        function Wait-Gs2PoolStopped { if($script:newSiteRollbackState.PoolState -cne 'Stopped'){throw 'POOL_NOT_DRAINED'} }
        function Set-Gs2CrmEnvironment { param($Variables,$Values) }
        function Get-Gs2CrmEnvironment { param($Variables) return @{} }
        $manager=New-NewSiteRollbackManager
        $site=$manager.Sites['GoodShepherd2Test']; $pool=$manager.ApplicationPools['GoodShepherd2Test']
        $changed=$true; $committed=$true; $release=$script:newSiteRollbackState.SitePath
        $previous=[ordered]@{ SiteExists=$false; PoolExists=$true; SiteStarted=$false; PoolStarted=$true; PhysicalPath=''; Certificate=''; Store='My'; SiteAutoStart=$false; PoolAutoStart=$true; SiteId=0 }
        $previousCrmEnvironment=@{}; $hasRecord=$false
        $recordData=[ordered]@{ RunId='fixture'; Phase='Liveness'; Result='Pending'; Rollback='Failed' }
        $remotePhase='Liveness'; $id='20261127T000000Z-eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee'; $failureType=''; $failureLine=0
        $remoteFailure=''
        try { & $rollbackScenario } catch { $remoteFailure=$_.Exception.Message }
        try {
            Assert-Condition ($remoteFailure -match 'rollback=NewSiteKeptStopped')
            Assert-Condition ($recordData.Rollback -ceq 'NewSiteKeptStopped')
            Assert-Condition ($script:newSiteRollbackState.SitePath -ceq $release -and $script:newSiteRollbackState.SiteState -ceq 'Stopped')
            Assert-Condition ($script:newSiteRollbackState.SiteAutoStart -eq $false -and $script:newSiteRollbackState.PoolAutoStart -eq $true)
            Assert-Condition ($script:newSiteRollbackState.PoolState -ceq 'Started')
            Assert-Condition ($script:newSiteRollbackState.Managers.Count -eq 3)
            Assert-Condition ($script:newSiteRollbackState.Managers[1].Disposed -and -not $script:newSiteRollbackState.Managers[2].Disposed)
        }
        finally {
            try { foreach($owner in $script:newSiteRollbackState.Managers){$owner.Dispose()} }
            finally { $script:newSiteRollbackState=$null }
        }
    }
    IisAnonymousSection_IsNotReadBeforeSiteCandidateExists = {
        $definition = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq 'Get-Gs2AnonymousSection'
        }, $true)
        Assert-Condition ($null -ne $definition)
        . ([scriptblock]::Create($definition.Extent.Text))
        $manager = [pscustomobject]@{}
        $manager | Add-Member ScriptMethod GetApplicationHostConfiguration { throw 'No site context exists.' }
        Assert-Condition ($null -eq (Get-Gs2AnonymousSection $manager $null))
        # 真正IIS已另以未Commit候選驗證可讀；此替身只保護首次安裝不可先存取不存在路徑。
    }
    CrmCredential_ExactOrganizationOnlyAndNoFallback = {
        $definition = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq 'Get-Gs2CrmCredential'
        }, $true)
        Assert-Condition ($null -ne $definition)
        . ([scriptblock]::Create($definition.Extent.Text))
        $settings = [pscustomobject]@{
            'Deployment:Organizations:goodshepherd20:OrganizationUrl' = 'https://goodshepherd20.speechmessage.com.tw/XRMServices/2011/Organization.svc'
            'Deployment:Organizations:goodshepherd20:Username' = 'fixture-service'
            'Deployment:Organizations:goodshepherd20:Password' = 'fixture-password'
            'Deployment:Organizations:goodshepherdback:Username' = 'foreign-service'
            'Deployment:Organizations:goodshepherdback:Password' = 'foreign-password'
        }
        $url = $settings.'Deployment:Organizations:goodshepherd20:OrganizationUrl'
        $credential = Get-Gs2CrmCredential $settings $url
        try {
            Assert-Condition ($credential -is [Management.Automation.PSCredential])
            Assert-Condition ($credential.UserName -ceq 'fixture-service')
            Assert-Condition ($credential.GetNetworkCredential().Password -ceq 'fixture-password')
        }
        finally { $credential.Password.Dispose() }
        foreach ($field in @('OrganizationUrl', 'Username', 'Password')) {
            $copy = $settings | Select-Object *
            $copy.PSObject.Properties.Remove('Deployment:Organizations:goodshepherd20:' + $field)
            Assert-Rejected { Get-Gs2CrmCredential $copy $url }
        }
        $settings.'Deployment:Organizations:goodshepherd20:OrganizationUrl' = 'https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc'
        Assert-Rejected { Get-Gs2CrmCredential $settings $url }
    }
    CrmEnvironment_RoundTripRestoresOnlyOwnedKeysWithoutOutput = {
        foreach ($name in @('Get-Gs2CrmEnvironment', 'Set-Gs2CrmEnvironment')) {
            $definition = $ast.Find({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name
            }, $true)
            Assert-Condition ($null -ne $definition)
            . ([scriptblock]::Create($definition.Extent.Text))
        }
        # 替身只模擬IIS集合API；保護產品可觀察的異動／回復邊界，不連線或保存真實秘密。
        $variables = [Collections.ArrayList]::new()
        Add-Member -InputObject $variables -MemberType ScriptMethod -Name CreateElement -Value { param($kind) return @{} }
        $variables.Add(@{ name='Unrelated'; value='unchanged' }) | Out-Null
        $variables.Add(@{ name='CrmConnection__Username'; value='old-service' }) | Out-Null
        $before = Get-Gs2CrmEnvironment $variables
        $candidate = @{ CrmConnection__Username='new-service'; CrmConnection__Password='fixture-password' }
        $output = @(Set-Gs2CrmEnvironment $variables $candidate)
        Assert-Condition ($output.Count -eq 0 -and $variables.Count -eq 3)
        Assert-Condition (($variables | Where-Object { $_.name -ceq 'CrmConnection__Username' }).value -ceq 'new-service')
        Assert-Condition (($variables | Where-Object { $_.name -ceq 'CrmConnection__Password' }).value -ceq 'fixture-password')
        Set-Gs2CrmEnvironment $variables $before
        Assert-Condition ($variables.Count -eq 2)
        Assert-Condition (($variables | Where-Object { $_.name -ceq 'CrmConnection__Username' }).value -ceq 'old-service')
        Assert-Condition (($variables | Where-Object { $_.name -ceq 'Unrelated' }).value -ceq 'unchanged')
    }
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
        Assert-Condition $template.Value.Contains('__GS2_PLATFORM_CONNECTION_STRING__')
        $connectionDefinition = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq 'Get-Gs2PlatformConnectionString'
        }, $true)
        Assert-Condition ($null -ne $connectionDefinition)
        . ([scriptblock]::Create($connectionDefinition.Extent.Text))
        $actualSql = Get-Gs2PlatformConnectionString
        $rendered = $template.Value.Replace('__GS2_CRM_URL__', 'https://goodshepherd20.speechmessage.com.tw/XRMServices/2011/Organization.svc').Replace(
            '__GS2_PLATFORM_CONNECTION_STRING__', [Security.SecurityElement]::Escape($actualSql))
        $document = [xml]$rendered
        $hosting = $document.configuration.location.'system.webServer'.aspNetCore
        Assert-Condition ($hosting.processPath -ceq 'dotnet' -and $hosting.hostingModel -ceq 'inprocess')
        Assert-Condition ($hosting.stdoutLogEnabled -ceq 'false')
        $variables = @{}
        foreach ($variable in $hosting.environmentVariables.environmentVariable) { $variables.Add($variable.name, $variable.value) }
        Assert-Condition ($variables['DOTNET_ENVIRONMENT'] -ceq 'Development' -and $variables['ASPNETCORE_ENVIRONMENT'] -ceq 'Development')
        Assert-Condition ($variables['CrmConnection__ServerUrl'] -ceq 'https://goodshepherd20.speechmessage.com.tw/XRMServices/2011/Organization.svc')
        Assert-Condition ($variables['GoodShepherd2__AllowedDevelopmentOrganizations__0'] -ceq 'goodshepherd20.speechmessage.com.tw')
        $sql = $variables['ConnectionStrings__Platform']
        Assert-Condition ($sql -ceq $expectedPlatformSql -and $sql -ceq $actualSql)
        Assert-Condition ($sql -match 'Encrypt=Strict')
        Assert-Condition ($sql -match 'ServerCertificate=C:\\ProgramData\\SpeechMessage\\certs\\d365-9\.1-sql\.cer')
        Assert-Condition ($sql -notmatch '(?i)TrustServerCertificate\s*=\s*true')
    }
    PlatformSql_DevelopmentJsonAndWebConfigUseStrictPinnedCertificate = {
        $source = [IO.File]::ReadAllText($runnerPath)
        Assert-Condition $source.Contains($expectedPlatformSql)
        Assert-Condition (-not $source.Contains('Server=D365APP01;Database=GoodShepherd2Platform;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;'))
        $connectionDefinition = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq 'Get-Gs2PlatformConnectionString'
        }, $true)
        $settingsDefinition = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq 'New-Gs2DevelopmentSettings'
        }, $true)
        Assert-Condition ($null -ne $connectionDefinition -and $null -ne $settingsDefinition)
        . ([scriptblock]::Create($connectionDefinition.Extent.Text))
        . ([scriptblock]::Create($settingsDefinition.Extent.Text))
        $actualSql = Get-Gs2PlatformConnectionString
        $settings = New-Gs2DevelopmentSettings 'https://goodshepherd20.speechmessage.com.tw/XRMServices/2011/Organization.svc'
        $json = $settings | ConvertTo-Json -Depth 4
        $parsed = $json | ConvertFrom-Json
        Assert-Condition ($actualSql -ceq $expectedPlatformSql)
        Assert-Condition ($parsed.ConnectionStrings.Platform -ceq $expectedPlatformSql -and $parsed.ConnectionStrings.Platform -ceq $actualSql)
        Assert-Condition ($parsed.ConnectionStrings.Platform -match 'Encrypt=Strict')
        Assert-Condition ($parsed.ConnectionStrings.Platform -match 'ServerCertificate=C:\\ProgramData\\SpeechMessage\\certs\\d365-9\.1-sql\.cer')
        Assert-Condition ($parsed.ConnectionStrings.Platform -notmatch '(?i)TrustServerCertificate\s*=\s*true')
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
    catch {
        $failed++
        Write-Output ('FAIL ' + $entry.Key + ' line=' + $_.InvocationInfo.ScriptLineNumber +
            ' type=' + $_.Exception.GetType().FullName)
        Write-Output $_.ScriptStackTrace
    }
}
Write-Output ('Deployment offline tests: ' + ($cases.Count - $failed) + '/' + $cases.Count)
if ($failed -ne 0) { exit 1 }
