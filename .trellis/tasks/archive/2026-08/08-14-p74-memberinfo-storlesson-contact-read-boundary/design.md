# 設計：MemberInfo 上課紀錄的安全授權邊界判定

## 決策

`ORG-CALL-00027` 採 **source-only local design no-go**。既有 typed StorLesson client／DTO
雖可重用，卻不能修復其呼叫前的 authorization authority。不得新增原先規劃的 sub-gate，也
不得將保留 legacy branch 的 partial wiring 視為安全的 Gateway migration。

## 既有資料流與缺口

```text
browser contactId
  -> EnsureCorrectUserData
       -> Session password / static validation cache / mutable InMemoryContext.ListManager
       -> 必要時以保存帳密 SetupListManager
  -> CanViewContact
       -> GetAccess (Session _MemberInfoAccess 或 InMemoryContext)
       -> Shepherd: GetShepherdContactIds -> EnsureShepherdListsLoaded
  -> typed StorLesson client composition / legacy ToolUtility path
```

這條流程的 `contactId` 只能是 locator，不能當 authority。然而在 locator target validation 前，
`GetAccess` 已讀取／寫入 Session，並從 shared `InMemoryContext` 推導 access；Shepherd scope 更可能
利用共享 ListManager 的保存 credential 載入 CRM 資料。這些可變、歷史狀態無法證明隸屬目前
authenticated principal、profile 或 runtime generation，違反 repository-wide isolation contract。

`EnsureCorrectUserData` 的存在不補足缺口：它是以 Session password 調和 legacy ListManager 的操作，
且會使用 static validation cache；它不是在所有 shared state、client composition 與 CRM I/O 前建立的
immutable server authorization scope。既有 `StorLessonQueryService` 的 typed DTO projection 因此不能
安全地接受 `CanViewContact` 成功後的 contact GUID 當成 Gateway 的 verified input。

## 外部審查的證據權重

限時 CCG architect run 在 45 秒內僅取得 Gemini 輸出，Gemini 結論為 `go-local-design`；Claude 無
usable output，故此輪是「雙模型未完成」，不是完整 dual-model analysis。Gemini 僅從 action 表面順序
推論 `EnsureCorrectUserData`／`CanViewContact` 已構成 server authorization，沒有追溯
`GetAccess`、`EnsureShepherdListsLoaded` 與 `BaseChurchController` 的 shared-state／credential 行為。
本 child 以可重現的本機完整 source trace 為準，維持 no-go；不得以不完整外部輸出覆寫原始碼事實。

## 未來允許的資料流

```text
authenticated principal
  -> server-derived immutable MemberInfo scope
  -> server-side Church / Shepherd target allowlist
  -> validated contact locator
  -> fixed bounded stor-lesson query
  -> request-local Data8 lease
  -> immutable DTO projection
  -> request-local view row
```

前置 authorization-boundary child 必須證明 scope 的完整隔離 boundary，並讓 profile／generation 的
解析屬於 deployment-owned Gateway composition。未來 StorLesson capability 只能接受 scope 已授權的
target locator；不得接受 profile、workload、query、connector、endpoint、credential、Entity 或既有
Session object。fixed query 必須有 response budget；DTO、diagnostics 與錯誤須去識別化。任何 missing、
ambiguous、timeout、fault、partial 或 cleanup failure 都 fail closed，不 fallback、不 retry。

## 相容性與 rollback

本 child 沒有 runtime、設定、資料或 CE 改動，既有 legacy route 維持不變，rollback 是 no-op。
它不提供 CE、host capacity、Embedded／Dedicated parity、traffic、P7.5 removal 或 P8 readiness evidence。

## 明確禁止

- 不可把 `GetAccess` Session cache、`InMemoryContext`、`CanViewContact` 結果或舊 allowed contact
  set 視為 Gateway authorization proof。
- 不可在前置 scope 未建立時新增 sub-gate、typed branch、Church-only route 或 shared cache。
- 不可讓 typed path fallback／retry 到 ToolUtility，或傳遞 `Entity`、`EntityCollection`、query、
  credential、profile、endpoint、raw exception 或可識別 contact diagnostics。
- 不可發出 CE request、修改 matrix／feature gate／traffic，或開始 P7.5／P8。
