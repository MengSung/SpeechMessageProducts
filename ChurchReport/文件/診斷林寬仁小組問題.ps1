# 診斷林寬仁小組問題
# 文件名: 診斷林寬仁小組問題.ps1

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "林寬仁小組歸屬診斷工具" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

$contactName = "林寬仁"
$wrongGroupName = "瑀倢"

Write-Host "診斷目標:" -ForegroundColor Yellow
Write-Host "  聯絡人: $contactName" -ForegroundColor White
Write-Host "  錯誤出現在: $wrongGroupName 小組" -ForegroundColor White
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "CRM 資料檢查步驟" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "步驟 1: 檢查林寬仁所屬的所有名單" -ForegroundColor Green
Write-Host "--------------------------------------" -ForegroundColor Gray
Write-Host "1. 開啟 CRM" -ForegroundColor White
Write-Host "2. 導航到: 行銷 → 進階尋找" -ForegroundColor White
Write-Host "3. 尋找: 名單" -ForegroundColor White
Write-Host "4. 新增相關 → 名單成員 → 新增相關 → 聯絡人" -ForegroundColor White
Write-Host "5. 篩選條件: 全名 = '$contactName'" -ForegroundColor White
Write-Host "6. 執行查詢" -ForegroundColor White
Write-Host ""
Write-Host "   預期結果: 應該顯示林寬仁所屬的所有小組名單" -ForegroundColor Yellow
Write-Host "   ? 如果包含 '$wrongGroupName'，這就是問題所在！" -ForegroundColor Red
Write-Host ""

Write-Host "步驟 2: 檢查瑀倢小組的所有成員" -ForegroundColor Green
Write-Host "--------------------------------------" -ForegroundColor Gray
Write-Host "1. 導航到: 行銷 → 名單" -ForegroundColor White
Write-Host "2. 搜尋包含 '$wrongGroupName' 的名單" -ForegroundColor White
Write-Host "3. 開啟該名單" -ForegroundColor White
Write-Host "4. 點擊 '成員' 標籤" -ForegroundColor White
Write-Host "5. 查找 '$contactName'" -ForegroundColor White
Write-Host ""
Write-Host "   預期結果: 如果找到，需要將他移除" -ForegroundColor Yellow
Write-Host ""

Write-Host "步驟 3: 檢查林寬仁的出席記錄" -ForegroundColor Green
Write-Host "--------------------------------------" -ForegroundColor Gray
Write-Host "1. 導航到: 進階尋找" -ForegroundColor White
Write-Host "2. 尋找: 個人聚會與靈修記錄" -ForegroundColor White
Write-Host "3. 新增相關 → 聯絡人" -ForegroundColor White
Write-Host "4. 篩選條件: 全名 = '$contactName'" -ForegroundColor White
Write-Host "5. 顯示欄位: 名稱、主日日期、小組名單、週報" -ForegroundColor White
Write-Host "6. 執行查詢" -ForegroundColor White
Write-Host ""
Write-Host "   關鍵檢查:" -ForegroundColor Yellow
Write-Host "   - 查看 '小組名單' 欄位" -ForegroundColor White
Write-Host "   - 如果有記錄指向 '$wrongGroupName'，這就是問題！" -ForegroundColor White
Write-Host ""

Write-Host "步驟 4: 檢查瑀倢小組的週報" -ForegroundColor Green
Write-Host "--------------------------------------" -ForegroundColor Gray
Write-Host "1. 導航到: 進階尋找" -ForegroundColor White
Write-Host "2. 尋找: 小組主日週報" -ForegroundColor White
Write-Host "3. 新增相關 → 小組名單" -ForegroundColor White
Write-Host "4. 篩選條件: 名單名稱 包含 '$wrongGroupName'" -ForegroundColor White
Write-Host "5. 選擇最近的週報，開啟" -ForegroundColor White
Write-Host "6. 查看關聯的出席記錄" -ForegroundColor White
Write-Host ""
Write-Host "   關鍵檢查:" -ForegroundColor Yellow
Write-Host "   - 如果週報中包含 '$contactName' 的出席記錄" -ForegroundColor White
Write-Host "   - 這表示該出席記錄錯誤地關聯到瑀倢小組的週報" -ForegroundColor White
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "修復方案" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "方案 A: 從名單中移除（如果在步驟 2 發現）" -ForegroundColor Green
Write-Host "--------------------------------------" -ForegroundColor Gray
Write-Host "1. 在瑀倢小組的成員列表中" -ForegroundColor White
Write-Host "2. 勾選 '$contactName'" -ForegroundColor White
Write-Host "3. 點擊工具列的 '從名單移除'" -ForegroundColor White
Write-Host "4. 確認移除" -ForegroundColor White
Write-Host ""

Write-Host "方案 B: 修正出席記錄（如果在步驟 3 發現）" -ForegroundColor Green
Write-Host "--------------------------------------" -ForegroundColor Gray
Write-Host "1. 開啟錯誤的出席記錄" -ForegroundColor White
Write-Host "2. 找到 '小組名單' 欄位" -ForegroundColor White
Write-Host "3. 修改為林寬仁正確的小組" -ForegroundColor White
Write-Host "4. 找到 '週報' 欄位" -ForegroundColor White
Write-Host "5. 修改為正確小組的週報（或清空）" -ForegroundColor White
Write-Host "6. 儲存" -ForegroundColor White
Write-Host ""

