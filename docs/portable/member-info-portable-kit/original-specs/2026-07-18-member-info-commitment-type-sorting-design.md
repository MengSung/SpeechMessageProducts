# 會友資訊依委身類型客製化順序排序 — 設計規格

日期：2026-07-18  
分支：`Sunny_5.1.2.WorktreeTuneMemberView`  
核准方案：方案 A（Dynamics metadata rank＋分段遠端分頁）

## 1. 目的與修正

會友資訊三種表格必須依 Dynamics 365／Dataverse
`contact.customertypecode` 在系統客製化畫面中的選項排列順序顯示。

原設計把 OptionSet 的原始整數值視為順序，這與實際需求不符。整數值是資料識別碼，
不是畫面順位。例如「牧師師母」可以是 `100000006`，但 metadata 把它列在第一位時，
它就必須是預設排序的第一類。

因此本設計明確禁止：

- 依 `customertypecode` 整數遞增／遞減推定系統順位。
- 依本地化中文標籤排序。
- 使用 FetchXML `useraworderby="true"`。
- 在程式內硬編碼「牧師師母、區牧……」等教會專屬順序。

## 2. 唯一權威順序來源

以 `RetrieveAttributeRequest` 取得：

```text
EntityLogicalName = contact
LogicalName       = customertypecode
RetrieveAsIfPublished = true
```

回傳的 `PicklistAttributeMetadata.OptionSet.Options` 集合順序就是排序來源。
依集合索引建立從 0 開始的 rank：

```text
第一個 metadata option  → rank 0
第二個 metadata option  → rank 1
……
```

Microsoft SDK 的 `OrderOptionRequest.Values` 定義為「依期望順序排列的選項值陣列」，
說明客製化順序本身是一個獨立序列，不等同選項值大小。

metadata 結果使用應用程式共用 `IMemoryCache` 快取；快取只包含 schema metadata，
不含使用者或會友個資。應用程式重啟會立即重新讀取；存活期間採有限期限快取，
避免每列或每頁重複呼叫 metadata API。

## 3. 元件邊界

### 3.1 Metadata provider

新增會友資訊專用 provider，職責只有：

1. 讀取 `contact.customertypecode` metadata。
2. 保留 `OptionSet.Options` 原始集合順序。
3. 輸出 `Value`、本地化 `Label`、`Order`。
4. 透過共用記憶體快取避免重複查詢。

不直接修改既有、被多個舊模組使用的 `OptionSetMetadataService`，避免為本功能擴大
Big5／舊編碼檔案及全站 OptionSet 行為的變更範圍。

### 3.2 共用排序與分段規則

`MemberInfoCommitmentTypeSort` 改為處理 metadata rank，而不是原始值。

本機列的固定分類順序：

1. metadata 中已設定的選項。
2. 有 OptionSet 值、但目前 metadata 找不到的舊值。
3. 真正未填寫的空值。

第一類依 rank 正向或反向；第二、三類的位置不因方向改變。同類型內一律依
`FullName`（Ordinal）、`ContactId`（OrdinalIgnoreCase）遞增。

### 3.3 DTO

`GroupMemberRowViewModel` 提供：

```text
MembershipStatus: string
MembershipStatusOrder: int?
HasMembershipStatusValue: bool
```

- `MembershipStatus`：使用者看到的中文標籤。
- `MembershipStatusOrder`：metadata rank；不是 OptionSet 原始值。
- `HasMembershipStatusValue`：讓本機排序區分未知舊值與真正空白。

不把原始整數值新增成可見欄位。

## 4. 三種表格的資料流程

### 4.1 一般小組

1. CRM 查詢仍依現有權限及在籍條件取得會友。
2. 批次完成授權及關係目標資料。
3. `BuildMemberRows()` 使用同一次 metadata snapshot 設定顯示文字、rank 與 has-value。
4. 共用排序器依分類、rank、姓名、ContactId 排序。
5. JSON 送至共用 DataGrid。

### 4.2 搜尋結果

1. 維持現有搜尋、批次授權與 ContactId 去重。
2. 完成授權及去重後才套用同一個共用排序器。
3. 搜尋結果直接取代原表格時，欄位及排序行為與一般小組一致。

### 4.3 無小組遠端分頁

無小組不能把整個教會載入記憶體後排序，也不能先取一頁再排序。

伺服器依下列流程組成全域頁面：

