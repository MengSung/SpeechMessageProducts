# View 路由批次更新腳本 - 第二批 (高優先級檔案)
# 功能: 自動更新 View 檔案中的控制器路由

param(
    [string]$ViewsPath = ".\ChurchReport\Views",
    [switch]$WhatIf = $false,
    [switch]$Verbose = $false
)

# 定義路由對應表
$routeMappings = @{
    # 小組管理相關
    "SmallGroup" = @{
        OldController = "Home"
        NewController = "SmallGroup"
        Actions = @(
            "MultiGroupView",
            "IntegrateView",
            "LoadIntegrate",
            "InsertPresentRecord",
            "DeletePresentRecord",
            "UpdateSmallGroupPresentRecord",
            "SaveIntegrate",
            "UpdateHappyWeekIndex",
            "UpdateHappyWeekTopic"
        )
    }
    
    # 新人管理相關
    "NewPerson" = @{
        OldController = "Home"
        NewController = "NewPerson"
        Actions = @(
            "NewPersonFollowUpView",
            "LoadNewPersonFollowUp",
            "InsertNewPresentRecord",
            "UpdateNewPresentRecord",
            "DeleteNewPresentRecord",
            "SaveNewPersonFollowUp",
            "NewPerson",
            "SaveNewPerson",
            "AddNewPerson",
            "AssignSmallGroupGet"
        )
    }
    
    # 個人資訊相關
    "Personal" = @{
        OldController = "Home"
        NewController = "Personal"
        Actions = @(
            "PersonalReport",
            "PersonalInfomationView",
            "MaintainPersonInfomationView",
            "LoadPersonReport",
            "InsertPersonReport",
            "UpdatePersonReport",
            "DeletePersonReport",
            "SavePersonReport",
            "SavePersonalReportForm",
            "SavePersonalInfomation"
        )
    }
    
    # 行事曆相關
    "Appointment" = @{
        OldController = "Home"
        NewController = "Appointment"
        Actions = @(
            "Scheduler",
            "LoadAppointments",
            "PostAppointments",
            "PutAppointments",
            "DeleteAppointments",
            "NavigateAppointmentDate",
            "LoadAppointmentByLineId"
        )
    }
    
    # 奉獻管理相關
    "Dedication" = @{
        OldController = "Home"
        NewController = "Dedication"
        Actions = @(
            "QPayView",
            "DedicationFeeView",
            "DedicationFeeViewWeb",
            "KeyInDedicationFeeView",
            "KeyInDedicationFeeViewWeb",
            "DediationLineLoginView",
            "SaveQPayDedication",
            "LoadCreditCardList",
            "DeleteCreditCard",
            "LoadDedicationBookingList",
            "DeleteDedicationBooking",
            "UpdateDedicationFeeView",
            "SaveKeyInDedication",
            "LoadSameNameList",
            "DeleteSameNameContact",
            "CreateContact",
            "SetupUserLineId"
        )
    }
    
    # 奉獻稽核相關
    "DedicationAudit" = @{
        OldController = "Home"
        NewController = "DedicationAudit"
        Actions = @(
            "DedicationFeeAuditViewLine",
            "DedicationFeeAuditViewWeb",
            "AuditQueryDedication",
            "LoadDedicationFeeList",
            "ApproveDedication",
            "RejectDedication",
            "ExportDedicationReport",
            "GetDedicationSummary",
            "GetDedicationTrend"
        )
    }
    
    # QR Code 相關
    "QrCode" = @{
        OldController = "Home"
        NewController = "QrCode"
        Actions = @(
            "QrCodeView",
            "QrCodeGetLineId",
            "PollQrCodeView",
            "PollQrCodeGetLineId",
            "SavePoll",
            "SmallGroupQrCodeView",
            "SmallGroupQrCodeGetLineId",
            "SundayQrCodeView",
            "SundayQrCodeGetLineId",
            "PersonalQrCodeView",
            "PersonalQrCodeGetLineId"
        )
    }
    
    # 名單管理相關
    "ListManagement" = @{
        OldController = "Home"
        NewController = "ListManagement"
        Actions = @(
            "ChurchRoot",
            "LoadChurchRoot",
            "LoadListManagementList",
            "LoadListManagementSmallGroup",
            "LoadListManagementMember",
            "LoadLookupList",
            "PostRacerListManagementMember",
            "AddRaceLeader",
            "DeleteRaceLeader",
            "PostSmallGroupAction",
            "AddSmallGroup",
            "UpdateListManagementSmallGroup",
            "UpdateSmallGroup",
            "DeleteSmallGroup",
            "PostContactAction",
            "AddContact",
            "UpdateListManagementContactMember",
            "UpdateContactMember",
            "DeleteContact",
            "DeleteListManagement",
            "SaveListManagement",
            "SaveListManagementContactMember",
            "SaveListManagementSmallGroup"
        )
    }
}

