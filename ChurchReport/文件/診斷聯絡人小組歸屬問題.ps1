# 診斷聯絡人小組歸屬問題
# 文件名: 診斷聯絡人小組歸屬問題.ps1

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "裝備狀態聯絡人小組歸屬診斷工具" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# 請輸入問題聯絡人的姓名
$contactName = Read-Host "請輸入問題聯絡人的姓名（例如：王五）"
$wrongGroup = Read-Host "錯誤顯示在哪個小組？（例如：小組 A）"
$correctGroup = Read-Host "應該在哪個小組？（例如：小組 B）"

Write-Host ""
Write-Host "診斷資訊:" -ForegroundColor Yellow
Write-Host "  聯絡人: $contactName" -ForegroundColor White
Write-Host "  錯誤顯示在: $wrongGroup" -ForegroundColor Red
Write-Host "  應該在: $correctGroup" -ForegroundColor Green
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "診斷步驟" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "步驟 1: 檢查聯絡人所屬的名單" -ForegroundColor Green
Write-Host "--------------------------------------" -ForegroundColor Gray
Write-Host "1. 開啟 CRM" -ForegroundColor White
Write-Host "2. 導航到: 行銷 → 進階尋找" -ForegroundColor White
Write-Host "3. 尋找: 名單" -ForegroundColor White
Write-Host "4. 新增相關 → 名單成員 → 新增相關 → 聯絡人" -ForegroundColor White
Write-Host "5. 篩選條件: 全名 = '$contactName'" -ForegroundColor White
Write-Host "6. 執行查詢" -ForegroundColor White
Write-Host ""
Write-Host "   檢查重點:" -ForegroundColor Yellow
Write-Host "   - $contactName 是否在 '$wrongGroup' 的名單中？" -ForegroundColor White
Write-Host "   - $contactName 是否在 '$correctGroup' 的名單中？" -ForegroundColor White
Write-Host "   - $contactName 是否在多個名單中？" -ForegroundColor White
Write-Host ""
Read-Host "完成步驟 1 後按 Enter 繼續"
Write-Host ""

Write-Host "步驟 2: 檢查聯絡人的出席記錄" -ForegroundColor Green
Write-Host "--------------------------------------" -ForegroundColor Gray
Write-Host "1. 導航到: 進階尋找" -ForegroundColor White
Write-Host "2. 尋找: 個人聚會與靈修記錄" -ForegroundColor White
Write-Host "3. 新增相關 → 聯絡人" -ForegroundColor White
Write-Host "4. 篩選條件: 全名 = '$contactName'" -ForegroundColor White
Write-Host "5. 顯示欄位: 名稱、主日日期、小組名單、週報" -ForegroundColor White
Write-Host "6. 執行查詢" -ForegroundColor White
Write-Host ""
Write-Host "   檢查重點:" -ForegroundColor Yellow
Write-Host "   - 每筆記錄的 '小組名單' 欄位指向哪個小組？" -ForegroundColor White
Write-Host "   - 是否有記錄指向 '$wrongGroup'？" -ForegroundColor White
Write-Host "   - 是否有記錄指向 '$correctGroup'？" -ForegroundColor White
Write-Host ""

Write-Host "   記錄您的發現:" -ForegroundColor Yellow
$record1Date = Read-Host "   記錄 1 日期（例如：2024/01/15，無則按 Enter）"
if ($record1Date) {
    $record1Group = Read-Host "   記錄 1 的小組名單"
    Write-Host "     → 記錄 1: $record1Date → $record1Group" -ForegroundColor Gray
}

$record2Date = Read-Host "   記錄 2 日期（例如：2024/01/22，無則按 Enter）"
if ($record2Date) {
    $record2Group = Read-Host "   記錄 2 的小組名單"
    Write-Host "     → 記錄 2: $record2Date → $record2Group" -ForegroundColor Gray
}
Write-Host ""
Read-Host "完成步驟 2 後按 Enter 繼續"
Write-Host ""

Write-Host "步驟 3: 檢查週報包含的出席記錄" -ForegroundColor Green
Write-Host "--------------------------------------" -ForegroundColor Gray
Write-Host "1. 導航到: 進階尋找 → 小組主日週報" -ForegroundColor White
Write-Host "2. 篩選條件: 小組名單 = '$wrongGroup'" -ForegroundColor White
Write-Host "3. 選擇最近的週報，開啟" -ForegroundColor White
Write-Host "4. 查看 '相關' → '個人聚會與靈修記錄'" -ForegroundColor White
Write-Host "5. 檢查是否包含 '$contactName' 的記錄" -ForegroundColor White
Write-Host ""
$foundInReport = Read-Host "   在 '$wrongGroup' 的週報中找到 '$contactName' 的記錄嗎？(Y/N)"
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "診斷結果分析" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

