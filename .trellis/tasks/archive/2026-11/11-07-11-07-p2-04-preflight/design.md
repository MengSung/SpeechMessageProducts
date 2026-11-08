# P2-04 前置技術設計

## 邊界

部署 CLI 使用 `SpeechMessage.Dynamics.Deployment` 的既有 User Secrets provider、組織白名單、單命令 SDK lease、同組織寫鎖及 plan hash。所有 secrets 只在 apply 解析，plan、export、進度頁及操作紀錄只出現佔位名稱或固定摘要。

## 執行順序

1. 讀取鍵名存在性，不讀值。
2. 以既有 export、追蹤設定、步驟 metadata 及待處理作業查詢保存原況；必要時保存 D365/SQL 回復資訊。
3. 驗證 D365-9.1 對 `api.line.me:443` 的連線；如需出站例外，先記錄原設定，再做單一可回復變更。
4. 將註冊清單暫時改為 push 佔位並啟用同一 `DiagnosticsPingPlugin` 步驟；plan 後立即固定並人工檢查 plan.md，才允許 apply。
5. verify 只用來驗公開 metadata；含秘密佔位的 verify=3 不能作為 secret equality 或推播成功證據。
6. smoke 自己建立的 task 只在測試組織存在，完成後由工具刪除；以 smoke 時窗讀取對應追蹤紀錄，逐欄比對固定判定，不保存追蹤全文。
7. finally 路徑執行清單回復、plan/apply、verify=0、追蹤回例外狀況及殘留清查；若中途失敗，先停止新增寫入，依已保存 export 回復。

## 資源與隔離

- 同一組織的寫入命令依既有寫鎖序列化。
- 每個 CLI 命令只持有自己的 connector lease，完成或故障都 Dispose；逾時／取消後不重用。
- User Secrets 值不進 PowerShell 參數、stdout、stderr、log、artifact 摘要或 Git。
- 只使用 `goodshepherdback` 測試組織與測試 channel；正式組織與正式收件人不在允許範圍。

## 失敗處理

任何 plan/apply/verify/smoke 或清理失敗均保留固定錯誤類型、結束碼及去識別化證據；不回顯 SDK fault 原文、token、userId、訊息內容。清單未能回復或殘留不為 0 時，不標示前置完成並繼續追查可回復路徑。