# 高優先級檔案清單
$highPriorityFiles = @(
    # 小組管理
    "Home\MultiGroupView.cshtml",
    "Home\IntegrateView.cshtml",
    "Home\_GeneralGroupGrids.cshtml",
    "Home\_HappyGroupGrid.cshtml",
    "Home\_IndividualReportGrid.cshtml",
    
    # 新人管理
    "Home\NewPerson.cshtml",
    "Home\NewPersonFollowUpView.cshtml",
    
    # 個人資訊
    "Home\PersonalReport.cshtml",
    "Home\PersonalInfomationView.cshtml",
    "Home\MaintainPersonInfomationView.cshtml",
    
    # 奉獻管理
    "Home\QPayView.cshtml",
    "Home\DedicationFeeView.cshtml",
    "Home\DedicationFeeViewWeb.cshtml",
    
    # 奉獻稽核
    "Home\DedicationFeeAuditViewLine.cshtml",
    "Home\DedicationFeeAuditViewWeb.cshtml",
    
    # QR Code
    "Home\QrCodeView.cshtml",
    
    # 名單管理
    "Home\ChurchRoot.cshtml",
    
    # 其他
    "Home\Login.cshtml",
    "Home\LineIdLoginView.cshtml",
    "Home\DisplayErrorView.cshtml"
)

