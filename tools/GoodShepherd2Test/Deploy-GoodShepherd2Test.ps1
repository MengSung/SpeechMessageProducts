<#
.SYNOPSIS
檔案整體說明：以預設乾跑方式規劃好牧人二代測試站的可重跑部署。
.DESCRIPTION
目的與定位：本檔是共通層計畫Ｆ２的主機部署入口。只部署專用的測試網站與應用程式集區，
不負責組織資料、平台資料庫建置、網域轉接、防火牆或手機驗收，也不修改本機電腦設定。
主要功能與流程：乾跑只回傳固定部署計畫，不發佈、不建立檔案或遠端連線。明確套用時，
先從部署工具既有User Secrets精確取用goodshepherd20帳密，再以有界子程序發佈專案，
產生無秘密的開發設定與網站設定；只從已發佈的開發JSON白名單複製LIFF三項公開識別，
缺項、未知鍵、錯型別或app與Login不符時在封裝前拒絕，其他區段不複製；平台SQL固定使用Encrypt=Strict、TrustServerCertificate=False
與ProgramData中的公鑰憑證釘選路徑，憑證本身須由P0-2④在測試主機預先匯出，且不打包或複製。
打包並計算雜湊；遠端每次命令
先核對主機身分及系統管理員權限，再驗證固定內容根、獨占端口與精確萬用憑證。建立封閉的
證據目錄並取得獨占檔案鎖後，先備份完整IIS設定，才上傳到不公開的套件目錄；提交前再次核對與備份，完整解壓後停止專用集區並排空，
首次安裝先建立未Commit的站台候選，再解析本站匿名設定；切換到新的不可變發佈目錄，
每次Commit後釋放已封存的組態快照，重新取得管理器並核對本站路徑及停止狀態；
失敗回復也在設定Commit後重新取得管理器、核對舊路徑與憑證，再恢復原啟動狀態。
若舊設定已復原但啟動失敗，先安全停止並排空，紀錄「設定已復原、啟動未完成」，
不把它冒稱為完整復原。正常切換以虛擬帳號啟動，最後嚴格驗證本機HTTPS存活端點。
首次冷啟動僅在curl明確28且尚無HTTP回應時，最多三次各二十秒的同步檢查；
TLS、連線錯誤與非200立即拒絕，永不忽略憑證或把逾時當成功。每次只保存嘗試數、exit與三位狀態，
不保存回應本文或stderr；超出有界冷啟動預算仍走原精準回復，沒有背景probe或跨部署狀態。
使用邊界與維護：遠端唯一主機為部署前重新驗證的測試虛擬機。站台與集區名稱、網域、端口、
內容根與整合登入資料庫皆為固定值，不能以參數改成正式站台。CRM設定只接受核定測試主機
的完整HTTPS服務網址；本檔不呼叫CRM、SQL、LINE或就緒端點，也不讀憑證私鑰。
CRM帳密只在Apply後從固定部署User Secrets讀取，透過現有Negotiate加密遠端Session
傳給唯一專用集區的環境變數，不寫入publish、web.config、套件、操作紀錄或輸出。
IIS管理設定及原生備份的管理員／SYSTEM權限是這份部署環境的秘密保存邊界；不宣稱為
加密保管庫。舊環境值只在單次遠端命令記憶體中保留供失敗回復，不寫入JSON回復紀錄。
憑證只能來自本機電腦個人存放區，須有精確萬用SAN、伺服器驗證用途、有效期及私鑰；不唯一
就拒絕，明確指紋也不能略過驗證。不新增四四三繫結、不更動CRM站台、不放寬TLS或驗證。
資源擁有權：單次呼叫擁有CRM SecureString、發佈子程序、兩個丟棄輸出的串流工作及唯一遠端Session；子程序
三分鐘逾時即終止整棵程序樹，工作排空後Dispose；Session必定在finally移除。遠端提交的
檔案鎖、IIS管理器、憑證與壓縮串流均在同一命令finally釋放，沒有背景job或跨請求狀態。
僅全新目錄設定封閉ACL；集區取得候選發佈目錄讀取執行、其Logs子目錄及F7固定public-assets根修改權限。
F7固定公開網址為/assets/；圖片根獨立於release且跨部署保留，舊圖清理另案處理。既有根須精確符合
管理員／SYSTEM完整權限與本站專用集區修改權限，否則拒絕且不重寫ACL；不列目錄、不公開其他檔案。
備份與還原：原發佈目錄及套件不刪除；IIS備份存於系統預設備份目錄，安全回復紀錄在固定
根目錄records內，僅系統與管理員可讀。已保留部署槽的失敗會記錄非秘密階段及例外型別，回傳失敗而不
輸出原例外內容；已切換時只復原本站原路徑、憑證與啟停狀態。首次建立失敗則保留停用站台，
供下一次重跑修復。若精準回復失敗，維持失敗結果並以紀錄及IIS備份人工核對；不得自動整站
Restore IIS備份，因為那會覆蓋同機CRM之後的變更。人工回復亦須先取得測試環境鎖、核對
紀錄與其他站台差異。備份、套件、候選目錄均保留證據；達二十次保留上限即拒絕再部署，
由維運人員先封存舊證據。IIS原生備份另有六十份上限，含失敗前備份亦計入。本機暫存發佈
同樣最多保留二十次，不能無界堆積。
維護與驗證：執行同目錄離線測試、PowerShell parser與文件及位元組守門。離線乾跑只代表
計畫可產生；VM身分、憑證鏈、Hosting Bundle、SQL授權、外部路由及實機就緒均仍須另驗。
.PARAMETER Apply
明確啟用變更；套用主機需PowerShell七版以保證發佈程序樹可有界終止。省略或搭配WhatIf
只產生離線計畫，不會讀取秘密或接觸遠端主機。
.PARAMETER ComputerName
WinRM連線位址，可使用主機名稱或IP；遠端回報的主機名稱每次仍必須精確等於核定主機。
只使用現有Negotiate授權，不設定TrustedHosts、不讀取憑證檔或改用其他驗證機制。
.PARAMETER Credential
選用的既有PSCredential，由呼叫端持有；本檔不保存、列印或匯出其內容。
.PARAMETER CertificateThumbprint
選用的四十位十六進位憑證指紋；省略時有效的精確萬用憑證必須恰好一張。
.PARAMETER CrmServerUrl
僅供產生設定的核定測試組織服務網址，本部署工具不連線此網址。
.EXAMPLE
.\Deploy-GoodShepherd2Test.ps1
只顯示乾跑計畫；遠端驗證明確標為尚未執行。
.EXAMPLE
pwsh -NoProfile -File .\Deploy-GoodShepherd2Test.ps1 -Apply -ComputerName D365APP01
已取得測試環境鎖及核准實機步驟後，使用現有身分套用；本檔不取得其他工作線的鎖。
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [switch]$Apply,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$')]
    [string]$ComputerName = 'D365APP01',
    [System.Management.Automation.PSCredential]$Credential,
    [ValidatePattern('^([A-Fa-f0-9]{40})?$')]
    [string]$CertificateThumbprint = '',
    [string]$CrmServerUrl = 'https://goodshepherd20.speechmessage.com.tw/XRMServices/2011/Organization.svc'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Gs2CrmUrl {
    # 網址只作設定輸入；絕對網址、完整服務路徑及精確主機共同構成拒絕正式組織的邊界。
    param([string]$Value)
    $uri = $null
    if (-not [uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -cne 'https' -or
        $uri.DnsSafeHost -ine 'goodshepherd20.speechmessage.com.tw' -or
        $uri.Port -ne 443 -or $uri.UserInfo -ne '' -or $uri.Query -ne '' -or $uri.Fragment -ne '' -or
        $uri.AbsolutePath -cne '/XRMServices/2011/Organization.svc') {
        throw 'GS2_CRM_URL_REJECTED'
    }
}

function Get-Gs2PlatformConnectionString {
    # 連線目標與憑證檔路徑皆為核定測試環境常數；Strict使用TDS 8.0並驗證伺服器，禁止信任任意憑證。
    # 憑證由P0-2④在VM外部準備；本工具只輸出其固定路徑，不匯出、覆寫或封裝憑證內容。
    return 'Server=D365APP01;Database=GoodShepherd2Platform;Integrated Security=True;Encrypt=Strict;TrustServerCertificate=False;ServerCertificate=C:\ProgramData\SpeechMessage\certs\d365-9.1-sql-strict-pin.cer;'
}

function New-Gs2DevelopmentSettings {
    # 只產生固定測試組織、無密碼平台連線、F7公開圖片設定及選用LIFF公開映射；不複製來源其他區段或秘密。
    # 來源只在本次封裝中借用；每項先驗型別、界限與完整性，再逐值建立獨立候選，無reload或共享狀態。
    param([Parameter(Mandatory)][string]$ServerUrl, [object]$LiffSettings)
    Assert-Gs2CrmUrl $ServerUrl
    $result = [ordered]@{
        CrmConnection = @{ ServerUrl=$ServerUrl }
        GoodShepherd2 = @{ AllowedDevelopmentOrganizations=@('goodshepherd20.speechmessage.com.tw') }
        ConnectionStrings = @{ Platform=(Get-Gs2PlatformConnectionString) }
        PublicAssets = @{ RootPath='C:\inetpub\GoodShepherd2Test\public-assets'; PublicBaseUrl='https://gs2-test.speechmessage.com.tw/assets/' }
    }
    if ($PSBoundParameters.ContainsKey('LiffSettings')) {
        $names = @('LoginChannelId', 'MessagingChannelKey', 'LiffId')
        if ($null -eq $LiffSettings -or @($LiffSettings.PSObject.Properties).Count -ne 3) {
            throw 'GS2_LIFF_PUBLIC_MAPPING_REJECTED'
        }
        $mapping = [ordered]@{}
        foreach ($name in $names) {
            $property = $LiffSettings.PSObject.Properties[$name]
            if ($null -eq $property -or $property.Value -isnot [string]) {
                throw 'GS2_LIFF_PUBLIC_MAPPING_REJECTED'
            }
            $mapping[$name] = [string]$property.Value
        }
        if ($mapping.LoginChannelId -cnotmatch '\A[0-9]{1,32}\z' -or
            $mapping.MessagingChannelKey -cnotmatch '\A[0-9]{1,32}\z' -or
            $mapping.LiffId.Length -gt 128 -or
            $mapping.LiffId -cnotmatch ('\A' + [regex]::Escape($mapping.LoginChannelId) + '-[A-Za-z0-9_-]{1,95}\z')) {
            throw 'GS2_LIFF_PUBLIC_MAPPING_REJECTED'
        }
        $result.Liff = $mapping
    }
    return $result
}

function New-Gs2PublicAssetsAcl {
    # 公開圖片仍只能由專用worker修改；不把父目錄、套件或其他產品授權給該身分。
    # 回傳區域ACL候選，沒有磁碟副作用；管理員／SYSTEM保留唯一管理所有權。
    param([Security.Principal.SecurityIdentifier]$PoolSid)
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544', $PoolSid.Value)) {
        $rights = [Security.AccessControl.FileSystemRights]::FullControl
        if ($sid -ceq $PoolSid.Value) { $rights = [Security.AccessControl.FileSystemRights]::Modify }
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new($sid), $rights,
            [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
            [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow))
    }
    return $acl
}

