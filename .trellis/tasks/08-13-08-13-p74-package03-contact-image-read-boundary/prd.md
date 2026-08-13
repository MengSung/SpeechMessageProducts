# P7.4 Package03 聯絡人圖片唯讀邊界

## 目標與使用者價值

為 `ORG-CALL-00028` 建立一條新的、預設關閉的 Package03 聯絡人圖片唯讀讀取邊界。它讓後續正式遷移能以受伺服器驗證的身分、固定 deployment-owned profile/workload 與封閉 DTO 讀取已授權聯絡人的原始圖片，而不讓瀏覽器選擇 CRM、profile、connector 或身分。此 child 只交付本機可驗證的邊界，不能把本機結果宣稱為 CE 證據、現有路由切換、P7.5 ToolUtility removal 或 P8 Central Gateway readiness。

## 已確認事實

- 既有 `MemberInfoController.GetContactImage` 是 legacy 路由，除了 `entityimage` 之外還會輸出 LINE 頭像 redirect 或依性別產生 SVG avatar；它維持不變。
- `IPackage03SpecialResourceClient.RetrieveContactImageAsync` 已有固定 operation `memberinfo.contact.retrieve.image`，輸入只接受 profile、workload、contact Guid，輸出是 defensive-copy image bytes 與 media kind。
- 既有 `CanViewContact` 是目前唯一可重用的 server-side contact visibility 檢查；browser `contactId` 只能作 locator，不能當作授權。
- authoritative matrix 與 Package03 inventory 仍把 `ORG-CALL-00028` 視為 temporary-legacy；本 child 的 local contract 不得改寫或升格該狀態。
- 所有 deployment-owned feature gates 都必須維持 `false`；本 child 不送 CE request、不變更 CE 資料、不切流量。

## 需求

1. 建立獨立的新 HTTP GET 路由與 request-local service；不得修改、重導或呼叫既有 `GetContactImage`。
2. 新 gate `DynamicsAccess:Package03SpecialResourcesEnabled` 必須預設為 `false`。關閉時，route 必須在 GUID parse、`CanViewContact`、ProductClient、connector、cache、legacy CRM I/O 前以固定 404 停止。
3. gate 開啟時，route 必須先完成 `EnsureCorrectUserData` 與 server-side `GetAccess` scope 驗證；之後才 parse contact locator，再以 `CanViewContact` 完成精確目標授權、解析 request-local service 並呼叫 Package03 client。因 `CanViewContact` 的既有契約需要 Guid，不得錯誤宣稱它能在 parse 前執行。
4. typed path 只能使用 server-owned profile/workload 與 `RequestAborted`；不得接受 header、query string、session 或瀏覽器提供的 profile/workload/connector/CE version。
5. typed path 僅回傳已驗證的 image bytes 與封閉 content type；不得回傳 CRM SDK type、Entity、raw response、endpoint、credential、token、例外細節或跨 request cache。
6. typed no-image、typed failure 和取消不可 fallback 至 legacy CRM。`OperationCanceledException` 必須原樣傳播，不能進入一般 catch。
7. service/result 不得持有 Session、cache、connector、client、stream、timer、background work 或 mutable static state；每次 image getter/result 都必須維持 defensive-copy 與 request-local ownership。
8. 所有新增或實質修改的 C# 檔必須符合 AGENTS.md 的繁體中文文件、UTF-8 無 BOM、CRLF 與 final CRLF 要求。

## 驗收條件

- [ ] route 的 false gate 測試證明沒有 GUID parse、授權、DI client、legacy CRM 或 I/O。
- [ ] route 的 true gate 測試證明 server scope 授權先於 locator parse、精確目標授權在 parse 後且早於 typed dispatch，並且未授權請求沒有 typed dispatch。
- [ ] service 測試證明固定 profile/workload、取消 token 原樣傳遞、content type 映射、defensive-copy、A/B 交錯呼叫隔離與無 cache/fallback。
- [ ] source contract 測試鎖定：既有 `GetContactImage` 未變更、新路由不含 `GetConnection`/`ToolUtility`/`IOrganizationService`/`IMemoryCache`、取消不進 generic catch。
- [ ] targeted tests、ChurchReport Release tests、solution Release build、encoding/CRLF、`git diff --check` 與 scope check 通過。
- [ ] CCG analysis/review 透過 self-healing runner 嘗試且每次最多等候 45 秒；若降級，準確寫入 task 紀錄。
- [ ] feature gate、CE、流量、P7.5、P8、push 與 PR 均未變更或執行。

## 範圍外

- 既有圖片、LINE redirect、性別 avatar 的切換與完整 parity。
- 圖片寫入、CE fixture、任何 CE mutation、traffic enablement、Dedicated/Central deployment。
- 修改 immutable matrix、宣稱 `ORG-CALL-00028` 已 migrated，或啟動 P7.5/P8。
