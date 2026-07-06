# 驗證 ContactId 修復
# 此腳本會檢查 DownloadIntegrateData.cs 中的 ContactId 修改

Write-Host "===== 驗證 ContactId 修復 =====" -ForegroundColor Cyan
Write-Host ""

$filePath = "ChurchReport\WebServiceConnector\DownloadIntegrateData.cs"
$content = Get-Content $filePath -Raw

$issues = @()
$success = @()

Write-Host "檢查修改點..." -ForegroundColor Yellow
Write-Host ""

# 檢查 1: GetAllMemberDataFromPresentRecord - ContactId 變數
Write-Host "1. GetAllMemberDataFromPresentRecord - ContactId 變數宣告" -ForegroundColor Cyan
if ($content -match 'String ContactId = "".*// 聯絡人 ID') {
    Write-Host "  ? ContactId 變數已宣告" -ForegroundColor Green
    $success += "GetAllMemberDataFromPresentRecord - 變數宣告"
} else {
    Write-Host "  ? 缺少 ContactId 變數宣告" -ForegroundColor Red
    $issues += "GetAllMemberDataFromPresentRecord - 變數宣告"
}

# 檢查 2: GetAllMemberDataFromPresentRecord - ContactId 取值
Write-Host "2. GetAllMemberDataFromPresentRecord - ContactId 取值" -ForegroundColor Cyan
if ($content -match 'ContactId = aFullNameEntityReference\.Id\.ToString\(\)') {
    Write-Host "  ? ContactId 取值已設置" -ForegroundColor Green
    $success += "GetAllMemberDataFromPresentRecord - 取值"
} else {
    Write-Host "  ? 缺少 ContactId 取值" -ForegroundColor Red
    $issues += "GetAllMemberDataFromPresentRecord - 取值"
}

# 檢查 3: GetAllMemberDataFromPresentRecord - Member 中的 ContactId
Write-Host "3. GetAllMemberDataFromPresentRecord - Member 對象中的 ContactId" -ForegroundColor Cyan
if ($content -match 'PresentRecordId = PresentRecordEntity\.Id\.ToString\(\),\s+ContactId = ContactId') {
    Write-Host "  ? Member 對象已包含 ContactId" -ForegroundColor Green
    $success += "GetAllMemberDataFromPresentRecord - Member 對象"
} else {
    Write-Host "  ? Member 對象缺少 ContactId" -ForegroundColor Red
    $issues += "GetAllMemberDataFromPresentRecord - Member 對象"
}

# 檢查 4: GetAllMemberDataFromList - ContactId
Write-Host "4. GetAllMemberDataFromList - ContactId" -ForegroundColor Cyan
if ($content -match 'String ContactId = ContactEntity\.Id\.ToString\(\)') {
    Write-Host "  ? GetAllMemberDataFromList 已包含 ContactId" -ForegroundColor Green
    $success += "GetAllMemberDataFromList"
} else {
    Write-Host "  ? GetAllMemberDataFromList 缺少 ContactId" -ForegroundColor Red
    $issues += "GetAllMemberDataFromList"
}

# 檢查 5: SetAllMemberDataByPersonalReport - ContactId
Write-Host "5. SetAllMemberDataByPersonalReport - ContactId" -ForegroundColor Cyan
if ($content -match 'String ContactId = m_ContactEntity\.Id\.ToString\(\)') {
    Write-Host "  ? SetAllMemberDataByPersonalReport 已包含 ContactId" -ForegroundColor Green
    $success += "SetAllMemberDataByPersonalReport"
} else {
    Write-Host "  ? SetAllMemberDataByPersonalReport 缺少 ContactId" -ForegroundColor Red
    $issues += "SetAllMemberDataByPersonalReport"
}

# 檢查 6: CreateMember - ContactId
Write-Host "6. CreateMember - ContactId" -ForegroundColor Cyan
if ($content -match 'ContactId = this\.m_ContactEntity\.Id\.ToString\(\)') {
    Write-Host "  ? CreateMember 已包含 ContactId" -ForegroundColor Green
    $success += "CreateMember"
} else {
    Write-Host "  ? CreateMember 缺少 ContactId" -ForegroundColor Red
    $issues += "CreateMember"
}

Write-Host ""
Write-Host "===== 驗證結果 =====" -ForegroundColor Cyan
Write-Host ""

if ($issues.Count -eq 0) {
    Write-Host "? 所有檢查通過！" -ForegroundColor Green
    Write-Host ""
    Write-Host "已成功修改的項目 ($($success.Count)):" -ForegroundColor Green
    foreach ($item in $success) {
        Write-Host "  ? $item" -ForegroundColor Green
    }
    Write-Host ""
    Write-Host "下一步操作:" -ForegroundColor Yellow
    Write-Host "  1. 重啟應用程式" -ForegroundColor White
    Write-Host "  2. 訪問 /Equipment/EquipmentView" -ForegroundColor White
    Write-Host "  3. 展開小組和聯絡人" -ForegroundColor White
    Write-Host "  4. 查看 Visual Studio 輸出視窗" -ForegroundColor White
    Write-Host ""
    Write-Host "預期日誌輸出:" -ForegroundColor Yellow
    Write-Host "  [LoadEquipmentStorLessons] 查詢課程記錄: ContactName=XXX, ContactId={GUID}" -ForegroundColor White
    Write-Host "  [LoadEquipmentStorLessons] 查詢結果: storLessons=True, Count=X" -ForegroundColor White
} else {
    Write-Host "? 發現 $($issues.Count) 個問題" -ForegroundColor Red
    Write-Host ""
    Write-Host "缺少的項目:" -ForegroundColor Red
    foreach ($item in $issues) {
        Write-Host "  ? $item" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "建議操作:" -ForegroundColor Yellow
    Write-Host "  1. 重新運行修復腳本: powershell -File 'ChurchReport\文件\緊急修復ContactId.ps1'" -ForegroundColor White
    Write-Host "  2. 或手動按照 Member-ContactId添加指南.md 進行修改" -ForegroundColor White
}

Write-Host ""
