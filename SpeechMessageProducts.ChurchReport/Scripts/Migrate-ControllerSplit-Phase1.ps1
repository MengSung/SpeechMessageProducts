<#
檔案整體說明

目的與定位：為舊控制器拆分建立第一階段基礎檔案與目錄，保留舊產品結構遷移的操作入口；執行會修改來源，必須先核對輸入路徑及現有修改。

主要功能與流程：
操作入口包含Write-ColorOutput、New-DirectoryIfNotExists、New-FileWithContent；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
<#
.SYNOPSIS
    Controller 分割遷移腳本 - 階段一：建立基礎架構

.DESCRIPTION
    這個腳本會自動建立 Controller 分割所需的目錄結構和基礎檔案
    包含：
    - 建立新的目錄結構
    - 建立服務介面和實作類別
    - 建立 Model 定義
    - 建立新的 Controller

.NOTES
    執行前請確保：
    1. 已備份當前程式碼
    2. 已建立 Git 分支
    3. 已關閉 Visual Studio

.EXAMPLE
    .\Migrate-ControllerSplit-Phase1.ps1
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [string]$SolutionRoot = ([regex]::Unescape('D:\\\u7db2\u9801APP\u96f2\u7aef\u7dda\u4e0a\u7248\u672c\\DevExpressDevExtreme-21.2.7\u7248\u672c\\\u97f3\u8a0a\u7522\u54c1\u7248\u672c\\ChurchReport\\ChurchReport'))
)

# 設定錯誤處理
$ErrorActionPreference = "Stop"

# 顏色輸出函數
function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    Write-Host $Message -ForegroundColor $Color
}

# 建立目錄函數
function New-DirectoryIfNotExists {
    param([string]$Path)
    
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
        Write-ColorOutput "? $([regex]::Unescape('\u5efa\u7acb\u76ee\u9304')): $Path" "Green"
    } else {
        Write-ColorOutput "??  $([regex]::Unescape('\u76ee\u9304\u5df2\u5b58\u5728')): $Path" "Yellow"
    }
}

# 建立檔案函數
function New-FileWithContent {
    param(
        [string]$Path,
        [string]$Content
    )
    
    if (-not (Test-Path $Path)) {
        $Content | Out-File -FilePath $Path -Encoding UTF8
        Write-ColorOutput "? $([regex]::Unescape('\u5efa\u7acb\u6a94\u6848')): $Path" "Green"
    } else {
        Write-ColorOutput "??  $([regex]::Unescape('\u6a94\u6848\u5df2\u5b58\u5728\uff0c\u8df3\u904e')): $Path" "Yellow"
    }
}

Write-ColorOutput "`n========================================" "Cyan"
Write-ColorOutput ([regex]::Unescape('  Controller \u5206\u5272\u9077\u79fb - \u968e\u6bb5\u4e00')) "Cyan"
Write-ColorOutput "========================================`n" "Cyan"

# 1. 建立目錄結構
Write-ColorOutput ([regex]::Unescape('\u6b65\u9a5f 1: \u5efa\u7acb\u76ee\u9304\u7d50\u69cb...')) "Cyan"

$directories = @(
    "$SolutionRoot\Controllers\Authentication",
    "$SolutionRoot\Controllers\UserManagement",
    "$SolutionRoot\Services\Authentication",
    "$SolutionRoot\Services\Navigation",
    "$SolutionRoot\Services\PhoneManagement",
    "$SolutionRoot\Models\Authentication",
    "$SolutionRoot\Models\PhoneManagement",
    "$SolutionRoot\Tests\Controllers",
    "$SolutionRoot\Tests\Services"
)

foreach ($dir in $directories) {
    New-DirectoryIfNotExists -Path $dir
}

# 2. 建立 Model 檔案
Write-ColorOutput ([regex]::Unescape('\n\u6b65\u9a5f 2: \u5efa\u7acb Model \u5b9a\u7fa9...')) "Cyan"

