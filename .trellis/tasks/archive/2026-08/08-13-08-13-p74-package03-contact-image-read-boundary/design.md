# P7.4 Package03 聯絡人圖片唯讀邊界設計

## 邊界

本 child 新增一條獨立的 `/MemberInfo/Package03ContactImage` GET 路由和一個 request-local service。它不是 `GetContactImage` 的替代實作，也不共享該 legacy route 的快取、LINE redirect 或 avatar fallback。

## 資料流與信任邊界

```text
HTTP request
  -> fixed false gate (404; no parse / authorization / DI / I/O)
  -> EnsureCorrectUserData
  -> GetAccess server-side scope authorization
  -> Guid parse as locator
  -> CanViewContact server-side authorization
  -> Package03ContactImageReadService
  -> fixed configuration profile + fixed workload
  -> IPackage03SpecialResourceClient.RetrieveContactImageAsync(RequestAborted)
  -> immutable defensive-copy result
  -> File(content bytes, closed content type)
```

`CanViewContact` 的既有 API 需要已解析的 Guid，因此「authorization-before-parse」必須拆成兩層：先以 `GetAccess` 驗證目前登入者的伺服器 scope，parse 後才以 `CanViewContact` 驗證精確目標。真正的資料邊界是兩層 server authorization 都必須在 ProductClient dispatch 前完成；locator 本身不能決定 visibility、profile 或 operation。false-gate 是更早的強制停止點，因此 gate 關閉時連 parse 都不發生。

## 設計決策

### Feature gate

新增 deployment-owned `DynamicsAccess:Package03SpecialResourcesEnabled=false`。route 直接從 `IConfiguration` 判斷；關閉時只回傳 `NotFound`，不解析輸入、不取得 service、不呼叫任何 legacy/typed 依賴。這讓 rollback 是單一設定切換，且不建立反向 fallback。

### 授權與固定組態

控制器在 true-gate path 執行 `EnsureCorrectUserData`，以 `GetAccess` 驗證 MemberInfo Church/Shepherd scope，接著 parse locator，最後以既有 `CanViewContact(contactId)` 驗證目前登入者。service 從組態讀取 deployment-owned `DynamicsAccess:ProfileAlias`，但不接受 caller profile；workload 永遠為編譯常數 `church-report-member-info-image-read`。空白 profile fail closed，不呼叫 client。

### DTO 與回應

service 僅處理 `ContactImageResult`，立即取得 defensive image copy，按 `ContactImageMediaKind` 映射既定 JPEG 或 PNG content type，並建立自己的 immutable result copy。它不使用 cache、SDK type、stream、legacy connector 或 fallback。controller 只把 result copy 傳給 `File`。

### 取消與失敗

`HttpContext.RequestAborted` 原樣傳給 service/client；`OperationCanceledException` 不被 controller 的 generic catch 捕捉。其他 typed failure 只回傳固定 404，且不含例外訊息、不回呼 legacy CRM。因為沒有寫入、cache 或背景資源，沒有 CE reconciliation 或 data cleanup；唯一暫存 image array 的 owner 是當前 request/result，離開 action 後由 managed memory 回收。

## 相容性與 rollback

`GetContactImage` 完全保持 legacy 行為。新 route 沒有現有 UI consumer，且 gate 初始為 false，故部署後沒有 traffic 行為改變。將 gate 維持/改回 false 即停止新 route；無資料 mutation、cache 或 process/resource owner 要清理。這不是 CE、parity 或 cutover evidence。

## 測試設計

- service unit tests：固定 request、content type、defensive copy、A/B 交錯、取消與 no-image/fault。
- source contract tests：false-gate order、授權/dispatch order、legacy isolation、無 forbidden types/cache/fallback、取消 catch rule。
- configuration tests：base/development 皆為 false。
- solution tests/build：確認新 C# 型別與既有 runtime 整合沒有破壞。
