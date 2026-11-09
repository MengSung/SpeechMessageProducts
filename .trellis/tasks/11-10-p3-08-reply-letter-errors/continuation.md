# P3-08 進行中保存點與單目錄接續

- 2026-11-10 07:23 Asia/Taipei (UTC+08:00)：使用者核定回到正式 P3 單目錄、一次只做一包；先保存目前程式與規劃，不代表驗收。
- 本保存點：專用測試 4/4、AspNetCore Release 73/73、進度頁 28/28、generate --check 6 檔通過。
- 未通過：I18n verify 顯示 scope resolver 的 CJK 增量；文件守門受未追蹤 launchSettings.json 缺 companion 影響。完整固定 guard、正式 Reply transport 測試、雙模型審查及最終驗收未完成，不標 done。
- 決策＋理由：保留原程式與離線證據，先以「進行中」提交作可回復保存點；主目錄 20:00 資訊以逐項比對併入歷史，不回退 P3-06／07 驗收狀態。
- 外部分析：Claude 完成，Gemini 403／503 未完成；不得宣稱雙模型成功。後續仍由專案自復原入口呼叫兩個 backend 審查。
- 單目錄搬移：正式分支只做 fast-forward，不 merge、不 force push；evidence 逐檔 SHA-256 核對後才刪來源。清理舊分支前核對遠端正式 P3 已包含全部提交。
- P3-09 前置缺項仍未到位，不開工；不碰 main、PR、設計 v3.4 或階段 4。
