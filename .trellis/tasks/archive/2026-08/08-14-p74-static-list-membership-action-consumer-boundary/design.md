# P7.4 靜態名單成員動作消費端邊界設計

## 決策

本 child 的結果是 **consumer migration no-go**。`list.members.add.many` 與
`list.members.remove.one` 的底層 ProductClient contract 雖已存在，卻不是現有
`ListManagementDataManager` business operation 的完整替代品。直接將兩個呼叫改為 typed client，會令
一個請求內同時經由 Gateway 寫入 membership，並經由 ToolUtility 讀寫 contact/primary list/attendance。

這是一個不可接受的 split-brain composite：每一邊可能成功或失敗，既有程式沒有 single transaction、
共同 read-back、同一 deadline、逆序 compensating cleanup 或單一 rollback owner。因此不得引入 partial
Gateway path、request-time fallback 或 dual-write。

## 已考慮方案

1. **只替換 add/remove action：拒絕。** 表面變更最小，但會混合兩個 mutation transport，無法安全
   read-back 或 rollback。
2. **將整個小組移轉 workflow 做成一個 typed composite：未來獨立 P7.2 capability family。** 這是可行
   方向，但必須先完成 server authorization、所有讀/寫子步驟的 fixed DTO contract、冪等、read-back、
   reconciliation 和 deterministic cleanup，不屬於本 child。
3. **保持 legacy 並記錄缺口：採用。** 不改變任何使用者行為或外部資料，準確維持 matrix 的
   `temporary-legacy` / `consumer-not-migrated`，同時讓 P7.4 繼續處理獨立 capability。

## 安全、隔離與資源生命週期

此 child 不建立 ProductClient、host、Data8 pool、WCF channel、CRM service、timer、background task、
credential、session 或 CE request。證據僅來自 bounded repository source inspection；不輸出 CRM ID、
名稱、endpoint、token、例外原文或任何使用者資料。

未來 composite child 的每一 request 都必須保持 identity/profile/request-local，不得重用 mutable
`Entity`、`EntityCollection`、service 或 cancellation registration；在 timeout、cancellation、partial result
或 unknown outcome 時 fail closed，不得重送寫入。cleanup 只能針對 task-owned fixture 的 exact ID，且要以
已知 reverse order 做 read-back 證實。

## Rollback 與後續

本 child 沒有 runtime mutation；rollback 為不採用任何未提交的 consumer runtime change。本紀錄的
rollback owner 是 P7.4 capability owner。下一個 child 應選擇可獨立、DTO-only、具 server authorization
且不混合 legacy mutation 的 matrix capability；`memberinfo.contact.update.basic.info` 是優先再評估候選。