if ($foundInReport -eq "Y" -or $foundInReport -eq "y") {
    Write-Host "? 確認問題:" -ForegroundColor Red
    Write-Host "  $contactName 的出席記錄錯誤地關聯到 '$wrongGroup' 的週報" -ForegroundColor White
    Write-Host ""
    Write-Host "建議修復方案:" -ForegroundColor Yellow
    Write-Host "  → 使用 '方案 A: 修正出席記錄的小組關聯'" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "? 週報中沒有找到記錄" -ForegroundColor Yellow
    Write-Host "  可能原因:" -ForegroundColor White
    Write-Host "  - $contactName 被錯誤地加入 '$wrongGroup' 的名單中" -ForegroundColor White
    Write-Host "  - 程式碼在載入時沒有正確過濾" -ForegroundColor White
    Write-Host ""
    Write-Host "建議修復方案:" -ForegroundColor Yellow
    Write-Host "  → 使用 '方案 B: 從錯誤的名單中移除聯絡人'" -ForegroundColor Green
    Write-Host ""
}

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "修復步驟" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

if ($foundInReport -eq "Y" -or $foundInReport -eq "y") {
    Write-Host "方案 A: 修正出席記錄的小組關聯" -ForegroundColor Green
    Write-Host "--------------------------------------" -ForegroundColor Gray
    Write-Host "1. 在步驟 2 的查詢結果中，找到關聯到 '$wrongGroup' 的記錄" -ForegroundColor White
    Write-Host "2. 開啟該記錄" -ForegroundColor White
    Write-Host "3. 修改 '小組名單' 欄位 → 選擇 '$correctGroup'" -ForegroundColor White
    Write-Host "4. 檢查 '週報' 欄位 → 確保指向 '$correctGroup' 的週報（或清空）" -ForegroundColor White
    Write-Host "5. 儲存" -ForegroundColor White
    Write-Host ""
} else {
    Write-Host "方案 B: 從錯誤的名單中移除聯絡人" -ForegroundColor Green
    Write-Host "--------------------------------------" -ForegroundColor Gray
    Write-Host "1. 導航到: 行銷 → 名單 → '$wrongGroup'" -ForegroundColor White
    Write-Host "2. 點擊 '成員' 標籤" -ForegroundColor White
    Write-Host "3. 找到 '$contactName'" -ForegroundColor White
    Write-Host "4. 勾選並點擊 '從名單移除'" -ForegroundColor White
    Write-Host "5. 確認移除" -ForegroundColor White
    Write-Host ""
    Write-Host "6. 確認 '$contactName' 在 '$correctGroup' 的名單中" -ForegroundColor White
    Write-Host "   如果不在，請手動添加" -ForegroundColor White
    Write-Host ""
}

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "驗證修復" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "1. 清除應用程式快取" -ForegroundColor Green
Write-Host "   - 重新啟動應用程式" -ForegroundColor White
Write-Host "   - 或清除瀏覽器快取（Ctrl + Shift + Delete）" -ForegroundColor White
Write-Host ""

Write-Host "2. 測試裝備狀態頁面" -ForegroundColor Green
Write-Host "   - 訪問: /Equipment/EquipmentView" -ForegroundColor White
Write-Host "   - 展開 '$wrongGroup' → 確認 '$contactName' 不在列表中" -ForegroundColor White
Write-Host "   - 展開 '$correctGroup' → 確認 '$contactName' 在列表中" -ForegroundColor White
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "FetchXML 查詢範例" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "查詢 $contactName 的出席記錄及其小組關聯:" -ForegroundColor Green
Write-Host @"
<fetch version='1.0'>
  <entity name='new_present_record'>
    <attribute name='new_name' />
    <attribute name='new_sunday_date' />
    <attribute name='new_list_new_present_record' />
    <link-entity name='contact' from='contactid' to='new_contact_new_present_record'>
      <filter>
        <condition attribute='fullname' operator='eq' value='$contactName' />
      </filter>
    </link-entity>
    <link-entity name='list' from='listid' to='new_list_new_present_record' alias='list'>
      <attribute name='listname' />
    </link-entity>
  </entity>
</fetch>
"@ -ForegroundColor Gray
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "診斷完成" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "參考文檔:" -ForegroundColor Cyan
Write-Host "- 裝備狀態聯絡人錯誤歸屬問題診斷與修復指南.md" -ForegroundColor Gray
Write-Host "- 林寬仁錯誤出現在瑀倢小組診斷報告.md（類似案例）" -ForegroundColor Gray
Write-Host ""

Read-Host "按 Enter 鍵結束"
