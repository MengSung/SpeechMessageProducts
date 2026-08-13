# P7.4 MemberInfo 完整聯絡人頭像回應邊界設計

## 設計決策

採用「新的精確 display capability + 新的關閉預設 route」，不改寫既有 image-only capability 或舊
`GetContactImage`。這讓目前 image-only local route 仍維持其狹窄契約，也讓完整三分支回應能在所有資料
由同一次固定 Data8 `Retrieve` 投影後決定，避免 feature-on request 重新觸發 ToolUtility fallback。

完整 display operation 是 `memberinfo.contact.retrieve.image.display`。它仍只對應 `ORG-CALL-00028` 的
同一個使用者可見讀取能力；新增 operation 是因為原 operation 的「必有圖片」response contract 不能安全
表示 LINE redirect 或 avatar。它不是 generic contact read，也不改寫 immutable 70-row baseline。

## 資料流、信任邊界與生命週期

```text
HTTP /MemberInfo/Package03FullContactImage
  -> deployment-owned base + display sub-gate (兩者 false 時 404)
  -> EnsureCorrectUserData + GetAccess (server scope)
  -> browser GUID 僅作 locator
  -> CanViewContact(contactId) (server target authorization)
  -> fixed profile + fixed workload
  -> IPackage03SpecialResourceClient display request
  -> Data8 single fixed Retrieve(contact: entityimage, line URL, gender)
  -> closed display union
  -> request-local service / controller
     -> image thumbnail / image file
     -> verified redirect
     -> existing default SVG
```

1. gate 是第一個 executable decision。base/sub 任一 false 時，route 不解析 locator、不讀 session、
   不做 scope 驗證、不解析 DI client、不觸及 cache、ToolUtility 或 CRM。
2. `EnsureCorrectUserData`／`GetAccess` 先驗證登入者的 server scope；`CanViewContact` 因既有 API 需要
   `Guid`，只能在 locator parse 後驗證精確 target。兩段 authorization 都必須發生在 typed client 前。
3. 新 ProductClient、Data8 executor、connector lease 與 transport 都由既有 DI/process-host ownership
   管理。display client、ChurchReport service 和 controller 不 Dispose client，不保留 `HttpContext`、
   principal、lease、stream、cache、timer、CTS 或 background work。
4. SDK `Entity`、`OptionSetValue`、image decoder、temporary bytes 與 CRM response 都只留在 connector
   synchronous scope。closed union 只保留 defensive-copied PNG/JPEG bytes、bounded validated URL，或
   optional integer gender scalar。每個 getter／mapping 都產生 caller-owned copy。
5. `OperationCanceledException` 不進 generic catch；所有其他 typed fault 只能回傳固定 404。沒有
   retry、legacy fallback 或 partial publish。下游 executor 對 timeout/cancel/fault 的 lease eviction／dispose
   是唯一資源 cleanup 路徑。

## Closed display union

| Kind | 必要資料 | 禁止資料 | 發佈條件 |
| --- | --- | --- | --- |
| `Image` | non-empty、已驗證 PNG/JPEG copied bytes 與 closed media kind | URL、gender、Entity、stream | `entityimage` 存在且通過 byte/format/dimension/pixel limit。 |
| `LineRedirect` | bounded HTTP(S) absolute URL，沒有 username/password | image、gender、raw CRM string 以外資料 | 沒有 image，且固定 LINE 欄位通過 URL/UTF-8/size validation。 |
| `DefaultAvatar` | optional `gendercode` 整數純值 | image、URL、OptionSetValue | 沒有 image，且 URL 缺失或不合法；保留既有 `ForGender` neutral fallback。 |

`Image` 優先權高於 URL 與 gender；`LineRedirect` 優先權高於 avatar。每一個 factory 都驗證唯一 kind
與對應唯一 branch，constructor 立即複製資料；不允許 null/多重 branch。URL 以 connector 的獨立
pure validation 限制 UTF-8 bytes、絕對 URI、HTTP(S)、無 user-info；產品 route 可再次使用同樣封閉
validator，避免 header injection 或不一致 redirect。

## 新 route 與相容性

新增 `/MemberInfo/Package03FullContactImage`，接收與舊 route 相同的 `contactId`、`size`、`fit`
純 locator／顯示參數。它不呼叫 `GetContactImage`，不使用 `IMemoryCache`，不讀取 CRM SDK。image branch
可使用現有 `CreateThumbnailIfNeeded`／`CreateFitThumbnail` 的 pure local transform，保留 `size <= 0`
原圖和 `32..256` clamp 語意。所有結果設定 `private` response caching header，沒有 server retained cache。

redirect branch 只把已驗證 URL 交給 MVC redirect result；avatar branch 只使用既有 `DefaultAvatarSvg.ForGender`
純函數。任何 typed fault/no-go 都回 fixed 404，不回 legacy path。舊 route 留在原狀，因此 checked-in
gate=false 時完全沒有流量改變；rollback 為保持或設回 display sub-gate=false。

## 測試與交付邊界

先新增 failing tests，之後才寫 production code：

1. Abstraction/registry test：operation、response union 和 branch defensive-copy/invalid data fail-closed。
2. Data8 test：固定三欄 ColumnSet、image-first priority、URL validation、avatar fallback 及不合法 SDK types。
3. ProductClient/service test：固定 profile/workload、exact cancellation forwarding、A/B interleaving，與
   response branch isolation。
4. Controller source/contract test：base/sub false order、scope/locator/target authorization order、無
   forbidden legacy/cache/fallback symbol、image/redirect/avatar output，及 legacy route 未改。

本 child 的 verification 結果僅可表示 local-disabled candidate。CE 9.1 execution、Embedded/Dedicated
parity、capacity/non-overlap、soak/drain/rollback、P7.5 removal 與 P8 皆仍是後續各自的證據工作。
