# 執行計畫

1. 固定基準與寫入邊界，讀取正式 P1-01／00 §8／CI 規範，雙模型分析驗收缺口。
2. 逐項復核刪除前與刪除後引用、51 檔清單、15 個原測試名字保留、16 案實際執行與 ToolUtility.Tests 方案組態。
3. 在獨立 worktree 跑完整 Release build/test、Debug tracer、encoding Changed、duplicates、I18nCheck、架構及離線打包。
4. 撰寫驗收表，明列新驗證、歷史證據、DevExpress 決策與完整 hosted CI 限制；必要時修本次文檔問題，不接管主線產品修改。
5. 審查、位元組與 diff 檢查，保存本地提交；歸檔本收尾紀錄，通知主線以報告更新進度。
