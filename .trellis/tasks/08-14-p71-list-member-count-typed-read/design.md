# 設計：以 fail-closed 保護名單計數遷移邊界

## 決策

本 child 採取「不實作」的設計決策。這不是功能降級，而是拒絕把未驗證的 caller 名單識別、shared
ToolUtility state 或 CRM 儲存的 FetchXML 變成 Gateway 的 authority 與 executable input。

## 被拒絕的資料流

```text
browser/session/mutable list workflow
  -> listId
  -> Gateway or ProductClient
  -> CRM list.query FetchXML
  -> RetrieveMultiple
```

這條資料流同時缺少 server-derived authorization，並允許資料庫內容定義 executable query。它無法證明
不同使用者、profile 或 request 之間不會重用 state，也無法把 dynamic list 安全收斂為固定 operation。

## 未來允許的資料流

```text
authenticated request
  -> server-derived list authorization scope
  -> fixed capability branch
  -> bounded immutable request DTO
  -> server-owned fixed query/template
  -> bounded count-only response DTO
```

此資料流必須先由另一個專屬 child 建立授權範圍。它不得使用 Session、InMemoryContext、shared singleton、
caller profile、connector、credential 或 stored FetchXML 當 authority。任何未授權、缺 query、dynamic template
未登錄、逾時、transport fault、部分結果或不確定狀態都必須 fail closed，不得 fallback 到 ToolUtility。

## 相容性與回滾

既有 `DownloadListManager` 不變，代表現有 ChurchReport 行為不被 partial migration 改寫。因本 child 沒有
runtime code、gate、CE 或資料變更，所以回滾是 no-op；commit/archive 的唯一作用是保存此次安全決策。

## 明確禁止

- 不可把 static-only count 宣稱為完整 legacy migration。
- 不可讓 ProductClient 接收 raw CRM object、query、endpoint、credential、profile 或 connector。
- 不可把 dynamic `list.query` 當成可執行模板。
- 不可增加 legacy fallback、cache bridge、feature enablement、CE evidence、traffic cutover、P7.5 removal 或 P8 work。
