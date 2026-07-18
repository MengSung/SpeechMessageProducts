# MemberInfo 區長／小組／會友樹狀功能需求

## 權威來源

- 設計：`docs/superpowers/specs/2026-07-15-member-info-district-group-tree-design.md`
- 原計畫：`docs/superpowers/plans/2026-07-15-member-info-district-group-tree.md`
- 會員身份 metadata 排序設計：`docs/superpowers/specs/2026-07-18-member-info-commitment-type-sorting-design.md`
- 會員身份 metadata 排序計畫：`docs/superpowers/plans/2026-07-18-member-info-commitment-type-sorting.md`
- 本文件以設計為最高優先，修正原計畫中已確認的契約與效能矛盾。
- 早期 10 欄／搜尋展開等歷史描述已由 portable kit 的 `01-INTEGRATED-SPEC.md` 最終九欄與結果替換契約取代；本文件保留它們只供 lineage，不得覆蓋後期明確修正。

## 功能需求

1. 將會友資訊從平面 DataGrid 改為區長 → 小組 → 會友三層樹狀。
2. 區節點顯示區名、區長、跨所屬小組去重後的在籍非結案人數。
3. 小組節點顯示小組名、小組長及組內去重後的在籍非結案人數。
4. 區預設展開；小組預設收合；整列可點且有大三角提示。
5. 範圍內只有一個小組時自動展開該組，不受無小組節點是否存在影響。
6. 第三層點開後才載入 10 欄：頭像、姓名、性別、生日、手機、信仰狀態、地址、會員身份、關係、目標。
7. 成員回應不得包含圖片 bytes；畫面完成後沿用 `GetContactImagesBatch` 補頭像。
8. 姓名沿用 `openMemberInfoDetailPopup`；上傳新照片後同步更新樹內縮圖。
9. 關係／目標以單一批次 connection 查詢取得，兩欄同序、去重且索引對齊。
10. 全教會一律包含 `Ungrouped` 節點（可為 0 人），預設收合；牧養範圍不得出現。
11. 無小組必須在 CRM 端先套在籍、排除結案、搜尋、排除已分組、排序及 PageInfo，再只對當頁執行授權／關係／欄位建構。
12. 搜尋欄限定姓名、手機、會員身份；後端 `SearchDistrictTree` 回傳含命中成員的小組 ID 與無小組命中狀態，前端不得只比對區／組標題。
13. 搜尋結果自動展開目前頁的命中小組；清空搜尋恢復完整樹與預設展開狀態。
14. 每頁最多 50 個小組；跨頁重複區標題；無小組只在最後頁（或沒有任何小組時）顯示。
15. 保留全教會的重新同步 LINE；移除顯示照片／顯示全部切換。
16. 手機 header 至少 44px 可點、可換行；按鈕維護 `aria-expanded`；CRM 文字以 `textContent` 輸出防 XSS。

## 資料契約

- `DistrictTreeViewModel`：`Districts`、`Ungrouped`（全教會為物件、牧養為 null）、`Scope`。
- `UngroupedNodeViewModel`：`MemberCount`。
- `MemberInfoTreeSearchResultViewModel`：`MatchingListIds`、`HasUngrouped`。
- District／Group／Member DTO 一律 PascalCase；前端一律讀 PascalCase。
- `LoadGroupMembers` 的 DevExtreme 外層 envelope 可保留 `data`，其中每列欄位為 PascalCase。
- `LoadUngroupedMembers` 的 DevExtreme envelope 保留 `data`／`totalCount`，其中每列欄位為 PascalCase。
- 既有頭像與 LINE 匿名 API 的 `success`／`images`／`sources`／`ids` 維持小寫，不套用 Tree DTO 規則。

## 權限與安全

1. Church 與 Shepherd 都必須用伺服器算出的有效小組名單集合驗證 requested listId；Church 不得放行任意非空 GUID。
2. 有效小組條件：active、`purpose="小組名單"`、`new_app_named=true`。
3. Shepherd 可見集合為有效小組集合與登入者 `ListEntityId` 集合的交集。
4. Group、Search、Ungrouped 的 contact 都再次通過 chunked `CanViewContactsBatch`。
5. 越權 list、非 Church 存取 Ungrouped、未知 access 一律 403。
6. 使用者專屬 Shepherd 骨架與搜尋結果不得進共用快取。
7. 新功能解析不到「結案」 OptionSet 值時不得 fail-open；應回錯誤而非包含結案者。