Write-Host "方案 C: 刪除錯誤的出席記錄（如果是重複或錯誤建立）" -ForegroundColor Green
Write-Host "--------------------------------------" -ForegroundColor Gray
Write-Host "1. 確認該出席記錄是錯誤建立的" -ForegroundColor White
Write-Host "2. 開啟該記錄" -ForegroundColor White
Write-Host "3. 點擊 '刪除'" -ForegroundColor White
Write-Host "4. 確認刪除" -ForegroundColor White
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "驗證修復" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "1. 清除應用程式快取" -ForegroundColor Green
Write-Host "   - 重新啟動應用程式" -ForegroundColor White
Write-Host "   - 或在瀏覽器中清除快取（Ctrl + Shift + Delete）" -ForegroundColor White
Write-Host ""

Write-Host "2. 重新訪問裝備狀態頁面" -ForegroundColor Green
Write-Host "   - 訪問: /Equipment/EquipmentView" -ForegroundColor White
Write-Host ""

Write-Host "3. 檢查瑀倢小組" -ForegroundColor Green
Write-Host "   - 展開瑀倢小組" -ForegroundColor White
Write-Host "   - 確認 '$contactName' 不再出現" -ForegroundColor White
Write-Host ""

Write-Host "4. 檢查林寬仁的正確小組" -ForegroundColor Green
Write-Host "   - 確認林寬仁應該在哪個小組" -ForegroundColor White
Write-Host "   - 展開該小組" -ForegroundColor White
Write-Host "   - 確認 '$contactName' 出現在正確的位置" -ForegroundColor White
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "常見問題" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Q1: 修復後仍然看到林寬仁在瑀倢小組？" -ForegroundColor Yellow
Write-Host "A1: 可能是快取問題" -ForegroundColor White
Write-Host "    - 重新啟動應用程式" -ForegroundColor Gray
Write-Host "    - 清除瀏覽器快取" -ForegroundColor Gray
Write-Host "    - 使用無痕模式重新測試" -ForegroundColor Gray
Write-Host ""

Write-Host "Q2: 找不到錯誤的出席記錄？" -ForegroundColor Yellow
Write-Host "A2: 可能在週報層級" -ForegroundColor White
Write-Host "    - 檢查瑀倢小組的週報" -ForegroundColor Gray
Write-Host "    - 查看週報關聯的所有出席記錄" -ForegroundColor Gray
Write-Host "    - 使用 FetchXML 查詢（參考診斷報告）" -ForegroundColor Gray
Write-Host ""

Write-Host "Q3: 林寬仁出現在多個小組？" -ForegroundColor Yellow
Write-Host "A3: 可能有多筆錯誤的出席記錄" -ForegroundColor White
Write-Host "    - 檢查所有出席記錄" -ForegroundColor Gray
Write-Host "    - 逐一修正或刪除錯誤的記錄" -ForegroundColor Gray
Write-Host "    - 確保只有正確小組的記錄保留" -ForegroundColor Gray
Write-Host ""

Write-Host "Q4: 不確定林寬仁應該在哪個小組？" -ForegroundColor Yellow
Write-Host "A4: 聯繫小組長確認" -ForegroundColor White
Write-Host "    - 確認林寬仁的實際所屬小組" -ForegroundColor Gray
Write-Host "    - 確認是否最近有轉組" -ForegroundColor Gray
Write-Host "    - 根據確認結果修正資料" -ForegroundColor Gray
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "FetchXML 查詢範例" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "查詢 1: 林寬仁所屬的所有名單" -ForegroundColor Green
Write-Host @"
<fetch version='1.0'>
  <entity name='list'>
    <attribute name='listname' />
    <attribute name='listid' />
    <link-entity name='listmember' from='listid' to='listid'>
      <link-entity name='contact' from='contactid' to='entityid'>
        <filter>
          <condition attribute='fullname' operator='eq' value='林寬仁' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>
"@ -ForegroundColor Gray
Write-Host ""

Write-Host "查詢 2: 林寬仁的所有出席記錄" -ForegroundColor Green
Write-Host @"
<fetch version='1.0'>
  <entity name='new_present_record'>
    <attribute name='new_name' />
    <attribute name='new_sunday_date' />
    <attribute name='new_list_new_present_record' />
    <attribute name='new_group_present_weekly_report_prese' />
    <link-entity name='contact' from='contactid' to='new_contact_new_present_record'>
      <filter>
        <condition attribute='fullname' operator='eq' value='林寬仁' />
      </filter>
    </link-entity>
    <link-entity name='list' from='listid' to='new_list_new_present_record' alias='list'>
      <attribute name='listname' />
    </link-entity>
  </entity>
</fetch>
"@ -ForegroundColor Gray
Write-Host ""

Write-Host "查詢 3: 瑀倢小組週報中的所有出席記錄" -ForegroundColor Green
Write-Host @"
<fetch version='1.0'>
  <entity name='new_present_record'>
    <attribute name='new_name' />
    <attribute name='new_contact_new_present_record' />
    <link-entity name='new_group_present_weekly_report' from='new_group_present_weekly_reportid' to='new_group_present_weekly_report_prese'>
      <link-entity name='list' from='listid' to='new_list_weekly_report'>
        <filter>
          <condition attribute='listname' operator='like' value='%瑀倢%' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>
"@ -ForegroundColor Gray
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "診斷完成" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "下一步:" -ForegroundColor Yellow
Write-Host "1. 按照上述步驟在 CRM 中檢查" -ForegroundColor White
Write-Host "2. 找到問題根源" -ForegroundColor White
Write-Host "3. 執行修復方案" -ForegroundColor White
Write-Host "4. 驗證修復結果" -ForegroundColor White
Write-Host ""

Write-Host "如果需要更多幫助，請參考:" -ForegroundColor Cyan
Write-Host "- 林寬仁錯誤出現在瑀倢小組診斷報告.md" -ForegroundColor Gray
Write-Host ""

Read-Host "按 Enter 鍵結束"
