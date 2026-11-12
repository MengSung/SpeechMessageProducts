# 方案整合與國際化階段 4：金流＋幣別

## 目標

依 `文件\規劃設計書\實作計畫-3.5\04A_階段4_執行細則.md` 完成 P4-00、P4-01、P4-02、P4-04、P4-03、P4-05、P4-06 及階段審查，完成後停止，不啟動階段 5。本階段起點為 `43ea38cd`，工作分支為 `1.0.0.11.Integration.P4.Worktree`。

## 需求與限制

- 04A 是本階段執行依據；與 04 不一致時依 04A。04A §2 的 D4-1～D4-12 為已核定決定，不重新提案。
- 工作包順序固定為 P4-00 → P4-01 → P4-02 → P4-04 → P4-03 → P4-05 → P4-06 → 階段審查 → 停止。
- 永豐設定沿用 `Payment:Profiles:{profileName}`；設定區段為 `Payment`。依 D365 待辦所屬教會讀取 `account.new_sinopacprofile`，不可由 payload 或呼叫端指定設定檔；缺值或設定檔不存在時失敗關閉。
- 撥款待辦的 `sourceId` 是收費單 ID，payload 僅含 `feeId`；教會範圍以待辦的 `ChurchId` 為準，並核對收費單、會友及其 `parentcustomerid`。
- `Payment:Outbound:Enabled` 與 `Payment:Callbacks:Enabled` 預設皆為 `false`；缺少 `Payment` 設定時 LineWebhook 仍須正常啟動。
- 不新增 `.csproj`，不擴充 `tools\LegacyGolden`，不改動 ChurchReport `appsettings*.json` 既有金鑰；不接觸正式組織、正式商店或正式 LINE Channel，不進行真實付款。
- P4-06 僅依 04A 指定的 `Category=PaymentsLive` 在開發機連接測試組織與永豐沙箱；不重新部署 `LineWebhookP309`，不在本機啟動整台 LineWebhook。
- 產品程式提交在固定 guard `C:\smv\guard` 執行 00 §8.1；只改紀錄的提交執行 §8.3。P4-00 只執行 §8.3，且不推送；第一個推送在 P4-01 驗收後，僅推 P4 分支並以 `git ls-remote` 核對 SHA。
- 原始 TRX、log 與審查全文留在本機 evidence；版控只收一頁工作包回報及 SHA-256。不得輸出秘密值至命令列、repo、log、回報或進度頁。

## 驗收條件

- [ ] P4-00 至 P4-06 依序完成；每包依 00 §7.1 的五項及 04A 該包列明的同名測試驗收，不自行加項。
- [ ] 每包有一頁附錄 F 回報，位於該 Trellis 子任務的 `wp-reports`；進度頁只修改 DATA，逐筆包含台北日期時間，並保留「之後再說」與「計畫修正提案」兩份清單。
- [ ] P4-01 起每次程式提交通過固定 guard §8.1；紀錄型提交通過 §8.3。每包驗收後推送 P4 分支並以 `git ls-remote` 核對遠端 SHA；P4-00 依 D4-11 例外不推送。
- [ ] 階段審查依 00 §6.6 留下證據與一頁摘要；Critical／High 問題依規則處理。報告明確區分已驗證與未驗證，且 P4-06 第二個案例標明使用合成的永豐回應。
- [ ] 完成階段 4 後停止，不啟動階段 5、不推送 `main`、不開 PR。
