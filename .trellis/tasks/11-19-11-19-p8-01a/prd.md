# P8-01a 奉獻類別結構與轉換指令

## 目標

依 08A §4.1 與 §5 的 P8-01a 單元，新增奉獻類別查閱欄、離線可驗證的轉換 plan/apply/verify 指令，以及 GoodShepherd.Core 的不可變類別型別。此單元不連接任何 D365 組織；合成 SDK 測試不得讀取秘密或冒充實機結果。

## 需求與限制

- `d365/schema/speechmessagecore.schema.json` 的 `activePhase` 設為 8，並為既有 `new_fee`、`new_dedication_booking` 實體分別新增指向 `new_offeringcategory` 的 optional lookup `new_offeringcategoryid`，phase 為 8。
- Deployment 提供 `migrate-offering-categories --org <組織>`、含 `--apply --plan <檔> --plan-sha256 <雜湊>` 的套用形式，以及 `--verify`。plan 使用組織中繼資料標籤、類別代碼 `legacy-<選項值>`、依教會與選項值分組的計數；套用須驗證雜湊與規劃基準未漂移、先 Upsert 類別、再以每批 100 筆回填；已有查閱值者跳過，批次結果只寫入操作專屬的安全紀錄。
- 組織狀態、選項標籤、選項 RequiredLevel 及組織目前資料筆數未經授權連線查證；不得以 repo fixture 或假服務結果描述為實機事實。此缺口不得阻擋明確離線測試及其餘實作。
- 「目前畫面提供的選項」以 Deployment 內部固定清單表達，只用於 plan 對應，不供 ChurchReport 消費；依選項標籤精確對應，不合併不同選項值，未對應標籤列入 plan。
- `OfferingCategory` 為不可變值；`OfferingCategoryList` 以非空穩定 ID 驗證唯一性、保留同名不同 ID，排序依 `SortOrder` 再依代碼，並只暴露唯讀集合。
- `OfferingCategoryReader` 以伺服器傳入的教會 ID 讀取，最多 200 筆、超量拒絕、不快取；`OfferingCategoryText` 依查閱名稱、舊選項格式化標籤、通用字典文字的順序選擇顯示內容。
- 不新增 csproj，不擴大範圍至 ChurchReport 接線或任何 live 操作，不降低既有分析器、守門或安全限制。

## 必需測試

`SpeechMessage.Dynamics.Deployment.Tests`：

- `Plan_GroupsByChurchAndOptionValue`
- `Plan_SimilarNames_NotMerged`
- `Apply_TwiceIdempotent`
- `Apply_ResumeAfterInterruption`
- `Plan_NamesFromOrganizationMetadata_NotCodeMaps`
- `Plan_IncludesCurrentlyOfferedOptions`
- `Plan_RecordWithoutChurch_CountedNotFilled`
- `Apply_PlanHashOrOrganizationChanged_Rejected`
- `Apply_BatchesOf100_ExecuteMultiple`
- `Verify_OptionWithoutLookup_CountedPerChurch`
- `Verify_LookupToOtherChurchCategory_Reported`
- `Schema_OfferingLookups_PlannedForFeeAndDedicationBooking`

`SpeechMessageProducts.GoodShepherd.Core.Tests`（net48 與 net10.0）：

- `List_SameNameDifferentIds_BothKept`
- `List_DuplicateId_PublicationRejected`
- `List_OrderedBySortOrderThenCode`
- `Reader_MoreThan200_Rejected`

## 驗收條件

- 上列 16 個同名測試均存在，先以未實作狀態證實紅燈，完成後 Deployment Release 測試與 Core 兩個目標的 Release 測試均通過。
- Schema 定義、plan/apply/verify、批次與中斷續跑皆由合成資料和 SDK 替身驗證；無外部組織連線。
- 新增／實質修改的 C# 檔含完整繁體中文檔首說明、成員 XML 註解及繁體中文測試契約；UTF-8 無 BOM、CRLF、檔尾 CRLF。
- 更新 Deployment CLI help 與相關 `.trellis/spec`；JSON 檔伴隨說明維護於 `docs/file-purpose-companions.json`。
- 完成指定單元提交、`C:\smv\guard-p8` 完整 §8.1 守門、一頁回報與驗收記錄；首次推送 P8 分支並以 `git ls-remote` 核對 SHA，之後更新現行 3.5 進度頁 DATA。
