# P7.1 名單成員計數強型別唯讀能力：來源稽核 no-go

## 目標與使用者價值

針對權威矩陣的 `ORG-CALL-00047`（`list.members.count.by.listid`）完成可追溯的來源稽核，判定既有 ChurchReport 名單成員計數是否能在不破壞跨使用者隔離、授權邊界與既有動態名單語意的前提下，成為 Gateway 的強型別唯讀能力。

本 child 的可交付成果是精確的安全決策與恢復條件；它不是 runtime 遷移、consumer cutover 或 CE 測試任務。

## 已確認事實

- 權威矩陣把 `ORG-CALL-00047` 定義為 `list.members.count.by.listid` 唯讀 capability；它目前仍是 `temporary-legacy`，CE、host、rollout 與 rollback 證據均未完成。
- `DownloadListManager.GetSmallGroupMemberNumber` 的輸入在既有 `GetListManager` mutable workflow 中產生；該 workflow 使用登入帳密、可見名單及 operation-scoped service 可選 fallback，並非獨立的 request-local、server-authorized list capability。
- 靜態名單以 `listmember` 查詢計數；動態名單則先讀取 CRM `list.query`，再將其中儲存的 FetchXML 直接送回 CRM 執行。
- CRM 資料內的 dynamic FetchXML 不是 server-owned named template。把它交給 Data8／Gateway 執行，會讓資料中的查詢定義跨越 registry、authorization 與輸入控制邊界。
- 既有 fallback 會走 `ToolUtilityFactory` 的 shared instance 與 CRM service；它不得成為新的 ProductClient 授權來源、transport fallback 或 state bridge。

## 需求

1. 將本 child 定義為 source-only local design no-go；不得修改 production runtime code、feature gate、matrix migration state、CE、流量、P7.5 或 P8。
2. 明確區分靜態名單與動態名單，禁止以「只支援靜態名單」或隱藏動態分支來宣稱完成既有 legacy use case。
3. 禁止接受或傳遞 caller-supplied `listId` 作為唯一授權、static/dynamic 類型、FetchXML、OData、QueryExpression、Entity、EntityCollection、profile、connector、endpoint、credential 或 continuation。
4. 記錄未來重新評估前必須先具備的 server-derived authorization boundary 與 dynamic-list 安全設計。
5. 以 task-owned Trellis／CCG 紀錄保存決策，完成相稱的本機檢查、scope-only commit 與 archive。

## 驗收條件

- [ ] `source-audit.md` 能對應矩陣、legacy caller、static/dynamic query 分支與 shared fallback 的原始來源。
- [ ] `design.md` 明確說明為何本 child 不可實作 runtime capability，以及禁止事項與恢復條件。
- [ ] `implement.md` 僅規劃 task-record、檢查、審查、commit、archive；不包含 production code 或 CE 行動。
- [ ] `implement.jsonl` 與 `check.jsonl` 只含真實、相關的 spec／研究參考，沒有 seed example。
- [ ] CCG task 紀錄含需求、範圍、風險、分析與 review 結果。
- [ ] 檢查證明沒有 runtime、matrix、gate、CE、traffic、P7.5 或 P8 變更。

## 非目標

- 不建立 partial static-only Gateway capability。
- 不遷移 `GetListManager`、`DownloadListManager`、`ToolUtility` 或既有 session workflow。
- 不建立或執行 CE fixture、nonce、ledger、preflight、mutation、read-back 或 cleanup。
- 不啟用 feature gate、不切換 ChurchReport 流量、不宣稱 consumer migration、ToolUtility removal、P7.5-ready 或 P8 readiness。
