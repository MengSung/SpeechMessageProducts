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
    [string]$SolutionRoot = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport"
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
        Write-ColorOutput "? 建立目錄: $Path" "Green"
    } else {
        Write-ColorOutput "??  目錄已存在: $Path" "Yellow"
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
        Write-ColorOutput "? 建立檔案: $Path" "Green"
    } else {
        Write-ColorOutput "??  檔案已存在，跳過: $Path" "Yellow"
    }
}

Write-ColorOutput "`n========================================" "Cyan"
Write-ColorOutput "  Controller 分割遷移 - 階段一" "Cyan"
Write-ColorOutput "========================================`n" "Cyan"

# 1. 建立目錄結構
Write-ColorOutput "步驟 1: 建立目錄結構..." "Cyan"

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
Write-ColorOutput "`n步驟 2: 建立 Model 定義..." "Cyan"

# LoginRequest.cs
$loginRequestContent = @"
using System.ComponentModel.DataAnnotations;

namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// 登入請求模型
    /// </summary>
    public class LoginRequest
    {
        /// <summary>
        /// 帳號
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// 密碼
        /// </summary>
        public string Password { get; set; }
    }

    /// <summary>
    /// LINE 登入請求模型
    /// </summary>
    public class LineLoginRequest
    {
        /// <summary>
        /// LINE User ID
        /// </summary>
        [Required]
        public string LineUserId { get; set; }

        /// <summary>
        /// 顯示名稱
        /// </summary>
        public string DisplayName { get; set; }
    }
}
"@

New-FileWithContent -Path "$SolutionRoot\Models\Authentication\LoginRequest.cs" -Content $loginRequestContent

# LoginResponse.cs
$loginResponseContent = @"
namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// 登入回應模型
    /// </summary>
    public class LoginResponse
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 顯示檢視類型
        /// </summary>
        public string DisplayViewType { get; set; }

        /// <summary>
        /// 活躍清單 ID
        /// </summary>
        public string ActiveListId { get; set; }

        /// <summary>
        /// 訊息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 全名
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// 帳號
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// 密碼
        /// </summary>
        public string Password { get; set; }
    }
}
"@

New-FileWithContent -Path "$SolutionRoot\Models\Authentication\LoginResponse.cs" -Content $loginResponseContent

# AuthResult.cs
$authResultContent = @"
using Microsoft.Xrm.Sdk;

namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// 認證結果
    /// </summary>
    public class AuthResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 登入的連絡人實體
        /// </summary>
        public Entity LoginContact { get; set; }

        /// <summary>
        /// 全名
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// 登入類型
        /// </summary>
        public LoginType LoginType { get; set; }

        /// <summary>
        /// 錯誤訊息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 建立成功結果
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
        /// 建立失敗結果
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
    /// 登入類型列舉
    /// </summary>
    public enum LoginType
    {
        /// <summary>
        /// 帳號密碼登入
        /// </summary>
        AccountPassword,

        /// <summary>
        /// LINE ID 登入
        /// </summary>
        LineId,

        /// <summary>
        /// QR Code 登入
        /// </summary>
        QrCode
    }
}
"@

New-FileWithContent -Path "$SolutionRoot\Models\Authentication\AuthResult.cs" -Content $authResultContent

# SessionData.cs
$sessionDataContent = @"
using Microsoft.Xrm.Sdk;

namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// Session 資料模型
    /// </summary>
    public class SessionData
    {
        /// <summary>
        /// 登入的連絡人實體
        /// </summary>
        public Entity LoginContact { get; set; }

        /// <summary>
        /// 帳號
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// 密碼
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 全名
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// 登入類型
        /// </summary>
        public string LoginType { get; set; }

        /// <summary>
        /// 顯示檢視類型
        /// </summary>
        public string DisplayViewType { get; set; }

        /// <summary>
        /// 活躍清單 ID
        /// </summary>
        public string ActiveListId { get; set; }

        /// <summary>
        /// 使用者類型
        /// </summary>
        public string UserType { get; set; }

        /// <summary>
        /// 是否有幸福小組
        /// </summary>
        public bool HasHappyGroup { get; set; }

        /// <summary>
        /// 是否有繳費資料
        /// </summary>
        public bool HasFeeData { get; set; }
    }
}
"@

New-FileWithContent -Path "$SolutionRoot\Models\Authentication\SessionData.cs" -Content $sessionDataContent

# 3. 建立 Service 介面
Write-ColorOutput "`n步驟 3: 建立 Service 介面..." "Cyan"

# IAuthenticationService.cs
$authServiceInterfaceContent = @"
using ChurchReport.Models.Authentication;
using System.Threading.Tasks;

namespace ChurchReport.Services.Authentication
{
    /// <summary>
    /// 認證服務介面
    /// </summary>
    public interface IAuthenticationService
    {
        /// <summary>
        /// 驗證帳號密碼
        /// </summary>
        Task<AuthResult> ValidateCredentialsAsync(string account, string password);