function Assert-Gs2PublicAssetsAcl {
    # 既有根只核驗不重寫；其他主體、過大權限、繼承或不完整ACL都拒絕部署，不能藉重跑放寬。
    # 合法根跨發佈版本保留，回復站台不刪除圖片，避免破壞既有不可變URL。
    param([Security.AccessControl.DirectorySecurity]$Acl, [Security.Principal.SecurityIdentifier]$PoolSid)
    if (-not $Acl.AreAccessRulesProtected -or
        $Acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin @('S-1-5-18', 'S-1-5-32-544')) {
        throw 'GS2_PUBLIC_ASSETS_OWNER_CONFLICT'
    }
    $actual = @($Acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    $expected = @((New-Gs2PublicAssetsAcl $PoolSid).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    if ($actual.Count -ne $expected.Count) { throw 'GS2_PUBLIC_ASSETS_ACL_CONFLICT' }
    foreach ($rule in $expected) {
        $matched = @($actual | Where-Object {
            $_.IdentityReference.Value -ceq $rule.IdentityReference.Value -and
            $_.FileSystemRights -eq $rule.FileSystemRights -and $_.AccessControlType -eq $rule.AccessControlType -and
            $_.InheritanceFlags -eq $rule.InheritanceFlags -and $_.PropagationFlags -eq $rule.PropagationFlags -and
            -not $_.IsInherited
        })
        if ($matched.Count -ne 1) { throw 'GS2_PUBLIC_ASSETS_ACL_CONFLICT' }
    }
}

function Initialize-Gs2PublicAssetsDirectory {
    # 父目錄由部署器先驗管理員專用ACL；本函式僅處理固定根且每層拒絕reparse。
    # 既有根不重寫或刪除；首次建立失敗保留封閉候選供診斷，不覆蓋既有公開Logo。
    param([Security.Principal.NTAccount]$PoolAccount)
    if ($PoolAccount.Value -ine 'IIS APPPOOL\GoodShepherd2Test') { throw 'GS2_PUBLIC_ASSETS_POOL_CONFLICT' }
    $path = 'C:\inetpub\GoodShepherd2Test\public-assets'
    Assert-Gs2NoReparse $path
    $poolSid = $PoolAccount.Translate([Security.Principal.SecurityIdentifier])
    if (-not (Test-Path -LiteralPath $path)) {
        [IO.Directory]::CreateDirectory($path) | Out-Null
        Set-Acl -LiteralPath $path -AclObject (New-Gs2PublicAssetsAcl $poolSid)
    }
    elseif (-not (Get-Item -LiteralPath $path -Force).PSIsContainer) { throw 'GS2_PUBLIC_ASSETS_DIRECTORY_CONFLICT' }
    Assert-Gs2PublicAssetsAcl (Get-Acl -LiteralPath $path) $poolSid
}

function Get-Gs2CrmCredential {
    # 僅精確選取已授權組織；缺項不能回退至舊組織或其他產品。純轉換不連線、不輸出設定。
    param([object]$Settings, [string]$ExpectedUrl)
    Assert-Gs2CrmUrl $ExpectedUrl
    $prefix = 'Deployment:Organizations:goodshepherd20:'
    $values = @{}
    foreach ($name in @('OrganizationUrl', 'Username', 'Password')) {
        $property = $Settings.PSObject.Properties[$prefix + $name]
        if ($null -eq $property -or $property.Value -isnot [string] -or
            [string]::IsNullOrWhiteSpace($property.Value) -or $property.Value.Length -gt 4096 -or
            $property.Value -match '[\x00-\x1f\x7f]') { throw 'GS2_CRM_CREDENTIAL_MISSING_OR_INVALID' }
        $values[$name] = $property.Value
    }
    if ($values.OrganizationUrl -cne $ExpectedUrl) { throw 'GS2_CRM_CREDENTIAL_ORGANIZATION_MISMATCH' }
    $secure = ConvertTo-SecureString -String $values.Password -AsPlainText -Force
    try { return [Management.Automation.PSCredential]::new($values.Username, $secure) }
    catch { $secure.Dispose(); throw 'GS2_CRM_CREDENTIAL_INVALID' }
}

function Get-Gs2CrmEnvironment {
    # 回復候選僅活於單次遠端命令；只讀兩個本工具擁有的鍵，不讀其他站台或集區環境值。
    param($Variables)
    $snapshot = @{}
    foreach ($variable in $Variables) {
        $name = [string]$variable['name']
        if ($name -in @('CrmConnection__Username', 'CrmConnection__Password')) {
            if ($snapshot.ContainsKey($name)) { throw 'GS2_CRM_ENVIRONMENT_DUPLICATE' }
            $snapshot[$name] = [string]$variable['value']
        }
    }
    return $snapshot
}

function Set-Gs2CrmEnvironment {
    # 設定或精準回復本工具擁有的兩鍵；缺鍵表示移除，其他環境完全保留，且不產生管線輸出。
    param($Variables, [hashtable]$Values)
    foreach ($name in @('CrmConnection__Username', 'CrmConnection__Password')) {
        $existing = @($Variables | Where-Object { $_['name'] -ieq $name })
        if ($existing.Count -gt 1) { throw 'GS2_CRM_ENVIRONMENT_DUPLICATE' }
        foreach ($variable in $existing) { $Variables.Remove($variable) | Out-Null }
        if ($Values.ContainsKey($name)) {
            $element = $Variables.CreateElement('add')
            $element['name'] = $name
            $element['value'] = $Values[$name]
            $Variables.Add($element) | Out-Null
        }
    }
}

function Test-Gs2CrmEnvironmentSnapshot {
    # 比對目前兩個集區自有鍵與回復快照；實際值只留在呼叫範圍記憶體，完成或失敗立即清除。
    param($Variables, [hashtable]$Expected)
    $actual = Get-Gs2CrmEnvironment $Variables
    try {
        if ($actual.Count -ne $Expected.Count) { return $false }
        foreach ($name in $Expected.Keys) {
            if (-not $actual.ContainsKey($name) -or $actual[$name] -cne $Expected[$name]) { return $false }
        }
        return $true
    }
    finally {
        if ($null -ne $actual) { $actual.Clear(); $actual = $null }
    }
}

function Get-Gs2AnonymousSection {
    # IIS無法解析不存在站台的location。首次安裝先建立未Commit的站台候選，才讀取本站設定。
    # 候選仍由單次ServerManager擁有；本方法本身不建立、啟動、Commit或釋放任何資源。
    param($Manager, $Site)
    if ($null -eq $Site) { return $null }
    return $Manager.GetApplicationHostConfiguration().GetSection(
        'system.webServer/security/authentication/anonymousAuthentication', 'GoodShepherd2Test')
}

function Select-Gs2Certificate {
    # 純快照選取不存取私鑰；指定指紋與自動選取共用全部條件，不以最新或名稱近似來猜測。
    param([object[]]$Candidates, [string]$Thumbprint)
    $now = [datetime]::UtcNow
    $valid = @($Candidates | Where-Object {
        $_.HasSan -and $_.HasPrivateKey -and $_.ServerAuthentication -and
        $_.NotBeforeUtc -le $now -and $_.NotAfterUtc -gt $now -and
        @($_.DnsNames | Where-Object { $_ -ieq '*.speechmessage.com.tw' }).Count -gt 0 -and
        ([string]::IsNullOrEmpty($Thumbprint) -or $_.Thumbprint -ieq $Thumbprint)
    })
    if ($valid.Count -ne 1) { throw 'GS2_CERTIFICATE_NOT_UNIQUE_OR_INVALID' }
    return $valid[0]
}

function Assert-Gs2Topology {
    # 站台名稱不是所有權證據；固定單一應用、繫結、發佈路徑與專用集區必須同時吻合。
    param([object[]]$Sites, [object]$Pool, [int[]]$ListeningPorts)
    $root = 'C:\inetpub\GoodShepherd2Test'
    $ownedBinding = $false
    foreach ($site in $Sites) {
        $own = $site.Name -ieq 'GoodShepherd2Test'
        if ($own -and ($site.ApplicationCount -ne 1 -or @($site.VirtualDirectories).Count -ne 1 -or
            @($site.Bindings).Count -ne 1)) { throw 'GS2_SITE_SHAPE_CONFLICT' }
        foreach ($binding in $site.Bindings) {
            if ($own) {
                if ($binding.Protocol -cne 'https' -or $binding.Port -ne 9443 -or
                    $binding.Information -ine '*:9443:gs2-test.speechmessage.com.tw' -or
                    $binding.SslFlags -ne 1) { throw 'GS2_BINDING_CONFLICT' }
                $ownedBinding = $true
            }
            elseif ($binding.Port -eq 9443) { throw 'GS2_PORT_CONFLICT' }
        }
        foreach ($directory in $site.VirtualDirectories) {
            $path = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($directory.PhysicalPath)).TrimEnd('\')
            if ($own) {
                $releasePrefix = $root + '\releases\'
                if ($directory.ApplicationPath -cne '/' -or $directory.Path -cne '/' -or
                    $directory.Pool -ine 'GoodShepherd2Test' -or
                    -not $path.StartsWith($releasePrefix, [StringComparison]::OrdinalIgnoreCase) -or
                    $path.Substring($releasePrefix.Length) -cnotmatch '^[0-9]{8}T[0-9]{6}Z-[a-f0-9]{32}$') {
                    throw 'GS2_CONTENT_OWNERSHIP_CONFLICT'
                }
            }
            elseif ($directory.Pool -ieq 'GoodShepherd2Test' -or $path -ieq $root -or
                $path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) -or
                $root.StartsWith($path + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw 'GS2_SHARED_POOL_OR_CONTENT_CONFLICT'
            }
        }
    }
    if ($null -ne $Pool -and ($Pool.IdentityType -ne 4 -or $Pool.Runtime -ne '' -or
        $Pool.Pipeline -ne 0 -or $Pool.Enable32Bit)) { throw 'GS2_POOL_IDENTITY_CONFLICT' }
    if ($ListeningPorts -contains 9443 -and -not $ownedBinding) { throw 'GS2_LISTENER_CONFLICT' }
}

function Assert-Gs2ArchiveName {
    # 雜湊證明傳輸完整性，但仍逐筆防止解壓越界、替代資料流與意外發佈私鑰檔案。
    param([string]$Name)
    if ([string]::IsNullOrEmpty($Name) -or $Name.Length -gt 240 -or
        $Name -match '[\\:\x00-\x1f]' -or $Name.StartsWith('/') -or $Name -match '//|(^|/)\.{1,2}(/|$)' -or
        $Name -match '(^|/)(secrets\.json|[^/]+\.(pfx|p12|pem|key|user))$' -or
        $Name -match '[ .](/|$)') { throw 'GS2_ARCHIVE_NAME_REJECTED' }
}

function Assert-Gs2NoReparse {
    # 對每個既有祖先檢查連結屬性；路徑字串位於根內仍不足以防止junction跨越信任邊界。
    param([string]$Path)
    $current = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrEmpty($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'GS2_REPARSE_PATH_REJECTED' }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

Assert-Gs2CrmUrl $CrmServerUrl
$plan = [pscustomobject][ordered]@{
    Mode = 'DryRun'
    SiteName = 'GoodShepherd2Test'
    ApplicationPool = 'GoodShepherd2Test'
    ExpectedMachine = 'D365APP01'
    Port = 9443
    HostName = 'gs2-test.speechmessage.com.tw'
    ContentRoot = 'C:\inetpub\GoodShepherd2Test'
    Environment = 'Development'
    PlatformAuthentication = 'WindowsIntegrated;Encrypted;CertificateValidated'
    BackupRequired = $true
    RemoteValidation = 'Pending'
}
if (-not $Apply -or $WhatIfPreference) { return $plan }
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'GS2_APPLY_REQUIRES_POWERSHELL_7' }
if (-not $PSCmdlet.ShouldProcess('D365APP01/GoodShepherd2Test:9443', 'Publish and deploy isolated test site')) { return $plan }

$runId = [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ', [Globalization.CultureInfo]::InvariantCulture) + '-' + [guid]::NewGuid().ToString('N')
$session = $null
$crmCredential = $null
$phase = 'Publish'

# 純驗證函式的來源只來自本檔固定函式，不接受外部程式碼；遠端與離線測試共用相同拒絕規則。
$validatorSources = @(
    'function Initialize-Gs2PublicAssetsDirectory {' + ${function:Initialize-Gs2PublicAssetsDirectory}.ToString() + '}'
    'function New-Gs2PublicAssetsAcl {' + ${function:New-Gs2PublicAssetsAcl}.ToString() + '}'
    'function Assert-Gs2PublicAssetsAcl {' + ${function:Assert-Gs2PublicAssetsAcl}.ToString() + '}'
    'function Assert-Gs2CrmUrl {' + ${function:Assert-Gs2CrmUrl}.ToString() + '}'
    'function Select-Gs2Certificate {' + ${function:Select-Gs2Certificate}.ToString() + '}'
    'function Assert-Gs2Topology {' + ${function:Assert-Gs2Topology}.ToString() + '}'
    'function Assert-Gs2ArchiveName {' + ${function:Assert-Gs2ArchiveName}.ToString() + '}'
    'function Assert-Gs2NoReparse {' + ${function:Assert-Gs2NoReparse}.ToString() + '}'
    'function Get-Gs2CrmEnvironment {' + ${function:Get-Gs2CrmEnvironment}.ToString() + '}'
    'function Set-Gs2CrmEnvironment {' + ${function:Set-Gs2CrmEnvironment}.ToString() + '}'
    'function Test-Gs2CrmEnvironmentSnapshot {' + ${function:Test-Gs2CrmEnvironmentSnapshot}.ToString() + '}'
    'function Get-Gs2AnonymousSection {' + ${function:Get-Gs2AnonymousSection}.ToString() + '}'
)

$remoteDeployment = {
    param([string]$Stage, [string]$Id, [string]$Thumbprint, [string]$PackageHash, [string[]]$Validators,
        [Management.Automation.PSCredential]$CrmCredential)
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    # 每次遠端呼叫都重新驗證身分，不能把IP或前一次查詢視為永久授權。
    if ([Environment]::MachineName -ine 'D365APP01') { throw 'GS2_MACHINE_IDENTITY_REJECTED' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'GS2_REMOTE_ADMIN_REQUIRED'
        }
    }
    finally { $identity.Dispose() }
    foreach ($source in $Validators) { . ([scriptblock]::Create($source)) }
    if ($Stage -notin @('Prepare', 'Commit') -or $Id -cnotmatch '^[0-9]{8}T[0-9]{6}Z-[a-f0-9]{32}$') {
        throw 'GS2_REMOTE_INPUT_REJECTED'
    }
    if ($Stage -eq 'Commit' -and $null -eq $CrmCredential) { throw 'GS2_CRM_CREDENTIAL_REQUIRED' }
    $root = 'C:\inetpub\GoodShepherd2Test'
    $release = Join-Path $root ('releases\' + $Id)
    $package = Join-Path $root ('packages\' + $Id + '.zip')
    $record = Join-Path $root ('records\' + $Id + '.json')
    $backupName = 'GoodShepherd2Test-' + $Id + '-' + $Stage
    $appcmd = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
    $manager = $null
    $lock = $null
    $previous = $null
    $previousCrmEnvironment = $null
    $changed = $false
    $committed = $false
    $hasRecord = $false
    $rollback = 'NotRequired'
    $remotePhase = 'Preflight'
    $recordData = [ordered]@{ RunId=$Id; Backup=$backupName; Phase=$remotePhase; Result='Pending'; Rollback=$rollback }

    function Assert-Gs2PrivateDirectoryAcl {
        # 所有權標記不能取代實際ACL；非管理員可寫父目錄時，雜湊後仍可能遭替換，必須拒絕。
        param([string]$Path)
        $acl = Get-Acl -LiteralPath $Path
        if (-not $acl.AreAccessRulesProtected -or
            $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin @('S-1-5-18', 'S-1-5-32-544')) {
            throw 'GS2_PRIVATE_DIRECTORY_OWNER_CONFLICT'
        }
        foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
            if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and
                $rule.IdentityReference.Value -notin @('S-1-5-18', 'S-1-5-32-544')) {
                throw 'GS2_PRIVATE_DIRECTORY_ACL_CONFLICT'
            }
        }
    }

    function New-Gs2PrivateDirectory {
        # 僅全新目錄套用封閉ACL，既有目錄不藉部署放寬權限；唯一寫入者為管理員與系統。
        param([string]$Path)
        Assert-Gs2NoReparse $Path
        if (-not (Test-Path -LiteralPath $Path)) {
            $directory = [IO.Directory]::CreateDirectory($Path)
            $acl = New-Object Security.AccessControl.DirectorySecurity
            $acl.SetAccessRuleProtection($true, $false)
            $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
            foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
                $rule = New-Object Security.AccessControl.FileSystemAccessRule(
                    [Security.Principal.SecurityIdentifier]::new($sid),
                    [Security.AccessControl.FileSystemRights]::FullControl,
                    [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
                    [Security.AccessControl.PropagationFlags]::None,
                    [Security.AccessControl.AccessControlType]::Allow)
                $acl.AddAccessRule($rule)
            }
            Set-Acl -LiteralPath $directory.FullName -AclObject $acl
        }
        elseif (-not (Get-Item -LiteralPath $Path -Force).PSIsContainer) { throw 'GS2_DIRECTORY_CONFLICT' }
        Assert-Gs2PrivateDirectoryAcl $Path
    }

    function Backup-Gs2Iis {
        # 含前置失敗的備份也計入六十份硬上限，避免拒絕重跑仍持續產生無界備份。
        param([string]$Name)
        $backupRoot = Join-Path $env:windir 'System32\inetsrv\backup'
        Assert-Gs2NoReparse $backupRoot
        if ((Test-Path -LiteralPath $backupRoot) -and
            @(Get-ChildItem -LiteralPath $backupRoot -Directory -Filter 'GoodShepherd2Test-*').Count -ge 60) {
            throw 'GS2_IIS_BACKUP_RETENTION_LIMIT'
        }
        & $appcmd add backup $Name *> $null
        if ($LASTEXITCODE -ne 0) { throw 'GS2_IIS_BACKUP_FAILED' }
    }

    function Get-Gs2Topology {
        # 只投影判斷所有權必要的IIS中繼資料，絕不讀取集區密碼、連線字串或其他站台設定內容。
        param($Manager)
        $sites = @(foreach ($site in $Manager.Sites) {
            $directories = @(foreach ($app in $site.Applications) {
                foreach ($dir in $app.VirtualDirectories) {
                    [pscustomobject]@{ ApplicationPath=$app.Path; Path=$dir.Path; Pool=$app.ApplicationPoolName; PhysicalPath=$dir.PhysicalPath }
                }
            })
            $bindings = @(foreach ($binding in $site.Bindings) {
                $port = -1
                $sslFlags = 0
                if ($binding.Protocol -in @('http', 'https')) { $port = $binding.EndPoint.Port }
                if ($binding.Protocol -eq 'https') { $sslFlags = [int]$binding.SslFlags }
                [pscustomobject]@{ Protocol=$binding.Protocol; Port=$port; Information=$binding.BindingInformation; SslFlags=$sslFlags }
            })
            [pscustomobject]@{ Name=$site.Name; ApplicationCount=$site.Applications.Count; VirtualDirectories=$directories; Bindings=$bindings }
        })
        $pool = $Manager.ApplicationPools['GoodShepherd2Test']
        $poolState = $null
        if ($null -ne $pool) {
            $poolState = [pscustomobject]@{
                IdentityType=[int]$pool.ProcessModel.IdentityType; Runtime=$pool.ManagedRuntimeVersion
                Pipeline=[int]$pool.ManagedPipelineMode; Enable32Bit=$pool.Enable32BitAppOnWin64
            }
        }
        $ports = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | ForEach-Object Port)
        Assert-Gs2Topology $sites $poolState $ports
    }

    function Get-Gs2Certificate {
        # 憑證物件只活在本次讀取；回傳雜湊與純值後全部Dispose，不存取或匯出私鑰。
        $certificates = @(Get-ChildItem -LiteralPath 'Cert:\LocalMachine\My')
        try {
            $snapshots = @(foreach ($cert in $certificates) {
                $eku = @($cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' })
                $serverAuth = $false
                if ($eku.Count -eq 1) {
                    $serverAuth = @($eku[0].EnhancedKeyUsages | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.1' }).Count -gt 0
                }
                [pscustomobject]@{
                    Thumbprint=$cert.Thumbprint; Hash=$cert.GetCertHash()
                    HasSan=(@($cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' }).Count -eq 1)
                    DnsNames=@($cert.DnsNameList | ForEach-Object { $_.Unicode })
                    HasPrivateKey=$cert.HasPrivateKey; ServerAuthentication=$serverAuth
                    NotBeforeUtc=$cert.NotBefore.ToUniversalTime(); NotAfterUtc=$cert.NotAfter.ToUniversalTime()
                }
            })
            return (Select-Gs2Certificate $snapshots $Thumbprint)
        }
        finally { foreach ($cert in $certificates) { $cert.Dispose() } }
    }

    function Wait-Gs2PoolStopped {
        # 停止與排空有三十秒上限；每次取得的管理器立即Dispose，不把狀態輪詢變成常駐工作。
        $watch = [Diagnostics.Stopwatch]::StartNew()
        do {
            $probe = New-Object Microsoft.Web.Administration.ServerManager
            try {
                $pool = $probe.ApplicationPools['GoodShepherd2Test']
                if ($null -eq $pool -or ($pool.State.ToString() -eq 'Stopped' -and $pool.WorkerProcesses.Count -eq 0)) { return }
            }
            finally { $probe.Dispose() }
            Start-Sleep -Milliseconds 250
        } while ($watch.Elapsed.TotalSeconds -lt 30)
        throw 'GS2_POOL_DRAIN_TIMEOUT'
    }

    function Invoke-Gs2StrictLiveness {
        # IIS首次要求才啟動ANCM；實機HTTPERR證明原20秒要求已Connection_Dropped後，ANCM才成功啟動。
        # 每次保留相同SNI、Host、嚴格憑證及20秒硬期限，只有28／000容許下一次同步觀測。
        # 三次為最多60秒的有界啟動檢查，不重送部署、不更動IIS設定；任何非200／TLS錯誤立即回復。
        # Record只屬本次部署，安全純值供失敗記錄；不保留curl本文、原始錯誤、credential或process。
        param([Parameter(Mandatory)][Collections.IDictionary]$Record)
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            $status = & curl.exe --silent --noproxy '*' --connect-timeout 5 --max-time 20 --resolve 'gs2-test.speechmessage.com.tw:9443:127.0.0.1' --output NUL --write-out '%{http_code}' 'https://gs2-test.speechmessage.com.tw:9443/health/live' 2>$null
            $exitCode = $LASTEXITCODE
            $Record['LivenessAttempts'] = $attempt
            $Record['LivenessExitCode'] = $exitCode
            $Record['LivenessHttpStatus'] = 'invalid'
            if ($status -is [string] -and $status -cmatch '\A[0-9]{3}\z') {
                $Record['LivenessHttpStatus'] = $status
            }
            if ($exitCode -eq 0 -and $status -is [string] -and $status -ceq '200') { return }
            if ($exitCode -ne 28 -or $status -isnot [string] -or $status -cne '000') { break }
        }
        throw 'GS2_STRICT_LIVENESS_FAILED'
    }

    function Write-Gs2Record {
        # 僅固定階段、型別、路徑與雜湊，無例外訊息或其他站台資料；紀錄與套件受管理員ACL保護。
        param([object]$Data)
        Assert-Gs2NoReparse $record
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Data | ConvertTo-Json -Depth 5) + "`r`n")
        $stream = [IO.File]::Open($record, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
        finally { $stream.Dispose() }
    }

    try {
        Assert-Gs2NoReparse $root
        if (-not (Test-Path -LiteralPath $appcmd -PathType Leaf)) { throw 'GS2_IIS_NOT_INSTALLED' }
        Add-Type -Path (Join-Path $env:windir 'System32\inetsrv\Microsoft.Web.Administration.dll')
        $manager = New-Object Microsoft.Web.Administration.ServerManager
        Get-Gs2Topology $manager
        $certificate = Get-Gs2Certificate
        $globalModules = $manager.GetApplicationHostConfiguration().GetSection('system.webServer/globalModules').GetCollection()
        if (@($globalModules | Where-Object { $_.GetAttributeValue('name') -eq 'AspNetCoreModuleV2' }).Count -ne 1) {
            throw 'GS2_HOSTING_BUNDLE_REQUIRED'
        }
        if ($null -eq (Get-Command curl.exe -CommandType Application -ErrorAction SilentlyContinue)) {
            throw 'GS2_CURL_REQUIRED_FOR_STRICT_LOCAL_TLS_PROBE'
        }
        $marker = Join-Path $root '.deployment-owner'
        if (Test-Path -LiteralPath $root) {
            Assert-Gs2NoReparse $marker
            Assert-Gs2PrivateDirectoryAcl $root
            if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or
                (Get-Item -LiteralPath $marker).Length -gt 128 -or
                [IO.File]::ReadAllText($marker) -cne "GoodShepherd2Test/D365APP01/v1`r`n") {
                throw 'GS2_ROOT_OWNERSHIP_CONFLICT'
            }
        }
        # 封閉證據根與獨占鎖不變更IIS；備份成功之前不允許上傳或切換站台。
        New-Gs2PrivateDirectory $root
        if (-not (Test-Path -LiteralPath $marker)) {
            $markerStream = [IO.File]::Open($marker, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                $bytes = [Text.Encoding]::ASCII.GetBytes("GoodShepherd2Test/D365APP01/v1`r`n")
                $markerStream.Write($bytes, 0, $bytes.Length)
                $markerStream.Flush($true)
            }
            finally { $markerStream.Dispose() }
        }
        foreach ($name in @('releases', 'packages', 'records')) { New-Gs2PrivateDirectory (Join-Path $root $name) }
        Assert-Gs2NoReparse (Join-Path $root 'deployment.lock')
        $lock = [IO.File]::Open((Join-Path $root 'deployment.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        foreach ($name in @('releases', 'packages', 'records')) {
            $retained = @(Get-ChildItem -LiteralPath (Join-Path $root $name) -Force).Count
            if ($retained -gt 20 -or ($Stage -eq 'Prepare' -and $retained -ge 20)) { throw 'GS2_RETENTION_LIMIT_ARCHIVE_EVIDENCE_FIRST' }
        }
        if ($Stage -eq 'Prepare') {
            if ((Test-Path -LiteralPath $package) -or (Test-Path -LiteralPath $record)) { throw 'GS2_PACKAGE_ALREADY_EXISTS' }
            Backup-Gs2Iis $backupName
            # 鎖內先保留記錄槽再允許上傳，並行Prepare也不能超過二十次證據上限。
            $recordData.Phase = 'Prepared'
            $recordData.Result = 'PendingTransfer'
            Write-Gs2Record $recordData
            $hasRecord = $true
            return [pscustomobject]@{ PackagePath=$package; Backup=$backupName }
        }
        Assert-Gs2NoReparse $record
        if (-not (Test-Path -LiteralPath $record -PathType Leaf) -or (Get-Item -LiteralPath $record).Length -gt 4096) {
            throw 'GS2_DEPLOYMENT_RESERVATION_MISSING'
        }
        $reservation = [IO.File]::ReadAllText($record) | ConvertFrom-Json
        if ($reservation.RunId -cne $Id -or $reservation.Phase -cne 'Prepared' -or $reservation.Result -cne 'PendingTransfer') {
            throw 'GS2_DEPLOYMENT_RESERVATION_INVALID'
        }
        $hasRecord = $true
        Backup-Gs2Iis $backupName
        $remotePhase = 'Package'
        Assert-Gs2NoReparse $package
        if ($PackageHash -cnotmatch '^[A-Fa-f0-9]{64}$' -or
            -not (Test-Path -LiteralPath $package -PathType Leaf) -or
            (Get-Item -LiteralPath $package).Length -gt 536870912 -or
            (Test-Path -LiteralPath $release)) { throw 'GS2_PACKAGE_REJECTED' }
        Add-Type -AssemblyName System.IO.Compression
        $archiveStream = [IO.File]::Open($package, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try {
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $actualHash = [BitConverter]::ToString($sha.ComputeHash($archiveStream)).Replace('-', '') }
            finally { $sha.Dispose() }
            if ($actualHash -ine $PackageHash) { throw 'GS2_PACKAGE_HASH_MISMATCH' }
            $archiveStream.Position = 0
            $archive = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Read, $true)
            try {
                if ($archive.Entries.Count -eq 0 -or $archive.Entries.Count -gt 4096) { throw 'GS2_PACKAGE_ENTRY_LIMIT' }
                $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                $total = [long]0
                foreach ($entry in $archive.Entries) {
                    Assert-Gs2ArchiveName $entry.FullName
                    if ($entry.FullName -imatch '^Logs(/|$)') { throw 'GS2_PACKAGE_LOGS_DIRECTORY_RESERVED' }
                    if (-not $seen.Add($entry.FullName.TrimEnd('/'))) { throw 'GS2_PACKAGE_DUPLICATE_PATH' }
                    $total += $entry.Length
                    if ($total -gt 1073741824) { throw 'GS2_PACKAGE_SIZE_LIMIT' }
                }
                foreach ($required in @('SpeechMessageProducts.GoodShepherd2.Web.dll', 'SpeechMessageProducts.GoodShepherd2.Web.runtimeconfig.json', 'web.config', 'appsettings.Development.json')) {
                    if (-not $seen.Contains($required)) { throw 'GS2_PACKAGE_REQUIRED_FILE_MISSING' }
                }
                New-Gs2PrivateDirectory $release
                foreach ($entry in $archive.Entries) {
                    $target = [IO.Path]::GetFullPath((Join-Path $release $entry.FullName))
                    if (-not $target.StartsWith($release + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'GS2_EXTRACTION_ESCAPE' }
                    if ($entry.FullName.EndsWith('/')) { [IO.Directory]::CreateDirectory($target) | Out-Null; continue }
                    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
                    $inputStream = $entry.Open()
                    try {
                        $outputStream = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                        try {
                            $buffer = New-Object byte[] 81920
                            $copied = [long]0
                            while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                                $copied += $read
                                if ($copied -gt $entry.Length) { throw 'GS2_ARCHIVE_LENGTH_MISMATCH' }
                                $outputStream.Write($buffer, 0, $read)
                            }
                            if ($copied -ne $entry.Length) { throw 'GS2_ARCHIVE_LENGTH_MISMATCH' }
                            $outputStream.Flush($true)
                        }
                        finally { $outputStream.Dispose() }
                    }
                    finally { $inputStream.Dispose() }
                }
            }
            finally { $archive.Dispose() }
        }
        finally { $archiveStream.Dispose() }

        $remotePhase = 'Switch'
        # 套件上傳與解壓期間可能有其他管理操作，停止集區前用全新快照再次核驗並備份。
        $manager.Dispose()
        $manager = New-Object Microsoft.Web.Administration.ServerManager
        Get-Gs2Topology $manager
        $certificate = Get-Gs2Certificate
        $site = $manager.Sites['GoodShepherd2Test']
        $pool = $manager.ApplicationPools['GoodShepherd2Test']
        $anonymous = Get-Gs2AnonymousSection $manager $site
        if ($null -ne $site -and $anonymous.GetAttributeValue('userName') -ne '') { throw 'GS2_EXISTING_ANONYMOUS_IDENTITY_CONFLICT' }
        $previous = [ordered]@{
            SiteExists=($null -ne $site); PoolExists=($null -ne $pool)
            SiteStarted=$false; PoolStarted=$false; PhysicalPath=''; Certificate=''; Store='My'
            SiteAutoStart=$false; PoolAutoStart=$false; SiteId=0
        }
        if ($null -ne $site) {
            $previous.SiteStarted = $site.State.ToString() -eq 'Started'
            $previous.SiteAutoStart = $site.ServerAutoStart
            $previous.SiteId = $site.Id
            $previous.PhysicalPath = $site.Applications['/'].VirtualDirectories['/'].PhysicalPath
            $previous.Certificate = [Convert]::ToBase64String($site.Bindings[0].CertificateHash)
            $previous.Store = $site.Bindings[0].CertificateStoreName
            Assert-Gs2NoReparse $previous.PhysicalPath
        }
        if ($null -ne $pool) {
            $previous.PoolStarted = $pool.State.ToString() -eq 'Started'
            $previous.PoolAutoStart = $pool.AutoStart
        }
        $recordData.Previous = $previous
        $recordData.PackageSha256 = $PackageHash
        $recordData.Phase = 'BeforeSwitch'
        Write-Gs2Record $recordData
        Backup-Gs2Iis ($backupName + '-BeforeSwitch')
        $recordData.Backup = $backupName + '-BeforeSwitch'
        Write-Gs2Record $recordData
        $changed = $true
        if ($null -ne $site -and $site.State.ToString() -ne 'Stopped') { $site.Stop() | Out-Null }
        if ($null -ne $pool -and $pool.State.ToString() -ne 'Stopped') { $pool.Stop() | Out-Null }
        Wait-Gs2PoolStopped
        if ($null -eq $pool) {
            $pool = $manager.ApplicationPools.Add('GoodShepherd2Test')
            $pool.ManagedRuntimeVersion = ''
            $pool.ManagedPipelineMode = 0
            $pool.Enable32BitAppOnWin64 = $false
            $pool.ProcessModel.IdentityType = 4
        }
        $pool.AutoStart = $false
        # IIS集區環境由專用worker在啟動時繼承；不是部署操作者的User Secrets或通用IIS帳號。
        # 舊值不能放入recordData；回復時只還原這兩鍵，不覆蓋其他管理設定。
        $crmVariables = $pool.GetCollection('environmentVariables')
        $previousCrmEnvironment = Get-Gs2CrmEnvironment $crmVariables
        $candidateEnvironment = @{
            CrmConnection__Username = $CrmCredential.UserName
            CrmConnection__Password = $CrmCredential.GetNetworkCredential().Password
        }
        try { Set-Gs2CrmEnvironment $crmVariables $candidateEnvironment }
        finally { $candidateEnvironment.Clear(); $candidateEnvironment = $null }
        if ($null -eq $site) {
            $site = $manager.Sites.Add('GoodShepherd2Test', 'https', '*:9443:gs2-test.speechmessage.com.tw', $release)
        }
        $site.ServerAutoStart = $false
        $site.Applications['/'].ApplicationPoolName = 'GoodShepherd2Test'
        $site.Applications['/'].VirtualDirectories['/'].PhysicalPath = $release
        $site.Bindings[0].CertificateHash = [byte[]]$certificate.Hash
        $site.Bindings[0].CertificateStoreName = 'My'
        $site.Bindings[0].SslFlags = 1
        # 匿名檔案存取亦使用專用集區身分，避免為了網站可讀而授權共用IUSR或IIS_IUSRS。
        # 首次安裝到此才已有記憶體中的站台候選，IIS才可解析location；仍未Commit或啟動。
        $anonymous = Get-Gs2AnonymousSection $manager $site
        $anonymous.SetAttributeValue('userName', '')
        $manager.CommitChanges()
        $committed = $true
        $poolAccount = [Security.Principal.NTAccount]::new('IIS APPPOOL\GoodShepherd2Test')
        $releaseAcl = Get-Acl -LiteralPath $release
        $releaseAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $poolAccount, [Security.AccessControl.FileSystemRights]::ReadAndExecute,
            [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
            [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow))
        Set-Acl -LiteralPath $release -AclObject $releaseAcl
        $logs = Join-Path $release 'Logs'
        if (Test-Path -LiteralPath $logs) { throw 'GS2_PACKAGE_LOGS_DIRECTORY_RESERVED' }
        New-Gs2PrivateDirectory $logs
        $logsAcl = Get-Acl -LiteralPath $logs
        $logsAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $poolAccount, [Security.AccessControl.FileSystemRights]::Modify,
            [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
            [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow))
        Set-Acl -LiteralPath $logs -AclObject $logsAcl
        Initialize-Gs2PublicAssetsDirectory $poolAccount
        # Commit後的ConfigurationElement已封存，不可沿用舊pool／site修改AutoStart。
        # ACL完成後重開唯一管理器，重新核對所有權與排空狀態才允許啟用；失敗仍走既有精準回復。
        $manager.Dispose()
        $manager = New-Object Microsoft.Web.Administration.ServerManager
        Get-Gs2Topology $manager
        $site = $manager.Sites['GoodShepherd2Test']
        $pool = $manager.ApplicationPools['GoodShepherd2Test']
        if ($null -eq $site -or $null -eq $pool -or
            $site.Applications['/'].VirtualDirectories['/'].PhysicalPath -ine $release -or
            $site.State.ToString() -ne 'Stopped' -or $pool.State.ToString() -ne 'Stopped' -or
            $pool.WorkerProcesses.Count -ne 0) { throw 'GS2_ACTIVATION_OWNERSHIP_OR_STATE_CONFLICT' }
        $pool.AutoStart = $true
        $site.ServerAutoStart = $true
        $manager.CommitChanges()
        # 執行期啟動也只消費新的已提交快照，避免把組態寫入與服務控制混在失效物件上。
        $manager.Dispose()
        $manager = New-Object Microsoft.Web.Administration.ServerManager
        Get-Gs2Topology $manager
        $site = $manager.Sites['GoodShepherd2Test']
        $pool = $manager.ApplicationPools['GoodShepherd2Test']
        if ($null -eq $site -or $null -eq $pool -or
            $site.Applications['/'].VirtualDirectories['/'].PhysicalPath -ine $release) {
            throw 'GS2_ACTIVATION_OWNERSHIP_OR_STATE_CONFLICT'
        }
        if ($pool.State.ToString() -ne 'Started') { $pool.Start() | Out-Null }
        if ($site.State.ToString() -ne 'Started') { $site.Start() | Out-Null }
        $remotePhase = 'Liveness'
        Invoke-Gs2StrictLiveness -Record $recordData
        $recordData.Phase = 'Completed'
        $recordData.Result = 'LivenessPassed'
        Write-Gs2Record $recordData
        return [pscustomobject]@{ RunId=$Id; Result='LivenessPassed'; Backup=$recordData.Backup; Record=$record; Release=$release }
    }
    catch {
        $failureType = $_.Exception.GetType().FullName
        $failureLine = $_.InvocationInfo.ScriptLineNumber
        if ($changed -and $null -ne $previous) {
            $rollback = 'Failed'
            try {
                if ($null -ne $manager) { $manager.Dispose(); $manager = $null }
                $manager = New-Object Microsoft.Web.Administration.ServerManager
                Get-Gs2Topology $manager
                $site = $manager.Sites['GoodShepherd2Test']
                $pool = $manager.ApplicationPools['GoodShepherd2Test']
                if (-not $committed) {
                    # 初次設定尚未Commit時，丟棄舊管理器即可捨棄記憶體候選；不可再Commit它們。
                    # 只有原先存在的站台／集區才可能需要恢復執行狀態，未提交的新物件不得被留下。
                    if ([bool]$previous.SiteExists -ne ($null -ne $site) -or
                        [bool]$previous.PoolExists -ne ($null -ne $pool)) {
                        throw 'GS2_ROLLBACK_UNCOMMITTED_RESOURCE_CONFLICT'
                    }
                    if ($previous.SiteExists -and
                        ($site.Id -ne $previous.SiteId -or
                         $site.Applications['/'].VirtualDirectories['/'].PhysicalPath -ine $previous.PhysicalPath -or
                         [Convert]::ToBase64String([byte[]]$site.Bindings[0].CertificateHash) -cne $previous.Certificate -or
                         $site.Bindings[0].CertificateStoreName -cne $previous.Store -or
                         [bool]$site.ServerAutoStart -ne [bool]$previous.SiteAutoStart)) {
                        throw 'GS2_ROLLBACK_UNCOMMITTED_SITE_CONFIGURATION_CONFLICT'
                    }
                    if ($previous.PoolExists -and
                        ([bool]$pool.AutoStart -ne [bool]$previous.PoolAutoStart -or
                         ($null -ne $previousCrmEnvironment -and
                          -not (Test-Gs2CrmEnvironmentSnapshot ($pool.GetCollection('environmentVariables')) $previousCrmEnvironment)))) {
                        throw 'GS2_ROLLBACK_UNCOMMITTED_POOL_CONFIGURATION_CONFLICT'
                    }
                    if (-not $previous.SiteExists -and -not $previous.PoolExists) {
                        $rollback = 'NoChangeCommitted'
                    }
                    else {
                        $rollback = 'PreviousConfigurationRestoredActivationIncomplete'
                        try {
                            if ($previous.PoolExists -and $previous.PoolStarted -and $pool.State.ToString() -ne 'Started') { $pool.Start() | Out-Null }
                            if ($previous.SiteExists -and $previous.SiteStarted -and $site.State.ToString() -ne 'Started') { $site.Start() | Out-Null }
                            $expectedPoolState = if ($previous.PoolStarted) { 'Started' } else { 'Stopped' }
                            $expectedSiteState = if ($previous.SiteStarted) { 'Started' } else { 'Stopped' }
                            if (($previous.PoolExists -and $pool.State.ToString() -cne $expectedPoolState) -or
                                ($previous.SiteExists -and $site.State.ToString() -cne $expectedSiteState)) {
                                throw 'GS2_ROLLBACK_UNCOMMITTED_RUNTIME_STATE_CONFLICT'
                            }
                            $rollback = 'PreviousStateRestored'
                        }
                        catch {
                            if ($null -ne $site -and $site.State.ToString() -ne 'Stopped') { $site.Stop() | Out-Null }
                            if ($null -ne $pool -and $pool.State.ToString() -ne 'Stopped') { $pool.Stop() | Out-Null }
                            Wait-Gs2PoolStopped
                            if (($null -ne $site -and $site.State.ToString() -ne 'Stopped') -or
                                ($null -ne $pool -and $pool.State.ToString() -ne 'Stopped')) {
                                throw 'GS2_ROLLBACK_UNCOMMITTED_ACTIVATION_DRAIN_FAILED'
                            }
                            $rollback = 'PreviousConfigurationRestoredActivationIncomplete'
                        }
                    }
                }
                else {
                # 人工作業若已切到另一個合法版本，也不得先停站再判斷；回復前先驗證本次所有權。
                if ($previous.SiteExists) {
                    $expectedPath = $previous.PhysicalPath
                    if ($committed) { $expectedPath = $release }
                    if ($null -eq $site -or $site.Id -ne $previous.SiteId -or
                        $site.Applications['/'].VirtualDirectories['/'].PhysicalPath -ine $expectedPath) {
                        throw 'GS2_ROLLBACK_OWNERSHIP_CONFLICT'
                    }
                }
                elseif ($null -ne $site -and $site.Applications['/'].VirtualDirectories['/'].PhysicalPath -ine $release) {
                    throw 'GS2_ROLLBACK_OWNERSHIP_CONFLICT'
                }
                if ($null -ne $site -and $site.State.ToString() -ne 'Stopped') { $site.Stop() | Out-Null }
                if ($null -ne $pool -and $pool.State.ToString() -ne 'Stopped') { $pool.Stop() | Out-Null }
                Wait-Gs2PoolStopped
                if ($previous.SiteExists) {
                    if ($null -eq $site -or $site.Id -ne $previous.SiteId -or
                        ($committed -and $site.Applications['/'].VirtualDirectories['/'].PhysicalPath -ine $release)) {
                        throw 'GS2_ROLLBACK_OWNERSHIP_CONFLICT'
                    }
                    $site.Applications['/'].VirtualDirectories['/'].PhysicalPath = $previous.PhysicalPath
                    $site.Bindings[0].CertificateHash = [Convert]::FromBase64String($previous.Certificate)
                    $site.Bindings[0].CertificateStoreName = $previous.Store
                    $site.ServerAutoStart = $previous.SiteAutoStart
                }
                elseif ($null -ne $site) {
                    if ($site.Applications['/'].VirtualDirectories['/'].PhysicalPath -ine $release) { throw 'GS2_ROLLBACK_OWNERSHIP_CONFLICT' }
                    $site.ServerAutoStart = $false
                }
                if ($null -ne $pool) {
                    $pool.AutoStart = $previous.PoolAutoStart
                    if ($null -ne $previousCrmEnvironment) {
                        Set-Gs2CrmEnvironment ($pool.GetCollection('environmentVariables')) $previousCrmEnvironment
                    }
                }
                $manager.CommitChanges()
                # CommitChanges會封存本管理器持有的ConfigurationElement；舊pool/site不可再啟動。
                # 先釋放快照，再由新管理器確認已提交的舊路徑、憑證及自動啟動設定。
                $manager.Dispose()
                $manager = $null
                $manager = New-Object Microsoft.Web.Administration.ServerManager
                Get-Gs2Topology $manager
                $site = $manager.Sites['GoodShepherd2Test']
                $pool = $manager.ApplicationPools['GoodShepherd2Test']
                if ($previous.SiteExists) {
                    if ($null -eq $site -or $null -eq $pool -or $site.Id -ne $previous.SiteId -or
                        $site.Applications['/'].VirtualDirectories['/'].PhysicalPath -ine $previous.PhysicalPath -or
                        [Convert]::ToBase64String([byte[]]$site.Bindings[0].CertificateHash) -cne $previous.Certificate -or
                        $site.Bindings[0].CertificateStoreName -cne $previous.Store -or
                        [bool]$site.ServerAutoStart -ne [bool]$previous.SiteAutoStart -or
                        [bool]$pool.AutoStart -ne [bool]$previous.PoolAutoStart) {
                        throw 'GS2_ROLLBACK_COMMITTED_CONFIGURATION_CONFLICT'
                    }
                    if ($null -ne $previousCrmEnvironment -and
                        -not (Test-Gs2CrmEnvironmentSnapshot ($pool.GetCollection('environmentVariables')) $previousCrmEnvironment)) {
                        throw 'GS2_ROLLBACK_COMMITTED_POOL_ENVIRONMENT_CONFLICT'
                    }
                    # 到此已證明舊組態持久化。啟動是獨立階段，失敗不得回報完整恢復。
                    $rollback = 'PreviousConfigurationRestoredActivationIncomplete'
                    try {
                        if ($previous.PoolStarted -and $pool.State.ToString() -ne 'Started') { $pool.Start() | Out-Null }
                        if ($previous.SiteStarted -and $site.State.ToString() -ne 'Started') { $site.Start() | Out-Null }
                        if (($site.State.ToString() -eq 'Started') -ne [bool]$previous.SiteStarted -or
                            ($pool.State.ToString() -eq 'Started') -ne [bool]$previous.PoolStarted) {
                            throw 'GS2_ROLLBACK_PREVIOUS_RUNTIME_STATE_CONFLICT'
                        }
                        $rollback = 'PreviousStateRestored'
                    }
                    catch {
                        # 啟動只恢復到一半時盡量回到停止且已排空的安全狀態；若無法證實，外層記Failed。
                        if ($null -ne $site -and $site.State.ToString() -ne 'Stopped') { $site.Stop() | Out-Null }
                        if ($null -ne $pool -and $pool.State.ToString() -ne 'Stopped') { $pool.Stop() | Out-Null }
                        Wait-Gs2PoolStopped
                        if (($null -ne $site -and $site.State.ToString() -ne 'Stopped') -or
                            ($null -ne $pool -and $pool.State.ToString() -ne 'Stopped')) {
                            throw 'GS2_ROLLBACK_ACTIVATION_DRAIN_FAILED'
                        }
                        $rollback = 'PreviousConfigurationRestoredActivationIncomplete'
                    }
                }
                else {
                    if ($null -eq $site -or $null -eq $pool -or
                        $site.Applications['/'].VirtualDirectories['/'].PhysicalPath -ine $release -or
                        $site.State.ToString() -ne 'Stopped' -or $pool.State.ToString() -ne 'Stopped' -or
                        [bool]$site.ServerAutoStart -or [bool]$pool.AutoStart -ne [bool]$previous.PoolAutoStart) {
                        throw 'GS2_ROLLBACK_NEW_SITE_STOPPED_STATE_CONFLICT'
                    }
                    if ($null -ne $previousCrmEnvironment -and
                        -not (Test-Gs2CrmEnvironmentSnapshot ($pool.GetCollection('environmentVariables')) $previousCrmEnvironment)) {
                        throw 'GS2_ROLLBACK_NEW_SITE_POOL_ENVIRONMENT_CONFLICT'
                    }
                    $rollback = 'PreviousConfigurationRestoredActivationIncomplete'
                    try {
                        if ($previous.PoolExists -and $previous.PoolStarted -and $pool.State.ToString() -ne 'Started') { $pool.Start() | Out-Null }
                        $expectedPoolState = if ($previous.PoolStarted) { 'Started' } else { 'Stopped' }
                        if ($site.State.ToString() -cne 'Stopped' -or $pool.State.ToString() -cne $expectedPoolState) {
                            throw 'GS2_ROLLBACK_NEW_SITE_RUNTIME_STATE_CONFLICT'
                        }
                        $rollback = 'NewSiteKeptStopped'
                    }
                    catch {
                        if ($site.State.ToString() -ne 'Stopped') { $site.Stop() | Out-Null }
                        if ($pool.State.ToString() -ne 'Stopped') { $pool.Stop() | Out-Null }
                        Wait-Gs2PoolStopped
                        if ($site.State.ToString() -ne 'Stopped' -or $pool.State.ToString() -ne 'Stopped') {
                            throw 'GS2_ROLLBACK_NEW_SITE_ACTIVATION_DRAIN_FAILED'
                        }
                        $rollback = 'PreviousConfigurationRestoredActivationIncomplete'
                    }
                }
                }
            }
            catch { $rollback = 'Failed' }
        }
        $recordData.Phase = $remotePhase
        $recordData.Result = 'Failed'
        $recordData.Rollback = $rollback
        $recordData.ExceptionType = $failureType
        $recordData.FailureLine = $failureLine
        if ($hasRecord) {
            try { Write-Gs2Record $recordData }
            catch { [Console]::Error.WriteLine('GS2_RECORD_WRITE_FAILED') }
        }
        throw ('GS2_REMOTE_FAILED phase=' + $remotePhase + ' run=' + $Id + ' rollback=' + $rollback + ' type=' + $failureType)
    }
    finally {
        if ($null -ne $previousCrmEnvironment) { $previousCrmEnvironment.Clear(); $previousCrmEnvironment = $null }
        if ($null -ne $CrmCredential) { $CrmCredential.Password.Dispose(); $CrmCredential = $null }
        try { if ($null -ne $manager) { $manager.Dispose() } }
        finally { if ($null -ne $lock) { $lock.Dispose() } }
    }
}

try {
    $phase = 'CrmCredential'
    # 只讀固定User Secrets且限制檔案大小／連結；不接受命令列密碼、不複製到發佈目錄。
    $secretPath = Join-Path $env:APPDATA 'Microsoft\UserSecrets\SpeechMessage.Dynamics.Deployment.P0\secrets.json'
    Assert-Gs2NoReparse $secretPath
    if (-not (Test-Path -LiteralPath $secretPath -PathType Leaf) -or
        (Get-Item -LiteralPath $secretPath).Length -gt 1048576) { throw 'GS2_DEPLOYMENT_USER_SECRETS_REQUIRED' }
    $secretSettings = $null
    try {
        $secretSettings = [IO.File]::ReadAllText($secretPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
        $crmCredential = Get-Gs2CrmCredential $secretSettings $CrmServerUrl
    }
    finally { $secretSettings = $null }
    $phase = 'Publish'
    $repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $project = Join-Path $repo 'SpeechMessageProducts.GoodShepherd2.Web\SpeechMessageProducts.GoodShepherd2.Web.csproj'
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw 'GS2_PROJECT_NOT_FOUND' }
    $localRoot = Join-Path ([IO.Path]::GetTempPath()) 'SpeechMessage-GoodShepherd2Test'
    Assert-Gs2NoReparse $localRoot
    $localRun = Join-Path $localRoot $runId
    $publish = Join-Path $localRun 'publish'
    [IO.Directory]::CreateDirectory($localRoot) | Out-Null
    Assert-Gs2NoReparse (Join-Path $localRoot 'deployment.lock')
    $localLock = [IO.File]::Open((Join-Path $localRoot 'deployment.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        if (@(Get-ChildItem -LiteralPath $localRoot -Directory -Force).Count -ge 20) {
            throw 'GS2_LOCAL_RETENTION_LIMIT_ARCHIVE_EVIDENCE_FIRST'
        }
        [IO.Directory]::CreateDirectory($publish) | Out-Null
    }
    finally { $localLock.Dispose() }
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $dotnet
    $startInfo.WorkingDirectory = $repo
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('publish', $project, '--configuration', 'Release', '--framework', 'net10.0', '--output', $publish,
        '--self-contained', 'false', '--disable-build-servers', '-m:1', '-nr:false', '-p:UseAppHost=false', '-p:UseSharedCompilation=false')) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stdout = $null
    $stderr = $null
    try {
        if (-not $process.Start()) { throw 'GS2_PUBLISH_START_FAILED' }
        # 兩條串流直接排入Null，固定緩衝且不累積原生命令輸出，避免錯誤包含設定或秘密。
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null, 4096)
        $stderr = $process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null, 4096)
        if (-not $process.WaitForExit(180000)) { throw 'GS2_PUBLISH_TIMEOUT' }
        if ($process.ExitCode -ne 0) { throw 'GS2_PUBLISH_FAILED' }
    }
    finally {
        try {
            if ($process.Id -gt 0 -and -not $process.HasExited) { $process.Kill($true) }
            if ($process.Id -gt 0 -and -not $process.WaitForExit(10000)) { throw 'GS2_PUBLISH_DRAIN_FAILED' }
            foreach ($task in @($stdout, $stderr)) {
                if ($null -ne $task) {
                    if (-not $task.Wait(10000)) { throw 'GS2_PUBLISH_OUTPUT_DRAIN_FAILED' }
                    $task.GetAwaiter().GetResult() | Out-Null
                    $task.Dispose()
                }
            }
        }
        finally { $process.Dispose() }
    }
    $utf8 = [Text.UTF8Encoding]::new($false)
    # 已發佈設定來自本工作樹；只讀有界JSON並擷取公開LIFF白名單，不攜出其他區段。
    $publishedSettingsPath = Join-Path $publish 'appsettings.Development.json'
    if ((Get-Item -LiteralPath $publishedSettingsPath).Length -gt 65536) { throw 'GS2_PUBLIC_SETTINGS_TOO_LARGE' }
    $publishedSettings = [IO.File]::ReadAllText($publishedSettingsPath) | ConvertFrom-Json
    $liffProperty = $publishedSettings.PSObject.Properties['Liff']
    if ($null -eq $liffProperty) {
        $development = New-Gs2DevelopmentSettings $CrmServerUrl
    } else {
        $development = New-Gs2DevelopmentSettings $CrmServerUrl -LiffSettings $liffProperty.Value
    }
    $platformConnectionString = [string]$development.ConnectionStrings.Platform
    [IO.File]::WriteAllText((Join-Path $publish 'appsettings.Development.json'), (($development | ConvertTo-Json -Depth 4) + "`r`n"), $utf8)
    $webConfig = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\SpeechMessageProducts.GoodShepherd2.Web.dll" stdoutLogEnabled="false" hostingModel="inprocess">
        <environmentVariables>
          <environmentVariable name="DOTNET_ENVIRONMENT" value="Development" />
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Development" />
          <environmentVariable name="ASPNETCORE_DETAILEDERRORS" value="false" />
          <environmentVariable name="CrmConnection__ServerUrl" value="__GS2_CRM_URL__" />
          <environmentVariable name="GoodShepherd2__AllowedDevelopmentOrganizations__0" value="goodshepherd20.speechmessage.com.tw" />
          <environmentVariable name="ConnectionStrings__Platform" value="__GS2_PLATFORM_CONNECTION_STRING__" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
'@
    $webConfig = $webConfig.Replace('__GS2_CRM_URL__', [Security.SecurityElement]::Escape($CrmServerUrl)).Replace(
        '__GS2_PLATFORM_CONNECTION_STRING__', [Security.SecurityElement]::Escape($platformConnectionString))
    [IO.File]::WriteAllText((Join-Path $publish 'web.config'), ($webConfig.Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n"), $utf8)
    $phase = 'Package'
    # 先逐層列舉並限制個數／容量，不遞迴追蹤連結；壓縮只消費本次publish私有目錄。
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($publish)
    $count = 0
    $bytes = [long]0
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            $count++
            if ($count -gt 4096 -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'GS2_LOCAL_PACKAGE_LIMIT_OR_LINK' }
            $relative = $item.FullName.Substring($publish.Length + 1).Replace('\', '/')
            Assert-Gs2ArchiveName $relative
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            else {
                $bytes += $item.Length
                if ($bytes -gt 1073741824) { throw 'GS2_LOCAL_PACKAGE_SIZE_LIMIT' }
            }
        }
    }
    $archivePath = Join-Path $localRun 'package.zip'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($publish, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $false)
    if ((Get-Item -LiteralPath $archivePath).Length -gt 536870912) { throw 'GS2_COMPRESSED_PACKAGE_SIZE_LIMIT' }
    $packageHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    $phase = 'Connect'
    $options = New-PSSessionOption -OpenTimeout 30000 -OperationTimeout 180000 -CancelTimeout 10000 -IdleTimeout 180000 -MaximumRedirection 0 -MaxConnectionRetryCount 0
    $connection = @{ ComputerName=$ComputerName; Authentication='Negotiate'; SessionOption=$options; ErrorAction='Stop' }
    if ($null -ne $Credential) { $connection.Credential = $Credential }
    $session = New-PSSession @connection
    $phase = 'Prepare'
    $prepared = Invoke-Command -Session $session -ScriptBlock $remoteDeployment -ArgumentList @('Prepare', $runId, $CertificateThumbprint, '', $validatorSources, $null) -ErrorAction Stop
    $phase = 'Transfer'
    Copy-Item -LiteralPath $archivePath -Destination $prepared.PackagePath -ToSession $session -ErrorAction Stop
    $phase = 'Commit'
    $result = Invoke-Command -Session $session -ScriptBlock $remoteDeployment -ArgumentList @('Commit', $runId, $CertificateThumbprint, $packageHash, $validatorSources, $crmCredential) -ErrorAction Stop
    $plan.Mode = 'Applied'
    $plan.RemoteValidation = 'LivenessPassed;ReadinessNotChecked'
    $plan | Add-Member -NotePropertyName Deployment -NotePropertyValue $result
    $plan
}
catch {
    # 原始錯誤可能包含建置來源或設定值；保持失敗與關聯編號，詳細安全階段由遠端紀錄追查。
    throw ('GS2_DEPLOYMENT_FAILED phase=' + $phase + ' run=' + $runId + ' type=' + $_.Exception.GetType().FullName)
}
finally {
    try { if ($null -ne $session) { Remove-PSSession -Session $session -ErrorAction Stop } }
    finally {
        if ($null -ne $crmCredential) { $crmCredential.Password.Dispose(); $crmCredential = $null }
    }
}
