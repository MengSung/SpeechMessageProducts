# P8 自主決策紀錄

| 時間（Asia/Taipei） | 單元 | 遇到的選擇 | 選了什麼 | 依據 | 怎麼回復 |
|---|---|---|---|---|---|
| 2026-11-19 21:00 | P8-01a | 08A 要求開工唯讀查詢 D365 選項標籤與 RequiredLevel；AGENTS 禁止連任何正式組織，既有 verify 未取得狀態 | 不連線；以 schema 和合成測試繼續離線實作，RequiredLevel 保留未知，不製造黃金檔或實機結論；在上線前置清單列為待查證 | AGENTS 優先於 08A；08A §7.2「只有使用者能做的事跳過並記錄，繼續其他步驟」及 §7.1 範圍優先 | 上線前取得明確授權並依正式切換流程唯讀查證，再決定必填分支；不需回復本單元離線程式 |
| 2026-11-19 21:12 | P8-01a | 08A 與 CCG 規則要求雙模型分析；自癒 runner 重試兩次後 Gemini 仍回 503，summary 為 `dualModelComplete=false`、`degradedFallback=false`，Claude 有輸出 | 不宣稱雙模型分析完成；保留 Claude 分析作單模型參考，以既有程式、合約與測試作裁決依據，繼續離線工作；不再連續重試外部服務 | `.trellis/spec/guides/ccg-external-review-thinking-guide.md` 故障分類；08A §7.2 外部服務不可用的續行政策 | 若服務恢復，後續必要外部審查仍須由同一自癒入口執行；不影響程式回復 |
| 2026-11-19 21:12 | P8-01a | 收費單／預約若同時提供直接教會 lookup 與付款人教會，來源可能不一致 | 依 D8-8 順序將實體直接教會 lookup 定為優先權威；只有實體沒有該欄時才解析付款人的 account `parentcustomerid`，非 account 或缺值不回填並計入無法判定 | 08A D8-8；08A §7.1 既有程式與範圍優先、較小範圍優先 | 若未來資料契約改以付款人為權威，需另立核定變更；本單元不自動改寫任何既有 lookup |
| 2026-11-19 23:33 | P8-01a | 兩個來源 choice 的值集合不同，原 adapter 以完整集合相等拒絕整份 plan；共享值的 label 仍須避免任意覆蓋 | 將兩集合依數值聯集，不同值各保留其 metadata label；共享值 label 不同則 fail closed；兩欄 RequiredLevel 分開記錄。新增合成 SDK 測試先紅後綠 | 08A §7.1 第 3、4、5 條；每個正式選項值為身份、標籤來自 metadata；不以顯示名稱合併 | 回退可將 adapter 恢復為集合相等檢查並撤除兩個合成案例；沒有資料或組織寫入 |
| 2026-11-19 23:33 | P8-01a | 續跑唯讀 `verify --org goodshepherdback` 再次以 `InvalidDataException` 失敗；前次投影摘要有值與 RequiredLevel、沒有 labels，原始 snapshot 未在目前可用目錄找到 | 不再重試（累計兩次失敗），不推定 off／verify=0，不製作假黃金檔；保留離線實作與測試，P8-01a 維持進行中，未查證事項列上線前置清單 | 08A §0.4、§7.2、§7.3；使用者禁止連正式組織，實機限定 goodshepherdback | 離線程式無組織副作用；上線前須由獲授權人員取得原始唯讀 metadata 證據並補驗 labels 及狀態 |
| 2026-11-20 01:07 | P8-01a | 是否因 option labels 與 `verify=0` 缺少而不驗收離線單元 | 更正前述狀態：依 08A §5 P8-01a 完成條件及 00 §7.1，不加列實機要求；16 個指定測試及雙目標、完整守門均通過，故驗收 P8-01a。labels／組織狀態仍明確標未查證；verify 累計兩次失敗後不再重試 | 08A §5 P8-01a、00 §7.1、AGENTS 的實機邊界 | 沒有組織寫入；上線前由獲授權人員補取得原始 metadata／狀態證據 |
