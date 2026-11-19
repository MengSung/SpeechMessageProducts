# P8-01b 技術設計

## 邊界與資料流

伺服器端既有登入／授權解析器提供當次操作的 church ID；ChurchReport 使用該 ID 與目前借入的 `IOrganizationService` 呼叫 Core `OfferingCategoryReader`，得到最多 200 筆、單教會、正式 ID 唯一且不可變排序的快照。組裝器只將啟用類別投影成表單選項（資料庫 ID 為 value、類別名稱為 text），不保存跨請求快取。

POST 的 category ID 只視為不可信識別輸入。服務端從同一請求的授權教會讀取最新清單，要求 ID 存在且啟用，並確認必填舊選項值存在；其後以該伺服器讀回的類別資料寫 lookup 與 legacy option，不從瀏覽器接收類別名稱、教會 ID 或舊 option 值。收費單與認獻預約各自的建單路徑須共用此驗證／映射，失敗時在任何 Create 前拒絕。

既有資料讀取先解析 `new_offeringcategoryid` lookup 並查得其類別名稱；lookup 缺漏或不可用時以 `FormattedValues` 舊選項標籤回退，再使用目前語境中的通用奉獻文字。類別文字不經整數對照表。保留不同資料庫 ID 的同名記錄。

## 相容與故障策略

- 有效教會清單存在時優先使用；若該教會尚無類別，依 08A 精確回退 OptionSet metadata。metadata 查詢失敗要傳遞／依既有錯誤入口處理，不使用自製固定名稱清單。
- 必填 legacy option 不可空；不符合時在 CRM Create 前拒絕，不留下部分記錄。
- lookup 指向別教會、重複正式 ID、類別缺欄、清單超限、未識別／停用提交均 fail closed。
- 查詢結果、表單模型及建單候選皆為單次操作區域資料；不建立 static、singleton 或 MemoryCache 保存教會資料。SDK 是呼叫端借入並持有，本功能不 Dispose。

## 測試策略

以 08A 規定 11 個固定名稱測試覆蓋優先序、教會隔離、來源碼對照移除、未知標籤 fallback、UI 選項／預設、空清單 metadata 相容、雙欄寫入、必填分支、跨教會拒絕及訊息解析。用合成 SDK／記憶體替身故障注入，不連線任何組織。另跑 ChurchReport、Core 測試、I18nCheck、文件與位元組檢查，以及完整 P8 guard。
