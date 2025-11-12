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
    
    Write-Host "處理檔案: $FilePath" -ForegroundColor Cyan
    
    $content = Get-Content -Path $FilePath -Raw -Encoding UTF8
    $originalContent = $content
    $changesMade = $false
    
    # 更新 href 路由
    foreach ($oldRoute in $routeMappings.Keys) {
        $newRoute = $routeMappings[$oldRoute]
        if ($content -match [regex]::Escape($oldRoute)) {
            $content = $content -replace [regex]::Escape($oldRoute), $newRoute
            Write-Host "  ? 更新 href: $oldRoute → $newRoute" -ForegroundColor Green
            $changesMade = $true
        }
    }
    
    # 更新 Url.Action 呼叫
    foreach ($oldPattern in $urlActionMappings.Keys) {
        $newPattern = $urlActionMappings[$oldPattern]
        if ($content -match $oldPattern) {
            $content = $content -replace $oldPattern, $newPattern
            Write-Host "  ? 更新 Url.Action: $oldPattern → $newPattern" -ForegroundColor Green
            $changesMade = $true
        }
    }
    
    # 如果有變更且不是測試模式，則寫入檔案
    if ($changesMade -and -not $DryRun) {
        Set-Content -Path $FilePath -Value $content -Encoding UTF8 -NoNewline
        Write-Host "  ? 檔案已更新" -ForegroundColor Yellow
    } elseif ($changesMade) {
        Write-Host "  ? 測試模式：檔案未實際更新" -ForegroundColor Yellow
    } else {
        Write-Host "  - 無需更新" -ForegroundColor Gray
    }
    
    return $changesMade
}

# 主程式
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$viewsPath = Join-Path $scriptPath "Views"

Write-Host "========================================" -ForegroundColor Magenta
Write-Host "View 路由更新腳本" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# 詢問是否為測試模式
$response = Read-Host "是否為測試模式？(僅顯示變更但不實際修改) [Y/N]"
$dryRun = $response -eq "Y"

if ($dryRun) {
    Write-Host "? 測試模式：只顯示變更，不實際修改檔案" -ForegroundColor Yellow
} else {
    Write-Host "? 正式模式：將實際修改檔案" -ForegroundColor Red
    $confirm = Read-Host "確定要繼續嗎？[Y/N]"
    if ($confirm -ne "Y") {
        Write-Host "操作已取消" -ForegroundColor Red
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
Write-Host "處理完成" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "處理檔案數: $filesProcessed" -ForegroundColor Cyan
Write-Host "變更檔案數: $filesChanged" -ForegroundColor Green
Write-Host ""

if ($dryRun) {
    Write-Host "這是測試模式的結果，實際檔案未被修改" -ForegroundColor Yellow
    Write-Host "若要實際更新，請重新執行腳本並選擇 'N'" -ForegroundColor Yellow
}
