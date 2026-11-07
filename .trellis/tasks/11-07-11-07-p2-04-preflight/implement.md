# P2-04 前置執行檢查表

## 啟動與盤點

- [x] 讀取完整 AGENTS.md、3.4 計畫、附錄 G/I 及目前進度頁 DATA。
- [x] 以鍵名確認兩個 User Secrets 存在，未輸出秘密值。
- [x] 建立本次操作專屬 evidence 目錄；不寫入原始 secrets、trace、TRX 或完整 log。
- [ ] 取得目前 Git HEAD、Plugin 清單、組織 export、步驟啟用狀態、追蹤設定及待處理作業摘要。

## 實機流程

- [ ] 確認 D365-9.1 → `api.line.me:443` TLS 1.2。
- [x] 變更前保存原況與回復方式。
- [x] 暫時設定 push 佔位、啟用固定 DiagnosticsPingPlugin 步驟。
- [x] 執行 plan，立即檢查該次 `plan.md` 只有預期變更且沒有秘密值。
- [ ] 執行 apply，確認固定成功摘要。
- [ ] 執行 verify，正確區分 metadata verify=3 與完整 verify=0。
- [ ] 執行 `p0-ping-push` smoke，確認 smoke=0、LINE 200、有效 RequestId、追蹤無敏感內容、task 已清除。

## 收尾與證據

- [x] 設定回 trace、secureConfig 空白、enabled=false。
- [ ] 重新 plan → 審核 → apply → verify=0。
- [ ] 追蹤回「例外狀況」，再次確認本次 task、暫存資料及暫存檔殘留為 0。
- [x] 建立一頁操作紀錄及 SHA-256，內容不含 secrets、token、userId、訊息內容或完整追蹤。
- [x] 更新 3.4 進度頁 DATA、跑進度頁契約測試、UTF-8 無 BOM／CRLF／檔尾 CRLF 檢查。
- [ ] 若本前置成功，進度頁標記里程碑完成，P2-04 本體仍須另行實作與驗收。

## 2026-11-08 接續

apply 失敗，未 smoke；trace 已回 1，live DLL／step 與 pre-apply 原況一致。TLS、舊流程精確盤點及整體 verify=0 未完成；缺組織加密復原材料，詳一頁報告。