        /// <summary>
        /// 驗證 LINE User ID
        /// </summary>
        Task<AuthResult> ValidateLineIdAsync(string lineUserId);
    }
}
"@

New-FileWithContent -Path "$SolutionRoot\Services\Authentication\IAuthenticationService.cs" -Content $authServiceInterfaceContent

# ISessionInitializationService.cs
$sessionServiceInterfaceContent = @"
using ChurchReport.Models.Authentication;
using Microsoft.Xrm.Sdk;
using System.Threading.Tasks;

namespace ChurchReport.Services.Authentication
{
    /// <summary>
    /// Session 初始化服務介面
    /// </summary>
    public interface ISessionInitializationService
    {
        /// <summary>
        /// 初始化使用者 Session
        /// </summary>
        Task<SessionData> InitializeSessionAsync(
            Entity loginContact,
            LoginType loginType,
            string account,
            string password);

        /// <summary>
        /// 清除 Session
        /// </summary>
        void ClearSession();
    }
}
"@

New-FileWithContent -Path "$SolutionRoot\Services\Authentication\ISessionInitializationService.cs" -Content $sessionServiceInterfaceContent

# INavigationService.cs
$navigationServiceInterfaceContent = @"
using ChurchReport.Models.Authentication;

namespace ChurchReport.Services.Navigation
{
    /// <summary>
    /// 導覽服務介面
    /// </summary>
    public interface INavigationService
    {
        /// <summary>
        /// 決定登入後的重導向資訊
        /// </summary>
        RedirectInfo DetermineRedirect(SessionData sessionData);
    }

    /// <summary>
    /// 重導向資訊
    /// </summary>
    public class RedirectInfo
    {
        public string ViewType { get; set; }
        public string ActiveListId { get; set; }
    }
}
"@

New-FileWithContent -Path "$SolutionRoot\Services\Navigation\INavigationService.cs" -Content $navigationServiceInterfaceContent

# 4. 建立 README
Write-ColorOutput "`n步驟 4: 建立 README 文件..." "Cyan"

$readmeContent = @"
# Controller 分割遷移進度

## 階段一：基礎架構建立 ?

已完成：
- [x] 建立目錄結構
- [x] 建立 Model 定義
- [x] 建立 Service 介面
- [ ] 實作 Service 類別
- [ ] 建立新的 Controller
- [ ] 修改 Startup.cs 註冊服務

## 下一步

請參考以下文件繼續實作：
1. `Controller分割實作範例.md` - 查看完整實作範例
2. `Controller分割設計評估報告.md` - 查看整體設計方案

## 注意事項

1. 所有新建的檔案都在適當的命名空間下
2. 請使用 Visual Studio 將這些檔案加入專案
3. 實作 Service 類別時，請參考原始的 HomeController 邏輯
4. 記得在 Startup.cs 註冊新服務

## 執行命令

```powershell
# 進入專案目錄
cd ChurchReport

# 執行階段二遷移腳本（待建立）
.\Scripts\Migrate-ControllerSplit-Phase2.ps1
```

建立時間: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
"@

New-FileWithContent -Path "$SolutionRoot\文件\Controller分割遷移進度.md" -Content $readmeContent

# 完成
Write-ColorOutput "`n========================================" "Cyan"
Write-ColorOutput "  階段一完成！" "Green"
Write-ColorOutput "========================================`n" "Cyan"

Write-ColorOutput "已建立以下內容：" "White"
Write-ColorOutput "  ?? 目錄結構（Controllers, Services, Models）" "White"
Write-ColorOutput "  ?? Model 定義檔案（4 個）" "White"
Write-ColorOutput "  ?? Service 介面檔案（3 個）" "White"
Write-ColorOutput "  ?? 進度追蹤文件" "White"

Write-ColorOutput "`n下一步：" "Yellow"
Write-ColorOutput "  1. 使用 Visual Studio 開啟方案" "White"
Write-ColorOutput "  2. 將新建的檔案加入專案" "White"
Write-ColorOutput "  3. 參考 'Controller分割實作範例.md' 實作 Service 類別" "White"
Write-ColorOutput "  4. 建立 AuthenticationController" "White"
Write-ColorOutput "  5. 在 Startup.cs 註冊服務" "White"

Write-ColorOutput "`n詳細說明請參考：" "Cyan"
Write-ColorOutput "  ?? 文件\Controller分割設計評估報告.md" "White"
Write-ColorOutput "  ?? 文件\Controller分割實作範例.md" "White"
Write-ColorOutput "  ?? 文件\Controller分割遷移進度.md" "White"

Write-ColorOutput "`n提示：記得先建立 Git 分支！" "Yellow"
Write-ColorOutput "  git checkout -b feature/controller-split-authentication`n" "Gray"