# LoginRequest.cs
$loginRequestContent = ([regex]::Unescape('using System.ComponentModel.DataAnnotations;

namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// \u767b\u5165\u8acb\u6c42\u6a21\u578b
    /// </summary>
    public class LoginRequest
    {
        /// <summary>
        /// \u5e33\u865f
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// \u5bc6\u78bc
        /// </summary>
        public string Password { get; set; }
    }

    /// <summary>
    /// LINE \u767b\u5165\u8acb\u6c42\u6a21\u578b
    /// </summary>
    public class LineLoginRequest
    {
        /// <summary>
        /// LINE User ID
        /// </summary>
        [Required]
        public string LineUserId { get; set; }

        /// <summary>
        /// \u986f\u793a\u540d\u7a31
        /// </summary>
        public string DisplayName { get; set; }
    }
}'))

New-FileWithContent -Path "$SolutionRoot\Models\Authentication\LoginRequest.cs" -Content $loginRequestContent

# LoginResponse.cs
$loginResponseContent = ([regex]::Unescape('namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// \u767b\u5165\u56de\u61c9\u6a21\u578b
    /// </summary>
    public class LoginResponse
    {
        /// <summary>
        /// \u662f\u5426\u6210\u529f
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// \u986f\u793a\u6aa2\u8996\u985e\u578b
        /// </summary>
        public string DisplayViewType { get; set; }

        /// <summary>
        /// \u6d3b\u8e8d\u6e05\u55ae ID
        /// </summary>
        public string ActiveListId { get; set; }

        /// <summary>
        /// \u8a0a\u606f
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// \u5168\u540d
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// \u5e33\u865f
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// \u5bc6\u78bc
        /// </summary>
        public string Password { get; set; }
    }
}'))

New-FileWithContent -Path "$SolutionRoot\Models\Authentication\LoginResponse.cs" -Content $loginResponseContent

# AuthResult.cs
$authResultContent = ([regex]::Unescape('using Microsoft.Xrm.Sdk;

namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// \u8a8d\u8b49\u7d50\u679c
    /// </summary>
    public class AuthResult
    {
        /// <summary>
        /// \u662f\u5426\u6210\u529f
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// \u767b\u5165\u7684\u9023\u7d61\u4eba\u5be6\u9ad4
        /// </summary>
        public Entity LoginContact { get; set; }

        /// <summary>
        /// \u5168\u540d
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// \u767b\u5165\u985e\u578b
        /// </summary>
        public LoginType LoginType { get; set; }

        /// <summary>
        /// \u932f\u8aa4\u8a0a\u606f
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// \u5efa\u7acb\u6210\u529f\u7d50\u679c
        /// </summary>
        public static AuthResult Success(Entity contact, string fullName, LoginType type)
        {
            return new AuthResult
            {
                Success = true,
                LoginContact = contact,
                FullName = fullName,
                LoginType = type
            };
        }

        /// <summary>
        /// \u5efa\u7acb\u5931\u6557\u7d50\u679c
        /// </summary>
        public static AuthResult Fail(string errorMessage)
        {
            return new AuthResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// \u767b\u5165\u985e\u578b\u5217\u8209
    /// </summary>
    public enum LoginType
    {
        /// <summary>
        /// \u5e33\u865f\u5bc6\u78bc\u767b\u5165
        /// </summary>
        AccountPassword,

        /// <summary>
        /// LINE ID \u767b\u5165
        /// </summary>
        LineId,

        /// <summary>
        /// QR Code \u767b\u5165
        /// </summary>
        QrCode
    }
}'))

New-FileWithContent -Path "$SolutionRoot\Models\Authentication\AuthResult.cs" -Content $authResultContent

# SessionData.cs
$sessionDataContent = ([regex]::Unescape('using Microsoft.Xrm.Sdk;

namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// Session \u8cc7\u6599\u6a21\u578b
    /// </summary>
    public class SessionData
    {
        /// <summary>
        /// \u767b\u5165\u7684\u9023\u7d61\u4eba\u5be6\u9ad4
        /// </summary>
        public Entity LoginContact { get; set; }

        /// <summary>
        /// \u5e33\u865f
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// \u5bc6\u78bc
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// \u5168\u540d
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// \u767b\u5165\u985e\u578b
        /// </summary>
        public string LoginType { get; set; }

        /// <summary>
        /// \u986f\u793a\u6aa2\u8996\u985e\u578b
        /// </summary>
        public string DisplayViewType { get; set; }

        /// <summary>
        /// \u6d3b\u8e8d\u6e05\u55ae ID
        /// </summary>
        public string ActiveListId { get; set; }

        /// <summary>
        /// \u4f7f\u7528\u8005\u985e\u578b
        /// </summary>
        public string UserType { get; set; }

        /// <summary>
        /// \u662f\u5426\u6709\u5e78\u798f\u5c0f\u7d44
        /// </summary>
        public bool HasHappyGroup { get; set; }

        /// <summary>
        /// \u662f\u5426\u6709\u7e73\u8cbb\u8cc7\u6599
        /// </summary>
        public bool HasFeeData { get; set; }
    }
}'))

New-FileWithContent -Path "$SolutionRoot\Models\Authentication\SessionData.cs" -Content $sessionDataContent

# 3. 建立 Service 介面
Write-ColorOutput ([regex]::Unescape('\n\u6b65\u9a5f 3: \u5efa\u7acb Service \u4ecb\u9762...')) "Cyan"

# IAuthenticationService.cs
$authServiceInterfaceContent = ([regex]::Unescape('using ChurchReport.Models.Authentication;
using System.Threading.Tasks;

namespace ChurchReport.Services.Authentication
{
    /// <summary>
    /// \u8a8d\u8b49\u670d\u52d9\u4ecb\u9762
    /// </summary>
    public interface IAuthenticationService
    {
        /// <summary>
        /// \u9a57\u8b49\u5e33\u865f\u5bc6\u78bc
        /// </summary>
        Task<AuthResult> ValidateCredentialsAsync(string account, string password);

        /// <summary>
        /// \u9a57\u8b49 LINE User ID
        /// </summary>
        Task<AuthResult> ValidateLineIdAsync(string lineUserId);
    }
}'))

New-FileWithContent -Path "$SolutionRoot\Services\Authentication\IAuthenticationService.cs" -Content $authServiceInterfaceContent

# ISessionInitializationService.cs
$sessionServiceInterfaceContent = ([regex]::Unescape('using ChurchReport.Models.Authentication;
using Microsoft.Xrm.Sdk;
using System.Threading.Tasks;

namespace ChurchReport.Services.Authentication
{
    /// <summary>
    /// Session \u521d\u59cb\u5316\u670d\u52d9\u4ecb\u9762
    /// </summary>
    public interface ISessionInitializationService
    {
        /// <summary>
        /// \u521d\u59cb\u5316\u4f7f\u7528\u8005 Session
        /// </summary>
        Task<SessionData> InitializeSessionAsync(
            Entity loginContact,
            LoginType loginType,
            string account,
            string password);

        /// <summary>
        /// \u6e05\u9664 Session
        /// </summary>
        void ClearSession();
    }
}'))

New-FileWithContent -Path "$SolutionRoot\Services\Authentication\ISessionInitializationService.cs" -Content $sessionServiceInterfaceContent

# INavigationService.cs
$navigationServiceInterfaceContent = ([regex]::Unescape('using ChurchReport.Models.Authentication;

namespace ChurchReport.Services.Navigation
{
    /// <summary>
    /// \u5c0e\u89bd\u670d\u52d9\u4ecb\u9762
    /// </summary>
    public interface INavigationService
    {
        /// <summary>
        /// \u6c7a\u5b9a\u767b\u5165\u5f8c\u7684\u91cd\u5c0e\u5411\u8cc7\u8a0a
        /// </summary>
        RedirectInfo DetermineRedirect(SessionData sessionData);
    }

    /// <summary>
    /// \u91cd\u5c0e\u5411\u8cc7\u8a0a
    /// </summary>
    public class RedirectInfo
    {
        public string ViewType { get; set; }
        public string ActiveListId { get; set; }
    }
}'))

New-FileWithContent -Path "$SolutionRoot\Services\Navigation\INavigationService.cs" -Content $navigationServiceInterfaceContent

# 4. 建立 README
Write-ColorOutput ([regex]::Unescape('\n\u6b65\u9a5f 4: \u5efa\u7acb README \u6587\u4ef6...')) "Cyan"

$readmeContent = @"
# Controller $([regex]::Unescape('\u5206\u5272\u9077\u79fb\u9032\u5ea6'))

## $([regex]::Unescape('\u968e\u6bb5\u4e00\uff1a\u57fa\u790e\u67b6\u69cb\u5efa\u7acb')) ?

$([regex]::Unescape('\u5df2\u5b8c\u6210\uff1a'))
- [x] $([regex]::Unescape('\u5efa\u7acb\u76ee\u9304\u7d50\u69cb'))
- [x] $([regex]::Unescape('\u5efa\u7acb')) Model $([regex]::Unescape('\u5b9a\u7fa9'))
- [x] $([regex]::Unescape('\u5efa\u7acb')) Service $([regex]::Unescape('\u4ecb\u9762'))
- [ ] $([regex]::Unescape('\u5be6\u4f5c')) Service $([regex]::Unescape('\u985e\u5225'))
- [ ] $([regex]::Unescape('\u5efa\u7acb\u65b0\u7684')) Controller
- [ ] $([regex]::Unescape('\u4fee\u6539')) Startup.cs $([regex]::Unescape('\u8a3b\u518a\u670d\u52d9'))

## $([regex]::Unescape('\u4e0b\u4e00\u6b65'))

$([regex]::Unescape('\u8acb\u53c3\u8003\u4ee5\u4e0b\u6587\u4ef6\u7e7c\u7e8c\u5be6\u4f5c\uff1a'))
1. `Controller$([regex]::Unescape('\u5206\u5272\u5be6\u4f5c\u7bc4\u4f8b')).md` - $([regex]::Unescape('\u67e5\u770b\u5b8c\u6574\u5be6\u4f5c\u7bc4\u4f8b'))
2. `Controller$([regex]::Unescape('\u5206\u5272\u8a2d\u8a08\u8a55\u4f30\u5831\u544a')).md` - $([regex]::Unescape('\u67e5\u770b\u6574\u9ad4\u8a2d\u8a08\u65b9\u6848'))

## $([regex]::Unescape('\u6ce8\u610f\u4e8b\u9805'))

1. $([regex]::Unescape('\u6240\u6709\u65b0\u5efa\u7684\u6a94\u6848\u90fd\u5728\u9069\u7576\u7684\u547d\u540d\u7a7a\u9593\u4e0b'))
2. $([regex]::Unescape('\u8acb\u4f7f\u7528')) Visual Studio $([regex]::Unescape('\u5c07\u9019\u4e9b\u6a94\u6848\u52a0\u5165\u5c08\u6848'))
3. $([regex]::Unescape('\u5be6\u4f5c')) Service $([regex]::Unescape('\u985e\u5225\u6642\uff0c\u8acb\u53c3\u8003\u539f\u59cb\u7684')) HomeController $([regex]::Unescape('\u908f\u8f2f'))
4. $([regex]::Unescape('\u8a18\u5f97\u5728')) Startup.cs $([regex]::Unescape('\u8a3b\u518a\u65b0\u670d\u52d9'))

## $([regex]::Unescape('\u57f7\u884c\u547d\u4ee4'))

```powershell
# $([regex]::Unescape('\u9032\u5165\u5c08\u6848\u76ee\u9304'))
cd ChurchReport

# $([regex]::Unescape('\u57f7\u884c\u968e\u6bb5\u4e8c\u9077\u79fb\u8173\u672c\uff08\u5f85\u5efa\u7acb\uff09'))
.\Scripts\Migrate-ControllerSplit-Phase2.ps1
```

$([regex]::Unescape('\u5efa\u7acb\u6642\u9593')): $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
"@

New-FileWithContent -Path "$SolutionRoot\$([regex]::Unescape('\u6587\u4ef6'))\Controller$([regex]::Unescape('\u5206\u5272\u9077\u79fb\u9032\u5ea6')).md" -Content $readmeContent

# 完成
Write-ColorOutput "`n========================================" "Cyan"
Write-ColorOutput ([regex]::Unescape('  \u968e\u6bb5\u4e00\u5b8c\u6210\uff01')) "Green"
Write-ColorOutput "========================================`n" "Cyan"

Write-ColorOutput ([regex]::Unescape('\u5df2\u5efa\u7acb\u4ee5\u4e0b\u5167\u5bb9\uff1a')) "White"
Write-ColorOutput ([regex]::Unescape('  ?? \u76ee\u9304\u7d50\u69cb\uff08Controllers, Services, Models\uff09')) "White"
Write-ColorOutput ([regex]::Unescape('  ?? Model \u5b9a\u7fa9\u6a94\u6848\uff084 \u500b\uff09')) "White"
Write-ColorOutput ([regex]::Unescape('  ?? Service \u4ecb\u9762\u6a94\u6848\uff083 \u500b\uff09')) "White"
Write-ColorOutput ([regex]::Unescape('  ?? \u9032\u5ea6\u8ffd\u8e64\u6587\u4ef6')) "White"

Write-ColorOutput ([regex]::Unescape('\n\u4e0b\u4e00\u6b65\uff1a')) "Yellow"
Write-ColorOutput ([regex]::Unescape('  1. \u4f7f\u7528 Visual Studio \u958b\u555f\u65b9\u6848')) "White"
Write-ColorOutput ([regex]::Unescape('  2. \u5c07\u65b0\u5efa\u7684\u6a94\u6848\u52a0\u5165\u5c08\u6848')) "White"
Write-ColorOutput ([regex]::Unescape('  3. \u53c3\u8003 ''Controller\u5206\u5272\u5be6\u4f5c\u7bc4\u4f8b.md'' \u5be6\u4f5c Service \u985e\u5225')) "White"
Write-ColorOutput ([regex]::Unescape('  4. \u5efa\u7acb AuthenticationController')) "White"
Write-ColorOutput ([regex]::Unescape('  5. \u5728 Startup.cs \u8a3b\u518a\u670d\u52d9')) "White"

Write-ColorOutput ([regex]::Unescape('\n\u8a73\u7d30\u8aaa\u660e\u8acb\u53c3\u8003\uff1a')) "Cyan"
Write-ColorOutput ([regex]::Unescape('  ?? \u6587\u4ef6\\Controller\u5206\u5272\u8a2d\u8a08\u8a55\u4f30\u5831\u544a.md')) "White"
Write-ColorOutput ([regex]::Unescape('  ?? \u6587\u4ef6\\Controller\u5206\u5272\u5be6\u4f5c\u7bc4\u4f8b.md')) "White"
Write-ColorOutput ([regex]::Unescape('  ?? \u6587\u4ef6\\Controller\u5206\u5272\u9077\u79fb\u9032\u5ea6.md')) "White"

Write-ColorOutput ([regex]::Unescape('\n\u63d0\u793a\uff1a\u8a18\u5f97\u5148\u5efa\u7acb Git \u5206\u652f\uff01')) "Yellow"
Write-ColorOutput "  git checkout -b feature/controller-split-authentication`n" "Gray"