## 快取與效能

- 初次只載入不含個資及頭像的骨架。
- Church 完整骨架與 Church grouped ID snapshot 可共用快取 3 分鐘；搜尋結果不快取。
- Shepherd 骨架即時計算。
- `IN`／授權／connection 查詢需分塊並支援 PagingCookie。
- 更新會員身份時清除舊平面清單及新 tree/grouped snapshot 快取。
- `customertypecode` metadata 成功結果採共用有限期快取，暫時失敗只短期快取空結果；快取只含 schema，不含會友資料。
- 無小組依會員身份排序時，先 aggregate 計算 value counts 與 null count，再依 Configured／Unknown／Empty segments 將全域 skip/take 切成必要 slices；禁止載入全教會後記憶體排序。

## 2026-07-18 會員身份 metadata 排序補充

1. `PicklistAttributeMetadata.OptionSet.Options` 的集合位置是 `contact.customertypecode` 唯一權威順序。
2. 禁止依 raw OptionSet 整數、中文 label、Sunny 硬編碼清單或 FetchXML `useraworderby` 排序。
3. DTO 保留可見 `MembershipStatus`，另提供 `MembershipStatusOrder` 與 `HasMembershipStatusValue`；不輸出可見 raw value 欄。
4. 排序分類固定為 metadata Configured、metadata 未知舊值 Unknown、真正空白 Empty；正反向只反轉 Configured，後兩類仍置底。
5. 同類型依 `FullName`、`ContactId` 穩定排序。
6. 一般小組與搜尋結果必須在 contact 批次授權後排序；搜尋先依 allowed IDs 過濾與去重。
7. 無小組先依現有 filters 計數／分段，再做遠端 paging；每個 segment 內只依 fullname／contactid。
8. 可見欄位仍顯示目標 CRM label；local sort 讀 rank／has-value，remote selector 使用 `MembershipStatusOrder`。
9. metadata 暫時失敗不得改用 raw value 冒充順序；應保留安全資料與診斷，並將該 capability 標為未完成或核准降級。

## 已確認的原計畫修正

- 修正 Tree PascalCase／lower camel 混用。
- 新增後端 `SearchDistrictTree`，不採只比對樹標題的前端搜尋。
- 無小組改成真正先分頁後建 row，不採 `DataSourceLoader` 對全部資料記憶體分頁。
- `IsListAllowed` 對 Church 也要求 authoritative visible list set。
- DTO 採設計文件的 nested `Ungrouped`，不採互相矛盾的 `HasUngrouped/UngroupedCount` tree shape。
- 單組自動展開不再受 Ungrouped 阻擋。

## 驗收

- 新增純邏輯測試先紅後綠；現有 45 tests 不回歸。
- `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj` 全綠。
- `dotnet build ChurchReport.sln -c Debug` 成功。
- 靜態 View contract 證明舊 grid／photo filter 已移除、新 endpoints 與 PascalCase 10 欄存在。
- Provider／sort／count query tests 證明非數值 metadata 順序、Unknown／Empty、正反向與跨 segment slices；Controller／View contracts 證明目前三種 grid 不使用 raw ordering。
- 在真實 Dynamics 資料以 raw value 與 configured position 不一致的 option 驗證三種表格；Ungrouped 25／50／100 分頁不得重複或遺漏。
- Git diff 僅含本需求列出的 controller、services、view models、views、tests 與 CCG 任務檔。

## 外部分析狀態

- Gemini wrapper：HTTP 403 `餘額不足`，含指定 `gemini-2.5-flash` 重試仍相同。
- Claude wrapper：修正 wrapper 的空 `--setting-sources` 後，CLI 明確回覆 OAuth session expired。
- 因兩個外部帳戶皆不可用，改由三個獨立 Codex 子代理分別完成後端、前端與測試稽核；三方對四個 blocker 結論一致。
