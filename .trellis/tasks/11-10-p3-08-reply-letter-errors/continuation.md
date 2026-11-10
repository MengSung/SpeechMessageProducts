# P3-08 進行中保存點與單目錄接續

- 2026-11-10 07:23 Asia/Taipei (UTC+08:00)：使用者核定回到正式 P3 單目錄、一次只做一包；先保存目前程式與規劃，不代表驗收。
- 本保存點：專用測試 4/4、AspNetCore Release 73/73、進度頁 28/28、generate --check 6 檔通過。
- 未通過：I18n verify 顯示 scope resolver 的 CJK 增量；文件守門受未追蹤 launchSettings.json 缺 companion 影響。完整固定 guard、正式 Reply transport 測試、雙模型審查及最終驗收未完成，不標 done。
- 決策＋理由：保留原程式與離線證據，先以「進行中」提交作可回復保存點；主目錄 20:00 資訊以逐項比對併入歷史，不回退 P3-06／07 驗收狀態。
- 外部分析：Claude 完成，Gemini 403／503 未完成；不得宣稱雙模型成功。後續仍由專案自復原入口呼叫兩個 backend 審查。
- 單目錄搬移：正式分支只做 fast-forward，不 merge、不 force push；evidence 逐檔 SHA-256 核對後才刪來源。清理舊分支前核對遠端正式 P3 已包含全部提交。
- P3-09 前置缺項仍未到位，不開工；不碰 main、PR、設計 v3.4 或階段 4。
- 2026-11-10 07:51 Asia/Taipei (UTC+08:00)：決策：將 ResolvedWebhookScope 兩個相容建構式的預設 churchName 由 CJK 顯示字串改為 Church。理由：正式 resolver 一律傳入 CRM 實際教會名稱；相容建構式只供離線／既有呼叫端，使用中性非本地化占位可消除 CJK 棘輪新增而不改變正式輸出。
- 2026-11-10 08:05 Asia/Taipei (UTC+08:00)：首次固定 guard 的全方案測試發現 Localization 兩目標各一個固定數量斷言仍期待 19，但 P3-08 已正式加入 Webhook.Letter.Subject，實際獨立資源鍵為 20。決策：同步更新獨立 ExpectedSystemKeys、兩個數量斷言及檔案說明，保留明確鍵清單；理由：修正測試契約的真實基準，不降低測試強度，也不補回已刪除的 Plugin Debug 建置行。
- 2026-11-10 08:12 Asia/Taipei (UTC+08:00)：固定 guard 的 Changed encoding 發現新加入 SpeechMessageProducts.LineWebhook/Properties/launchSettings.json 缺 final CRLF。決策：只補檔尾 CRLF，不改 JSON 內容；理由：符合 .gitattributes 與 AGENTS.md 的 UTF-8 無 BOM／CRLF／末尾 CRLF 契約，且不影響啟動設定語意。
- 2026-11-10 08:37 Asia/Taipei (UTC+08:00)：使用者明確指示停止雙模型審查，取代前述仍預期呼叫兩個 backend 的安排。決策：不啟動 Gemini、Claude 或雙模型 runner；以本機測試、固定 guard 與協調者自審形成驗收證據，並明確註記未取得外部審查。理由：遵循使用者最新授權，不把先前只有單方完成的分析誤稱為雙模型結果。