1. 建立現有嚴格在籍、搜尋、授權範圍及「不在任何目前小組」的 base query。
2. 取得 metadata 中依設定順序排列的 option values。
3. 以安全的 SDK query 轉換及 FetchXML aggregate，取得各非空 OptionSet 值的筆數。
4. 另取得真正 null 的筆數。
5. 依 metadata values 建立已設定選項分段；未出現在 metadata 的非空值合併成
   「未知舊值」分段；null 為最後分段。
6. 反向排序只反轉已設定選項分段，未知及 null 仍固定在最後兩區。
7. 依 `skip/take` 計算目前頁面跨到哪些分段。
8. 每個命中分段只查詢所需範圍，段內依 `fullname`、`contactid` 排序。
9. 合併後回傳 `data` 及所有分段總和的 `totalCount`。

這個流程確保跨頁順序正確，且不會把所有無小組會友載入應用程式。

## 5. DataGrid 排序契約

可見欄位仍是：

```text
dataField: MembershipStatus
caption: 會員身份
```

排序 selector 改為：

```text
MembershipStatusOrder
```

- 一般小組／搜尋結果：方向感知的本機 sort-value 函式。
- 無小組：把 `MembershipStatusOrder` selector 傳給 Controller，由分段分頁處理。
- 預設 `sortOrder: 'asc'`、`sortIndex: 0`。
- 反覆點擊同一表頭切換 metadata 正／反順序。
- 本機函式依目前方向替未知與空白提供不同 sentinel，使兩者在正反向都維持：
  已設定選項 → 未知舊值 → 空白。
- 不增加第十個可見欄位。

## 6. 錯誤與相容策略

- metadata 暫時無法取得時，不使用 raw value 或硬編碼清單假裝是正確順序。
- 有 OptionSet 值的會友仍保留並依姓名、ContactId 穩定排列；真正空白仍最後。
- metadata 未列出的舊值不得被過濾掉，統一置於已設定選項之後。
- 搜尋、在籍、結案、授權與 grouped-id 排除條件完全沿用現有契約。
- 不修改 CRM metadata 或任何資料。

## 7. 測試設計

### 7.1 Provider

- metadata 順序為 `100000006, 1, 100000000` 時，rank 必須為 `0, 1, 2`。
- 證明「牧師師母」可因 metadata rank 0 排第一，而不是因數值。
- 快取命中不重複執行 metadata request。
- 無 label、無 value 及 metadata 失敗有明確相容行為。

### 7.2 共用排序

- 正向及反向只反轉已設定 rank。
- 同 rank 依姓名及 ContactId。
- 未知值位於已設定值之後。
- null 永遠最後。
- 跨多個 metadata 分段的 `skip/take` 不遺漏、不重複。

### 7.3 Controller／搜尋

- 一般小組及搜尋結果使用 `MembershipStatusOrder`。
- 無小組在 CRM 分頁前按 metadata 分段。
- Aggregate counts、未知分段、null 分段與 totalCount 正確。
- 原始值排序、`useraworderby` 與舊 selector 均不存在。

### 7.4 View

- 中文 `MembershipStatus` 仍是唯一可見會員身份欄。
- 本機及遠端 selector 都使用 `MembershipStatusOrder`。
- 正反方向下未知及空白 sentinel 都固定置底。
- 既有九欄、固定頭像／姓名、單一水平捲軸、觸控滑動及搜尋返回契約不變。

## 8. 交付與驗收

自動驗證必須包含：

- focused RED → GREEN 測試。
- 完整 `ChurchReport.MemberInfo.Tests`。
- `ChurchReport` 與測試專案 Build。
- Razor JavaScript 語法檢查。
- 所有本任務文字檔 UTF-8 驗證。
- `git diff --check` 與範圍稽核。

最後由使用者在 VS 2026 實測：

1. 預設第一類為系統客製化畫面的第一個選項（目前為「牧師師母」）。
2. 點擊會員身份表頭可正／反切換。
3. 一般小組、搜尋結果與無小組三處一致。
4. 無小組跨 25／50 筆分頁無遺漏或重複。
5. 中文標籤、手機滑動及既有版面無回歸。

使用者確認前不 Commit、merge 或 push。

## 9. 參考依據

- Microsoft Learn — `OrderOptionRequest`：
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.messages.orderoptionrequest>
- Microsoft Learn — `OrderOptionRequest.Values`：
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.messages.orderoptionrequest.values>
- Microsoft Learn — `OptionSetMetadata.Options`：
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.metadata.optionsetmetadata.options>
