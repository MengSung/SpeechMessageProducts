# P2-04 前置：沙箱 LINE 測試推播

## 依據與範圍

依《實作計畫 3.4》`03_階段2-4_共用送訊_Webhook_金流.md` 的 P2-04 前置驗收段落、`00_總覽與執行規則.md` 及附錄 G、I 執行。目標只限測試組織 `goodshepherdback` 與測試 LINE channel，不操作正式組織、不執行真實扣款、不向未授權收件人發訊。

本子任務包含：

- 只確認 `TestChannelToken`、`TestLineUserId` 兩個 User Secrets 鍵存在，不輸出或保存秘密值。
- 保存測試組織目前可回復原況、Plugin 步驟狀態、追蹤設定及本次操作摘要。
- 確認 D365-9.1 對 `api.line.me:443` 的 TLS 連通性；若需修改，只限可回復的測試環境出站例外。
- 以 `DiagnosticsPingPlugin` 的固定步驟執行 push 模式的 plan → apply → verify。
- 執行 `smoke --org goodshepherdback --scenario p0-ping-push`，確認 smoke 結束碼、LINE HTTP 200、有效 RequestId、追蹤保密及測試 task 清理。
- 無論成功或失敗，都把清單改回 trace、secureConfig 清空、步驟停用、apply 後 verify=0，追蹤改回「例外狀況」，並確認殘留測試資料為 0。
- 產生一頁不含秘密、token、userId、追蹤全文或原始 log 的操作紀錄，並保存其 SHA-256。

不包含 P2-04 本體擴充；必要時只修復阻礙本前置的診斷缺陷，不把離線或 mock 測試當作實機驗收。

## 必要條件

- `Deployment:Organizations:goodshepherdback:TestChannelToken` 存在。
- `Deployment:Organizations:goodshepherdback:TestLineUserId` 存在。
- 部署 Plugin 產物為計畫核定的 `net462` 版本，且通過既有 Plugin 打包檢查。
- 只操作 `goodshepherdback`，所有寫入前先保存原況及回復方式。

## 驗收條件

- [ ] 第 0 步清理與原況保存完成，並證明測試資料、暫存作業及回復資訊可追蹤。
- [ ] 第 1 步 D365-9.1 對 `api.line.me:443` TLS 1.2 連通性通過，或已記錄明確阻塞與回復方式。
- [ ] 第 2～3 步 push 佔位清單、plan、apply、verify 完成；含秘密佔位時不得把 verify=3 誤標為完整成功。
- [ ] 第 4 步 `p0-ping-push` smoke 結束碼為 0，追蹤判定 `Outcome=Accepted`、`Status=200`、有效 RequestId，且不含 token、userId 或訊息內容。
- [ ] 第 5 步清單回 trace、步驟停用、收尾 verify=0、追蹤回「例外狀況」、測試資料與暫存資源殘留為 0。
- [ ] 第 6 步一頁操作紀錄完成，保存檔案 SHA-256；原始秘密、追蹤全文與原始 log 不進 repo。
- [ ] 進度頁 3.4 的 P2 摘要、P2-04 前置里程碑、阻塞與下一步已同步。
