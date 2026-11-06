<#
檔案整體說明

目的與定位：依固定路由對照表把舊Home畫面連結改為拆分後的控制器路徑；此為來源遷移工具，不能拿來推論現有端點的授權或自動修正任意字串。

主要功能與流程：
操作入口包含Update-ViewFile；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
# View 路由更新腳本
# 用於更新所有 View 檔案中的控制器路由

# 定義路由對應表
$routeMappings = @{
    # 小組管理
    "/Home/MultiGroupView" = "/SmallGroup/MultiGroupView"
    "/Home/IntegrateView" = "/SmallGroup/IntegrateView"
    "/Home/SmallGroupReportView" = "/SmallGroup/SmallGroupReportView"
    
    # 新人管理
    "/Home/NewPersonFollowUpView" = "/NewPerson/FollowUpView"
    "/Home/NewPerson" = "/NewPerson/NewPerson"
    
    # 個人資訊
    "/Home/PersonalReport" = "/Personal/Report"
    "/Home/PersonalInfomationView" = "/Personal/InfomationView"
    "/Home/MaintainPersonInfomationView" = "/Personal/MaintainInfomationView"
    
    # 行事曆
    "/Home/Scheduler" = "/Scheduler/Scheduler"
    "/Home/SchedulerView" = "/Scheduler/SchedulerView"
    
    # 奉獻管理
    "/Home/QPayView" = "/Dedication/QPayView"
    "/Home/DedicationFeeView" = "/Dedication/DedicationFeeView"
    "/Home/DedicationFeeViewWeb" = "/Dedication/DedicationFeeViewWeb"
    "/Home/KeyInDedicationFeeView" = "/Dedication/KeyInDedicationFeeView"
    "/Home/KeyInDedicationFeeViewWeb" = "/Dedication/KeyInDedicationFeeViewWeb"
    "/Home/DediationLineLoginView" = "/Dedication/DediationLineLoginView"
    
    # 奉獻稽核
    "/Home/DedicationFeeAuditViewLine" = "/DedicationAudit/AuditViewLine"
    "/Home/DedicationFeeAuditViewWeb" = "/DedicationAudit/AuditViewWeb"
    
    # QR Code
    "/Home/QrCodeView" = "/QrCode/CourseView"
    "/Home/PollQrCodeView" = "/QrCode/PollView"
    "/Home/SmallGroupQrCodeView" = "/QrCode/SmallGroupView"
    "/Home/SundayQrCodeView" = "/QrCode/SundayView"
    "/Home/PersonalQrCodeView" = "/QrCode/PersonalView"
    
    # 名單管理
    "/Home/ChurchRoot" = "/ListManagement/ChurchRoot"
}

# Url.Action 對應表
$urlActionMappings = @{
    # 小組管理
    'Url.Action\("MultiGroupView", "Home"' = 'Url.Action("MultiGroupView", "SmallGroup"'
    'Url.Action\("IntegrateView", "Home"' = 'Url.Action("IntegrateView", "SmallGroup"'
    'Url.Action\("SmallGroupReportView", "Home"' = 'Url.Action("SmallGroupReportView", "SmallGroup"'
    
    # 新人管理
    'Url.Action\("NewPersonFollowUpView", "Home"' = 'Url.Action("FollowUpView", "NewPerson"'
    'Url.Action\("NewPerson", "Home"' = 'Url.Action("NewPerson", "NewPerson"'
    
    # 個人資訊
    'Url.Action\("PersonalReport", "Home"' = 'Url.Action("Report", "Personal"'
    'Url.Action\("PersonalInfomationView", "Home"' = 'Url.Action("InfomationView", "Personal"'
    'Url.Action\("MaintainPersonInfomationView", "Home"' = 'Url.Action("MaintainInfomationView", "Personal"'
    
    # 行事曆
    'Url.Action\("Scheduler", "Home"' = 'Url.Action("Scheduler", "Scheduler"'
    'Url.Action\("SchedulerView", "Home"' = 'Url.Action("SchedulerView", "Scheduler"'
    
    # 奉獻管理
    'Url.Action\("QPayView", "Home"' = 'Url.Action("QPayView", "Dedication"'
    'Url.Action\("DedicationFeeView", "Home"' = 'Url.Action("DedicationFeeView", "Dedication"'
    'Url.Action\("DedicationFeeViewWeb", "Home"' = 'Url.Action("DedicationFeeViewWeb", "Dedication"'
    'Url.Action\("KeyInDedicationFeeView", "Home"' = 'Url.Action("KeyInDedicationFeeView", "Dedication"'
    'Url.Action\("DediationLineLoginView", "Home"' = 'Url.Action("DediationLineLoginView", "Dedication"'
    
    # 奉獻稽核
    'Url.Action\("DedicationFeeAuditViewLine", "Home"' = 'Url.Action("AuditViewLine", "DedicationAudit"'
    'Url.Action\("DedicationFeeAuditViewWeb", "Home"' = 'Url.Action("AuditViewWeb", "DedicationAudit"'
    
    # QR Code
    'Url.Action\("QrCodeView", "Home"' = 'Url.Action("CourseView", "QrCode"'
    'Url.Action\("PollQrCodeView", "Home"' = 'Url.Action("PollView", "QrCode"'
    'Url.Action\("SmallGroupQrCodeView", "Home"' = 'Url.Action("SmallGroupView", "QrCode"'
    'Url.Action\("SundayQrCodeView", "Home"' = 'Url.Action("SundayView", "QrCode"'
    'Url.Action\("PersonalQrCodeView", "Home"' = 'Url.Action("PersonalView", "QrCode"'
    
    # 名單管理
    'Url.Action\("ChurchRoot", "Home"' = 'Url.Action("ChurchRoot", "ListManagement"'
}

