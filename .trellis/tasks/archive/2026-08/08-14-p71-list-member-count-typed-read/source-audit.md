# ORG-CALL-00047 來源稽核

## 稽核範圍

本稽核僅讀取下列來源，不寫入 CRM、不執行 Gateway、Data8 或 ChurchReport runtime：

- `phase0-organization-call-matrix.json` 的 `ORG-CALL-00047`；
- `authoritative-gap-matrix.json` 的相同 call site；
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadListManager.cs`；
- `ToolUtility/ListOperations/ListService.cs`；
- `ToolUtility/Factory/ToolUtilityFactory.cs`。

## 追蹤結果

1. `DownloadListManager.GetListManager` 針對已取得的 list entity 呼叫
   `GetSmallGroupMemberNumber`，結果寫入週報畫面資料的 `TotalNumber` 與圖表總數。
   這是較大型的登入、可見名單、週報與 mutable collection workflow 中的一段，不是可由任意
   caller `listId` 直接操作的授權 API。
2. `GetSmallGroupMemberNumber` 在未傳入 operation-scoped service 時，會 fallback 到
   `m_ToolUtilityClass.m_Crm2011OrganizationService` 或 `m_OrganizationService`。該 service
   來自 `ToolUtilityFactory` shared instance；新 Gateway capability 不得保存、回用或依賴此狀態。
3. `ListService.RetrieveMemberListCore` 對靜態名單查詢 `listmember`；只有這個分支表面上能回傳
   一個 count，但它仍缺少獨立的 server-derived list authorization boundary。
4. `ListService.RetrieveDynamicMemberListCore` 讀取 CRM `list.query`，取得字串 FetchXML 後建立
   `FetchExpression` 執行。此 query 是資料內容，不是 registry 審核的 server-owned named template。
5. 因此不能把 `listId` 當作授權、不能由 caller 指定 static/dynamic，也不能將 stored FetchXML
   包進 Data8 executor。若只做 static branch，會靜默改變 dynamic-list legacy use case，並造成
   不完整遷移的錯誤宣稱。

## 結論

`ORG-CALL-00047` 在目前程式碼下為 **source-only local design no-go**。本 child 不可建立 runtime
DTO、registry、executor、ProductClient、consumer branch 或 CE evidence。這個結果只封鎖此 capability
的直接遷移，不封鎖其他具備完整 authorization boundary 的 P7 child。

## 重新評估的最低條件

1. 在任何 list 資料讀取前，先由已驗證 principal、伺服器持有的 policy 與完整 request-local scope
   推導可見名單集合；`listId` 僅能是該集合內的候選，不得自行形成 authority。
2. 靜態與動態名單須拆成明確的 capability。靜態分支可考慮固定 count contract；動態分支必須改成
   registry 審核的 server-owned named template，或繼續維持 temporary legacy。
3. ProductClient 只能接收有界、不可變 DTO，且不得持有 session、cache、lease、connector、CRM SDK
   物件或資料庫內的 query 字串。
4. 重新設計後必須先具備未授權拒絕、static/dynamic 分支、response bound、A/B 隔離、取消、fault
   eviction、lease drain 與 resource baseline 測試；之後才可建立獨立 child 評估 CE／host／traffic 證據。
