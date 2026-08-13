# P7.4 MemberInfo 完整聯絡人頭像回應邊界

## 目標與使用者價值

將權威 matrix 的 `ORG-CALL-00028` 既有 `MemberInfoController.GetContactImage` 三種可見結果──
CRM `entityimage`、合法 LINE 圖片轉址、以及依 `gendercode` 產生的預設 SVG 頭像──收斂為一條
server-authorized、預設關閉、DTO-only 的完整 typed candidate。這會消除「typed image 沒有圖片時必須
回到 ToolUtility 查 LINE／性別」的切流障礙；本 child 只產出本機程式與驗證，絕不把成果誤稱為 CE、
實機切流、P7.5 或 P8 證據。

## 已確認事實

1. 舊 `GetContactImage` 在完成 `CanViewContact` 後以單次 CRM `Retrieve` 讀取 `entityimage`、
   `new_line_picture_url`、`gendercode`。有圖優先回 JPEG；沒有圖時，合法 HTTP(S) LINE URL 轉址；
   否則回傳 `DefaultAvatarSvg.ForGender`。這三種結果合起來才是既有端點語意。
2. 已封存的 `08-13-08-13-p74-package03-contact-image-read-boundary` 只建立 image-bytes 的獨立
   local route；它刻意不取代舊路由，且 matrix 仍是 `temporary-legacy`。它不是本 child 的重做目標。
3. 既有 `memberinfo.contact.retrieve.image` 只投影 image bytes，缺圖即 fail closed；不能在它的
   feature-on request 內用 legacy CRM 補 URL 或性別，否則會違反 P7.4 禁止 request-time fallback。
4. P7.3 connector、executor 與 ProductClient 已具備受限的 image DTO 基礎，但尚未有完整 display-result
   discriminator、固定三欄 projection、完整回應 service 或專屬 rollback gate。
5. `Package03SpecialResourcesEnabled` 與所有既有 P7.4 gates 均為 false；P7.4 aggregate capacity／
   non-overlap enablement audit 仍為 no-go。P7.2 Slice C 歷史 cycle 已 closed，絕不可重試。

## 需求

1. 新增一個精確且 server-owned 的 display-read capability；不得把 caller-selected entity、attribute、
   query、profile、workload、URL、owner、connector、CE version 或 CRM SDK type 送入 typed 邊界。
2. capability 必須以單次固定 contact projection 決定唯一結果：已驗證圖片優先；無圖片時只允許有界、
   無 user-info、HTTP(S) 的 LINE URL；其餘情況僅回傳可選 `gendercode` 純值供產品產生既有 SVG。
   不得傳遞 CRM `Entity`、`OptionSetValue`、stream、cache entry、raw response 或 raw exception。
3. 新增獨立 `Package03MemberInfoFullContactImageReadEnabled` sub-gate；只有它和 Package03 base gate
   都為 true 才可進入新路由。兩份 checked-in configuration 必須保持 false，且 false-gate 必須在
   GUID parse、session／scope、DI、ProductClient、cache、ToolUtility 或 outbound I/O 前停止。
4. 新 route 在 true-gate 時先驗證 server-side MemberInfo scope，再 parse browser locator，接著以
   `CanViewContact` 驗證精確 target，才可組成固定 profile/workload 的 typed client。它不得改動、
   重導或呼叫舊 `GetContactImage`，也不得有 retry 或 legacy fallback。
5. 新結果在每層都要 defensive copy；圖片 bytes、URL、avatar code、response model、exception 和
   cancellation token 僅可活在目前 request。取消必須原樣傳遞；fault、格式不符、空圖片或無效 URL
   都不得發布 partial result。
6. 圖片 result 可沿用既有 local thumbnail/fit 演算以保留可見尺寸語意，但新 route 不可讀寫
   `IMemoryCache`。只可設定 private HTTP cache header；不可建立跨使用者、跨 profile 或跨 generation
   的 server-side image cache。
7. 必須以 test-first 方式證明 closed union、固定三欄查詢、gate order、雙層 authorization、取消、
   image/redirect/avatar 三分支、A/B interleaving、防禦性複製，以及舊 route 不變。

## 驗收條件

- [ ] display capability 有獨立 Operation ID、registry policy、response discriminator、Data8 fixed
      projection、executor dispatch、ProductClient DTO 與 request-local ChurchReport service；所有層皆拒絕
      不完整、歧義或超限資料。
- [ ] 新路由的 false-gate 在任何 locator parse、authorization、client composition 或 I/O 前回傳固定
      404；true-gate 以 server scope → locator parse → target authorization → typed dispatch 的順序運作。
- [ ] 三個結果分支均不使用 ToolUtility／CRM SDK fallback：圖片只回安全 image bytes、轉址只回已驗證
      HTTP(S) URL、avatar 只由 allowlisted optional gender scalar 產生既有 SVG。
- [ ] focused tests、完整相關 tests、solution Release build、UTF-8 無 BOM／CRLF／final CRLF、
      `git diff --check`、scope 與 isolation/lifecycle review 都通過；外部雙模型最多等候 45 秒並如實記錄。
- [ ] 所有 gates、CE、fixture、traffic、P7.5、P8、Official Worker、push 與 PR 均未啟用或執行；
      authoritative archived matrix 不被本機 candidate 改寫。

## 不在範圍

- 修改既有 `GetContactImage`、`GetContactImagesBatch`、`PersonalController` 圖片路徑或其 legacy cache。
- 影像寫入、LINE profile 同步、CE read/mutation、feature enablement、capacity/drain 實機證據、
  ToolUtility removal、P7.5、P8 或雲端部署。
- 新增 server-side user-image cache、generic CRM proxy、DTO-to-Entity rehydration、request-time fallback
  或重試任何 timeout／ambiguous operation。