# 函數：更新單個檔案
function Update-ViewFile {
    param (
        [string]$FilePath,
        [switch]$DryRun
    )
    
    Write-Host "$([regex]::Unescape('\u8655\u7406\u6a94\u6848')): $FilePath" -ForegroundColor Cyan
    
    $content = Get-Content -Path $FilePath -Raw -Encoding UTF8
    $originalContent = $content
    $changesMade = $false
    
    # 更新 href 路由
    foreach ($oldRoute in $routeMappings.Keys) {
        $newRoute = $routeMappings[$oldRoute]
        if ($content -match [regex]::Escape($oldRoute)) {
            $content = $content -replace [regex]::Escape($oldRoute), $newRoute
            Write-Host "  ? $([regex]::Unescape('\u66f4\u65b0')) href: $oldRoute $([regex]::Unescape('\u2192')) $newRoute" -ForegroundColor Green
            $changesMade = $true
        }
    }
    
    # 更新 Url.Action 呼叫
    foreach ($oldPattern in $urlActionMappings.Keys) {
        $newPattern = $urlActionMappings[$oldPattern]
        if ($content -match $oldPattern) {
            $content = $content -replace $oldPattern, $newPattern
            Write-Host "  ? $([regex]::Unescape('\u66f4\u65b0')) Url.Action: $oldPattern $([regex]::Unescape('\u2192')) $newPattern" -ForegroundColor Green
            $changesMade = $true
        }
    }
    
    # 如果有變更且不是測試模式，則寫入檔案
    if ($changesMade -and -not $DryRun) {
        Set-Content -Path $FilePath -Value $content -Encoding UTF8 -NoNewline
        Write-Host ([regex]::Unescape('  ? \u6a94\u6848\u5df2\u66f4\u65b0')) -ForegroundColor Yellow
    } elseif ($changesMade) {
        Write-Host ([regex]::Unescape('  ? \u6e2c\u8a66\u6a21\u5f0f\uff1a\u6a94\u6848\u672a\u5be6\u969b\u66f4\u65b0')) -ForegroundColor Yellow
    } else {
        Write-Host ([regex]::Unescape('  - \u7121\u9700\u66f4\u65b0')) -ForegroundColor Gray
    }
    
    return $changesMade
}

# 主程式
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$viewsPath = Join-Path $scriptPath "Views"

Write-Host "========================================" -ForegroundColor Magenta
Write-Host ([regex]::Unescape('View \u8def\u7531\u66f4\u65b0\u8173\u672c')) -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# 詢問是否為測試模式
$response = Read-Host ([regex]::Unescape('\u662f\u5426\u70ba\u6e2c\u8a66\u6a21\u5f0f\uff1f(\u50c5\u986f\u793a\u8b8a\u66f4\u4f46\u4e0d\u5be6\u969b\u4fee\u6539) [Y/N]'))
$dryRun = $response -eq "Y"

if ($dryRun) {
    Write-Host ([regex]::Unescape('? \u6e2c\u8a66\u6a21\u5f0f\uff1a\u53ea\u986f\u793a\u8b8a\u66f4\uff0c\u4e0d\u5be6\u969b\u4fee\u6539\u6a94\u6848')) -ForegroundColor Yellow
} else {
    Write-Host ([regex]::Unescape('? \u6b63\u5f0f\u6a21\u5f0f\uff1a\u5c07\u5be6\u969b\u4fee\u6539\u6a94\u6848')) -ForegroundColor Red
    $confirm = Read-Host ([regex]::Unescape('\u78ba\u5b9a\u8981\u7e7c\u7e8c\u55ce\uff1f[Y/N]'))
    if ($confirm -ne "Y") {
        Write-Host ([regex]::Unescape('\u64cd\u4f5c\u5df2\u53d6\u6d88')) -ForegroundColor Red
        exit
    }
}

Write-Host ""

# 處理所有 .cshtml 檔案
$filesProcessed = 0
$filesChanged = 0

Get-ChildItem -Path $viewsPath -Filter "*.cshtml" -Recurse | ForEach-Object {
    $filesProcessed++
    if (Update-ViewFile -FilePath $_.FullName -DryRun:$dryRun) {
        $filesChanged++
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ([regex]::Unescape('\u8655\u7406\u5b8c\u6210')) -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "$([regex]::Unescape('\u8655\u7406\u6a94\u6848\u6578')): $filesProcessed" -ForegroundColor Cyan
Write-Host "$([regex]::Unescape('\u8b8a\u66f4\u6a94\u6848\u6578')): $filesChanged" -ForegroundColor Green
Write-Host ""

if ($dryRun) {
    Write-Host ([regex]::Unescape('\u9019\u662f\u6e2c\u8a66\u6a21\u5f0f\u7684\u7d50\u679c\uff0c\u5be6\u969b\u6a94\u6848\u672a\u88ab\u4fee\u6539')) -ForegroundColor Yellow
    Write-Host ([regex]::Unescape('\u82e5\u8981\u5be6\u969b\u66f4\u65b0\uff0c\u8acb\u91cd\u65b0\u57f7\u884c\u8173\u672c\u4e26\u9078\u64c7 ''N''')) -ForegroundColor Yellow
}