# 統計變數
$totalFiles = 0
$updatedFiles = 0
$skippedFiles = 0
$errorFiles = 0
$changeLog = @()

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "View 路由批次更新工具 - 第二批" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 處理每個檔案
foreach ($relativeFilePath in $highPriorityFiles) {
    $totalFiles++
    $filePath = Join-Path $ViewsPath $relativeFilePath
    
    if (-not (Test-Path $filePath)) {
        Write-Host "??  檔案不存在: $relativeFilePath" -ForegroundColor Yellow
        $skippedFiles++
        continue
    }
    
    Write-Host "?? 處理檔案: $relativeFilePath" -ForegroundColor White
    
    try {
        # 讀取檔案內容
        $content = Get-Content $filePath -Raw -Encoding UTF8
        $originalContent = $content
        $changes = @()
        
        # 根據檔案路徑判斷應該使用哪個控制器
        $targetController = $null
        foreach ($key in $routeMappings.Keys) {
            $mapping = $routeMappings[$key]
            foreach ($action in $mapping.Actions) {
                if ($content -match "/Home/$action") {
                    $targetController = $mapping.NewController
                    break
                }
            }
            if ($targetController) { break }
        }
        
        if (-not $targetController) {
            Write-Host "  ??  無需更新 (未找到相關路由)" -ForegroundColor Gray
            $skippedFiles++
            continue
        }
        
        # 執行替換
        $mapping = $routeMappings | Where-Object { $_.Values.NewController -eq $targetController } | Select-Object -First 1
        if ($mapping) {
            foreach ($entry in $routeMappings.GetEnumerator()) {
                if ($entry.Value.NewController -eq $targetController) {
                    $oldCtrl = $entry.Value.OldController
                    $newCtrl = $entry.Value.NewController
                    
                    foreach ($action in $entry.Value.Actions) {
                        # 替換 Controller 屬性中的路由
                        $pattern1 = "Controller\(`"$oldCtrl`"\)"
                        $replacement1 = "Controller(`"$newCtrl`")"
                        if ($content -match $pattern1) {
                            $content = $content -replace $pattern1, $replacement1
                            $changes += "  ? Controller(`"$oldCtrl`") → Controller(`"$newCtrl`")"
                        }
                        
                        # 替換 URL 路徑
                        $pattern2 = "/$oldCtrl/$action"
                        $replacement2 = "/$newCtrl/$action"
                        if ($content -match [regex]::Escape($pattern2)) {
                            $content = $content -replace [regex]::Escape($pattern2), $replacement2
                            $changes += "  ? /$oldCtrl/$action → /$newCtrl/$action"
                        }
                        
                        # 替換 Url.Action
                        $pattern3 = "Url\.Action\(`"$action`",\s*`"$oldCtrl`"\)"
                        $replacement3 = "Url.Action(`"$action`", `"$newCtrl`")"
                        if ($content -match $pattern3) {
                            $content = $content -replace $pattern3, $replacement3
                            $changes += "  ? Url.Action(`"$action`", `"$oldCtrl`") → Url.Action(`"$action`", `"$newCtrl`")"
                        }
                        
                        # 替換 @Url.Action
                        $pattern4 = "@Url\.Action\(`"$action`",\s*`"$oldCtrl`"\)"
                        $replacement4 = "@Url.Action(`"$action`", `"$newCtrl`")"
                        if ($content -match $pattern4) {
                            $content = $content -replace $pattern4, $replacement4
                            $changes += "  ? @Url.Action(`"$action`", `"$oldCtrl`") → @Url.Action(`"$action`", `"$newCtrl`")"
                        }
                        
                        # 替換 JavaScript 中的 URL
                        $pattern5 = "`"/$oldCtrl/$action`""
                        $replacement5 = "`"/$newCtrl/$action`""
                        if ($content -match [regex]::Escape($pattern5)) {
                            $content = $content -replace [regex]::Escape($pattern5), $replacement5
                            $changes += "  ? JS: `"/$oldCtrl/$action`" → `"/$newCtrl/$action`""
                        }
                        
                        # 替換 JavaScript 中的 URL (單引號)
                        $pattern6 = "'/$oldCtrl/$action'"
                        $replacement6 = "'/$newCtrl/$action'"
                        if ($content -match [regex]::Escape($pattern6)) {
                            $content = $content -replace [regex]::Escape($pattern6), $replacement6
                            $changes += "  ? JS: '/$oldCtrl/$action' → '/$newCtrl/$action'"
                        }
                    }
                }
            }
        }
        
        # 檢查是否有變更
        if ($content -ne $originalContent) {
            if ($changes.Count -gt 0) {
                Write-Host "  ?? 發現 $($changes.Count) 處變更:" -ForegroundColor Green
                foreach ($change in $changes) {
                    if ($Verbose) {
                        Write-Host $change -ForegroundColor Gray
                    }
                }
            }
            
            if (-not $WhatIf) {
                # 寫入檔案
                $content | Out-File $filePath -Encoding UTF8 -NoNewline
                Write-Host "  ? 已更新" -ForegroundColor Green
                $updatedFiles++
                
                # 記錄變更
                $changeLog += [PSCustomObject]@{
                    File = $relativeFilePath
                    Controller = $targetController
                    ChangeCount = $changes.Count
                    Status = "成功"
                }
            } else {
                Write-Host "  ?? 模擬模式: 將會更新 (使用 -WhatIf:$false 執行實際更新)" -ForegroundColor Yellow
                $updatedFiles++
                
                # 記錄變更
                $changeLog += [PSCustomObject]@{
                    File = $relativeFilePath
                    Controller = $targetController
                    ChangeCount = $changes.Count
                    Status = "模擬"
                }
            }
        } else {
            Write-Host "  ??  無需更新" -ForegroundColor Gray
            $skippedFiles++
        }
        
        Write-Host ""
        
    } catch {
        Write-Host "  ? 錯誤: $_" -ForegroundColor Red
        $errorFiles++
        
        # 記錄錯誤
        $changeLog += [PSCustomObject]@{
            File = $relativeFilePath
            Controller = "N/A"
            ChangeCount = 0
            Status = "錯誤: $_"
        }
        
        Write-Host ""
    }
}

# 顯示統計摘要
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "更新完成統計" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "總檔案數: $totalFiles" -ForegroundColor White
Write-Host "已更新:   $updatedFiles" -ForegroundColor Green
Write-Host "跳過:     $skippedFiles" -ForegroundColor Yellow
Write-Host "錯誤:     $errorFiles" -ForegroundColor Red
Write-Host ""

# 匯出變更記錄
if ($changeLog.Count -gt 0) {
    $logPath = ".\ChurchReport\文件\路由更新記錄_$(Get-Date -Format 'yyyyMMdd_HHmmss').csv"
    $changeLog | Export-Csv -Path $logPath -Encoding UTF8 -NoTypeInformation
    Write-Host "?? 變更記錄已儲存至: $logPath" -ForegroundColor Cyan
}

if ($WhatIf) {
    Write-Host ""
    Write-Host "??  這是模擬執行,未實際修改檔案" -ForegroundColor Yellow
    Write-Host "   執行 .\Scripts\Update-ViewRoutes-Batch2.ps1 -WhatIf:`$false 進行實際更新" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "? 完成!" -ForegroundColor Green
