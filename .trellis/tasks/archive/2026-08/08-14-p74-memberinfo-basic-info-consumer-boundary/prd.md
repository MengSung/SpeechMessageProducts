# P7.4 MemberInfo basic-info consumer boundary

## 目標與使用者價值

以權威 matrix 的 `ORG-CALL-00030`（`memberinfo.contact.update.basic.info`）評估既有
`MemberInfoController.UpdateContactInfo` 能否安全接入既有 P7.2 typed ProductClient。若 action 同時包含
未由 typed contract 承接的寫入，必須 fail closed；不得以局部 Gateway 寫入或 legacy fallback 製造錯誤進度。

## 已確認事實

1. `ORG-CALL-00030` 的 registry、Data8 executor、ProductClient 已存在；consumer 尚未遷移，CE 與
   Embedded/Dedicated evidence 仍為 `evidence-pending`。
2. `UpdateContactInfo` 在同一 request 可更新 `mobilephone`、`address2_line1`、`customertypecode`、
   `new_spiriitual_identity` 四欄。
3. 既有 typed/Data8 contract 僅允許 phone/address，並對兩個 OptionSet fail closed；read-back 也只有
   兩個字串欄位。
4. 局部接線會形成 Gateway + ToolUtility split-brain；略過兩個 OptionSet 則會在 gate=true 時改變原本四欄語意。
   目前沒有共同 deadline、完整 read-back、reconciliation、reverse-order cleanup 或 single rollback owner。
5. P7.2 歷史 Slice C 是 `write-not-committed` no-go 且 cleanup 完成；不得重試、復用或改動其 nonce、ledger、
   fixture 或 descriptor。
6. P7.4 capacity/non-overlap evidence 尚未成立；gate 維持 false，本 child 不得 CE、切流、P7.5 或 P8。

## 需求

1. 以 bounded repository evidence 記錄四欄 legacy composite 與二欄 typed contract 的差異。
2. 禁止 partial migration、dual-write、SDK Entity bridge、request-time fallback、猜選 Owner，及 timeout/
   ambiguous mutation retry。
3. 列出恢復條件：完整四欄 DTO allowlist、OptionSet valid-value policy、server authorization、同一
   deadline/idempotency、逐欄 read-back/reconciliation、task-owned cleanup、single rollback owner 與
   CE/host/parity evidence。
4. 更新 parent 的下一個候選；不得啟動 P7.5 或 P8。

## 驗收條件

- [ ] PRD、design、implement 與 CCG context 記錄上述證據與 no-go。
- [ ] source-only scope guard 證實沒有 runtime/configuration/CE/fixture/CRM 變更。
- [ ] task artifacts 通過 UTF-8 無 BOM、CRLF、final CRLF、`git diff --check`。
- [ ] CCG 限時分析結果或「雙模型未完成」降級原因已記錄。
- [ ] 完成 Trellis Check、scope-only commit、archive，parent 保留精確後續候選。

## 不在範圍

接入 ProductClient、擴充 Data8/OptionSet contract、啟用 feature gate、CE、流量切換、P7.5、P8、正式資料。
