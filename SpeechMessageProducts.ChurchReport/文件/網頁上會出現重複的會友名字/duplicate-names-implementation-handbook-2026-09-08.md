# 小組回報重複會友姓名：實作、取證與驗收手冊

- 日期：2026-09-08；適用分支：`Jesus.5.2.3.FixDuplicateName.Worktree`。
- 程式基準：`6cb4b489dc84571609bb7bd80c00cf5f26fc34b2`。
- 本手冊已依目前完成的產品修正更新；程式建置與自動化測試已通過，但正式 CRM 資料查核、實機 LINE WebView 與跨教會部署仍須依本手冊逐站執行。
- 先讀同目錄 [實作計劃](duplicate-names-implementation-plan-2026-09-08.md)。
- 架構契約：repo root 的 `.trellis/tasks/09-08-fix-duplicate-member-names/design.md`；需求：同任務 `prd.md`。

## 1. 先釐清目前程式與舊報告的差異

兩份 2026-09-07 文件來自其他分支，是研究輸入；保留原檔不覆寫。

| 舊報告說法 | 2026-09-08 現況 | 實作要點 |
|---|---|---|
| 整合載入完全無鎖 | ListManager Ensure/Setup 已有 m_DetailLoadGate | 找沒走鎖的 reader/writer；不能把同 instance 兩個 Setup 同時 Add 當成已證明根因。 |
| HTML 通常已完成全載入 | 有 PrepareIntegrateFirstPage 延遲明細路徑 | 新人/小組/chart 的詳細載入順序一起修，保留首屏效能。 |
| 排序穩定、相鄰即插入相鄰 | 目前 List.Sort 原地排序 | 不用畫面成對順序推論執行緒時序。 |
| new loader 可隔離所有依賴 | loader 的 ToolUtilityFactory.GetInstance 是 singleton | 注入 operation-owned CRM lease，不能改共享身份。 |
| 只需載入改 snapshot | CRUD/Save/轉組還會改共享 Member/List | 所有讀寫都接入同一 generation 契約，回傳 detached 資料。 |

目前證據支持「有資料一致性缺口」，但尚不能判定截圖一定來自 server、CRM 或 DOM。

## 2. 準備與工作順序

在目前 repo root 開 PowerShell，先記錄：

```powershell
git status --short
git branch --show-current
git rev-parse HEAD
dotnet --info
```

沿用現有 worktree。先讀完整被修改檔案與 Trellis backend/frontend 索引。測試用既有 `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`；`ChurchReport.Tests/` 沒有 csproj，不能當測試命令目標。

依序完成：P0-A 診斷 → P0-B 協調/holder → P0-C 所有 API/草稿/儲存接線。CRM 遷移 P1-D 與資產 P1-E 有獨立 gate，最後 R-F 整合驗收。詳細檔案與每步 checkbox 在實作計劃。

不要以 FullName/手機/單獨 ContactId 去重；不要將例外吞成成功空表；不要把 client timeout 當上傳成功。

## 3. 瀏覽器取證（只讀）

使用受控測試帳號，打開小組頁 DevTools：

1. Network 開 Preserve log，先保持實際快取條件；重現時記錄瀏覽器/LINE/OS、時間、頁面日期、小組別名及 app instance。
2. 篩選 `LoadIntegrate`、`LoadNewPersonFollowUp`、`GetChartDataList`，保存原始 response/status/headers/時間。chart 的 WeeklyReportId 目前傳的是 ListId。
3. 原始 HAR/姓名/CRM id/cookie 留在存取受控的本機證據目錄，禁止提交 Git；分享前刪 credentials、cookies、PII。
4. 在 Console 執行以下片段，只輸出計數，不列姓名/id：

```javascript
(() => {
  const normalizedKey = value => String(value ?? '').trim().toLowerCase();
  const summarize = keys => {
    const counts = new Map();
    for (const key of keys.map(normalizedKey)) counts.set(key, (counts.get(key) || 0) + 1);
    return { count: keys.length, empty: counts.get('') || 0,
      duplicateKeys: [...counts.values()].filter(n => n > 1).length };
  };
  for (const id of ['SmallGroupgridContainer', 'NewPersonGridContainer']) {
    const element = document.getElementById(id);
    if (!element) continue;
    const grid = $(element).dxDataGrid('instance');
    if (!grid) continue;
    const items = grid.getDataSource().items();
    const visible = grid.getVisibleRows().filter(row => row.rowType === 'data');
    console.log(id, {
      dataSource: summarize(items.map(row => row.PresentRecordId)),
      visible: summarize(visible.map(row => row.key)),
      domDataRows: element.querySelectorAll('.dx-datagrid-rowsview .dx-data-row').length
    });
  }
  console.log('DevExtreme', window.DevExpress?.VERSION);
  console.log('assetUrls', performance.getEntriesByType('resource')
    .filter(x => /dx\.(all|aspnet|common|light)/.test(x.name)).map(x => x.name));
})();
```

`items()` 只代表已載入/目前頁，不當成全量 JSON；若 grouping 啟用，先改為未分組或遞迴展開，不能把 group header 當人員。固定欄位/虛擬列可能有鏡像 DOM，不能拿全 DOM count 當全資料筆數；比較同一可視範圍與 row key。

原始 Network response 可複製為本機變數後查 keys，務必不將含個資的 JSON 貼進 Git：

```javascript
// 將 response 原樣貼在 DevTools 本機變數 responseBody；本行不發出新 HTTP 請求。
const rows = Array.isArray(responseBody) ? responseBody : responseBody.data;
if (!Array.isArray(rows)) throw new Error('先確認 response 格式與 grouping 設定');
const keys = rows.map(row => String(row.PresentRecordId || '').toLowerCase());
console.log({ count: rows.length, unique: new Set(keys).size, empty: keys.filter(x => !x).length });
```

| 結果 | 調查方向 |
|---|---|
| JSON 同 PresentRecordId 重複 | 組裝重複/查詢放大/共享寫入；相同 primary id 不等於兩筆 CRM 實體。 |
| JSON 同 Contact、不同 PresentRecordId | CRM 候選業務重複，需再核對 List/week/事件類型；同名本身不足以判重。 |
| JSON 唯一、DataSource 重複 | reload/store/key/版本與 response 順序。 |
| JSON/DataSource 唯一、可視 DOM 重複 | virtual rendering；用 standard A/B 對照，仍需保留原始證據。 |
| 只有 date/小組切換時出現 | requested scope 與 published scope/generation 比較。 |

## 4. 實作操作卡：P0 核心

### 4.1 先固定 scope

從驗證的 Contact、組織、登入 epoch、可見 List、日期/週次、WeeklyReportId、scopeVersion 生成 key；日曆沿用 SundayCalculator/WeeklyScheduleProvider。日期改變與小組切換都增加版本，舊 tab 應收到明確 409 或由 client 忽略舊 response。

每 request 固定一個 holder lease，不重複 Get/check/Set 取 manager。ListId 的授權不能因 cache 命中而省略。

### 4.2 發布完整資料

同 key 同世代等待同一 build Task；只有 winner 執行載入。獨立 candidate/loader 與 CRM lease，完整驗證後設 LoadFlag=true，最後交換整個 snapshot envelope。失敗不發布、清除 faulted in-flight 並允许重試；新 key 失敗不能回舊 key 的資料。

目前 SDK async extension 仍同步 I/O，不新增每 request Task.Run。request cancellation 只 detach，build lifetime 自己管理；同步 SDK 到 timeout/完成前不得提早歸還連線。

### 4.3 所有寫入也一起改

- SmallGroup Crud + NewPerson：同一 mutation 命令處理 row，驗證 key/欄位/expectedGeneration；移除平行 PopulateObject。
- SaveIntegrate：移除 fire-and-forget，await 真正完成；傳入值副本，不捕獲 Controller/共享 report/憑證長期存活。client 按鈕等待、timeout 待查，不當作成功。
- 日期、多小組、登入、Personal、Equipment 旁路全部導向核心；舊 operation 不得清掉新頁資料。
- Razor adapter、DataSourceLoader 與 JSON 使用 detached Member/Chart values，不能只複製 List 容器。
- 無週報用穩定 draft key；與 CRM GUID 分開解析，所有 CRUD/upload 一起改。

### 4.4 生命週期驗收

專用 holder store 有容量/TTL；active request/build 的 lease 結束後才回收。cache eviction 不直接 Dispose 有等待者的 semaphore，也不允許同 scope 舊 writer 未結束就再建第二個 holder。metadata 與圖表/小組名稱業務 cache 分開，業務資料先授權再命中且使用 detached values。

## 5. CRM 只讀查核與遷移手冊

### 5.1 只讀報表

使用 CRM 管理員提供的授權環境/API，沿用既有安全連線，不從 appsettings/日誌取密碼。限定異常小組/週次，逐頁匯出：

- `new_present_recordid`、`new_contact_new_present_record`、`new_list_new_present_record`；
- `new_group_present_weekly_report_prese`、日期/事件類型、statecode、createdon/modifiedon；
- 出席/關懷內容與必要關聯（僅存受控原始匯出）。

父表 `new_group_present_weekly_report` 同時查同 List/週次是否 >1。建立來源 include app/QR/processor/plugin/Power Automate；別只查 upload。

匯出可正規化成 `WeeklyReportId,ContactId,ListId,WeekStartDate,StateCode,PresentRecordId` 欄位後，在受控本機執行：

```powershell
$rows = Import-Csv -LiteralPath (Join-Path $env:TEMP 'duplicate-names-evidence/present-records-normalized.csv')
$byBusiness = @($rows | Group-Object ListId,WeekStartDate,ContactId | Where-Object Count -gt 1)
$byPrimary = @($rows | Group-Object PresentRecordId | Where-Object Count -gt 1)
[pscustomobject]@{ Rows=$rows.Count; BusinessGroups=$byBusiness.Count; RepeatedPrimaryGroups=$byPrimary.Count }
```

這只是候選清單，不是自動合併命令；核對 QR 多事件與無小組個人回報是否不同業務事件。

### 5.2 平台鍵與部署順序

建議文字鍵使用 `sg_{listId:N}_{weekStart:yyyyMMdd}_{contactId:N}`，主檔用 `wr_{listId:N}_{weekStart:yyyyMMdd}`。格式用底線；冒號會影響 alternate-key GET/PATCH/Upsert。欄位 schema 名稱由 CRM solution 實際定義並納入變更單，不硬猜現有欄位。

1. sandbox 完成全部 writer 清冊與重送測試；CRM owner 確認事件唯一性。
2. 正式作業前匯出/備份、建立 old-id→canonical-id/欄位合併/關聯映射、檢查回復演練。
3. 暫停相關 app/plugin/flow 寫入；再次全量分頁掃描，避免清理期間插入新重複。
4. 依核准映射處理重複主檔及明細，保留衝突欄位與稽核。只把重複列 inactive 不代表索引能成功：Alternate Key 不自動只約束 active。
5. 若採新文字欄位，canonical active 列填非空 key；退休列依映射清空/改 retirement key。NULL 不受唯一性約束，所有 active writer 必須保證非空；阻擋退休列任意重新啟用。
6. 檢查全部被索引 rows 無重複，再建 Alternate Key，等 `EntityKeyIndexStatus=Active`；Pending/InProgress/Failed 都不能啟用。
7. 部署共同 Upsert/明確 patch writer；Count>1 產生衝突而非繼續新增。補建重送不能用 false/0/空白覆寫既有資料；週報主檔也防重。
8. sandbox/受控驗收確認後恢復寫入、分頁對帳。原本 GetPresentRecord 缺記錄的 Create 也需遷移，不能因叫 Download 就漏掉。

本輪不執行上述正式變更。平台唯一性與 application rollback 分开，平常回復不刪唯一鍵；必要時停止寫入並用映射/備份還原。

## 6. 資產與 LINE WebView

```powershell
rg -l 'dx\.all\.js|dx\.aspnet\.mvc\.js|dx\.aspnet\.data\.js|dx\.common\.css|dx\.light\.compact\.css' ChurchReport/Views -g '*.cshtml'
rg -n 'DevExtreme|TargetFramework' ChurchReport/ChurchReport.csproj
Get-Content ChurchReport/wwwroot/js/devextreme/dx.all.js -TotalCount 8
```

只讀 header，不要整份輸出 minified JS。清冊至少記錄 wrapper 23.1.5、runtime 22.1.6、AspNet.Data 5.1.0、實際 JS helper/CSS/header/asset URL。選擇相容套件組合；data helper/package 不必與 wrapper 同號。

所有直接引用頁面已列在計劃（Layout、LoginResources、Authentication、Dedication、Home 等）。本地 script/CSS 用 asp-append-version；實際檢查 URL hash 與 response cache header，動態 no-store 不與靜態長快取互相污染。

pageshow.persisted 時驗證身分/scope，沒有 dirty draft 才 reload DataSource；有草稿先保留/提示，避免無條件整頁刷新遺失內容。handler 只註冊一次，處理 401/403/409 的 UI 與三個 DataSource 同步更新。實機 iOS/Android LINE 需測，桌面窄螢幕不能取代 LINE。

## 7. 測試矩陣與執行

| Case | 操作/注入 | 必須結果 |
|---|---|---|
| C01 | fake builder barrier，100 concurrent 同 key | 一次 build、同 generation、完整 rows |
| C02 | A 慢/B 切日期/A 後完成 | 舊 key 不發布，B 不被覆寫 |
| C03 | winner/waiter request aborted | 其他等待者成功；無 CTS/lease leak |
| C04 | CRM 例外/timeout 後重試 | 無半成品，fault Task 清除、可重試 |
| C05 | 不同 session/user/organization、A logout B login | 無 identity/report/cache 污染 |
| C06 | newperson 先到、deferred 詳細尚未完成 | 不回空 placeholder 當成功、不漏新人 |
| C07 | Grid 編輯+refresh+Save+轉組交錯 | 草稿保留、只改同 key/generation |
| C08 | 無週報/draft key、同名不同人 | key 穩定、各人保留、CRUD 正確 |
| C09 | TTL eviction 持有 lease、store 滿 | 不 dispose 活躍 gate、不新增重複 holder；容量受控 |
| C10 | CRM 相同命令十次、雙 instance | 一個 canonical，欄位不被重試預設值覆蓋 |
| C11 | 主檔/明細已有 >1、inactive 相同 key | 不再新增，報衝突，索引前處理所有列 |
| C12 | scroll 50 次、bfcache、日期、LINE 實機 | 三層 keys 同範圍一致，草稿不丟 |

相關測試命令（未來實作後執行）：

```powershell
dotnet restore ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
dotnet build ChurchReport/ChurchReport.csproj -c Debug --no-restore
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~SmallGroup' --logger 'trx;LogFileName=duplicate-names.trx'
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj -c Debug --no-restore --logger 'trx;LogFileName=memberinfo-full.trx'
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj -c Debug
git diff --check
```

搭配 `ChurchReport/文件/測試手冊/前台兩層回歸測試手冊.md` 的 Tier 1 與 trigger matrix Tier 2；使用 `前台回歸測試紀錄範本.md`。案例標記 PASS/FAIL/BLOCKED/N/A；CRM/手機不可用則 BLOCKED，不能引用舊 PASS。

效能：相同 fake dataset 5 分鐘暖機、15 分鐘壓力、60 分鐘 soak；100 concurrent × 100 generations。记录 P50/P95/P99、CRM calls、allocation、ThreadPool queue、active leases/holders/CTS。P95/calls 超基準 10% 需分析；drain 後 active 操作與 lease/waiter/registration=0，TTL 後 idle holder=0，反覆批次無 retained memory 趨勢。RetainVM reserved/working set 不要求精確歸零。

## 8. 上線、回復與記錄

只對通過 Gate A–F 的已驗證版本 canary；診斷/A-B旗標可控，但不能以 flag 恢復已知錯 scope/半成品/無界背景工作。CRM key 不隨產品 rollback 刪除。發現資料串用即停止放量並停用受影響入口，保留去識別診斷。

每次記錄：commit/build、時間、browser/OS、instance、測試資料別名、cases 狀態、duplicate/build/lease 指標、未決事項、回復版本。原始 PII/CRM ids/HAR 在受控位置，Git 只放摘要。

## 9. 目前版本已完成的程式實作對照

以下項目已在 ChurchReport 完成，可直接作為移植到其他教會或 SpeechMessageProducts 的基準：

1. `ListManager.EnsureAndGetIntegrateDetachedRead` 在同一個 `m_DetailLoadGate` 內完成可見名單授權、報表載入、`ListEntityId` 精確比對，以及 `Member`／`ChartData` 深複製；鎖外只使用 detached 陣列。
2. `LoadIntegrate`、`GetChartDataList` 與新人跟進資料 API 使用同一個 detached read 邊界，不再把共享報表集合交給 DevExtreme 序列化。
3. `IntegrateReportPublicationValidator` 只驗證每個 grid 的穩定 row key（通常為 `PresentRecordId`），不以 `FullName`、手機或單獨 `ContactId` 合併資料。兩位同名且 row key 不同的會友必須同時存在。
4. 帳號密碼與 LINE ID 登入流程都先固定目前登入者、組織、日期與可見名單範圍；LINE 流程不再把 LINE User ID 當成小組 `ListEntityId`。
5. `SaveIntegrate` 與小組 CRUD 不再使用未受控的 Fire-and-Forget、`Task.Run` 或同集合平行寫入；request 結束前會完成或回報失敗，避免 Session、CRM client、Task、closure 長期存活。
6. 奉獻收費清單與奉獻稽核在找不到登入 contact 時 fail closed，清除敏感欄位並回傳空結果，不沿用上一位登入者的 contact。

## 10. 移植到其他教會或 SpeechMessageProducts 的具體步驟

### 10.1 先建立產品對照表

移植前把下列名稱替換成目標產品的實際型別；不要直接複製類別名稱後略過生命週期設計：

| 本專案概念 | 目標產品必須提供的對應物件 | 驗收要求 |
|---|---|---|
| `ListManager` | 每位登入者／租戶可取得的 holder | 不可把可變 holder 做成跨使用者 singleton |
| `m_DetailLoadGate` | `SemaphoreSlim` 或等價 per-scope gate | 載入、發布、深複製在同一 gate；`Dispose` 有明確 owner |
| `ListEntityId` | 教會小組、事工或名單的唯一 ID | 每個 API 都驗證目前登入者確實可見 |
| `PresentRecordId` | 出席／事件資料列的穩定 row key | 同 grid 內唯一；不可退回姓名當 key |
| `IntegrateDetachedRead` | DTO／immutable snapshot | 不含 Entity、CRM client、Session 或可變共享集合 |
| `UploadIntegrateDataAsync` | 真正可等待的非同步 writer | 完成、取消、timeout、例外都有明確結果 |

### 10.2 實作順序

1. 先建立 scope key：組織、登入 contact、登入世代、名單、日期、週報 ID。任何一項改變都必須視為新 scope。
2. 建立 operation-owned CRM connector；每次載入使用區域 connector，於 `finally` 歸還 connection。禁止把 connector 放入 Session、static 或長壽命 cache。
3. 在 gate 內載入區域 candidate，完成 header、Member、新人、chart 與 row key 驗證後，才一次交換公開 snapshot。
4. 建立 detached DTO，逐列複製每個純值欄位；`ToArray()` 只能複製容器，不能取代 Member 深複製。
5. 將所有讀取 API、編輯 API、儲存 API、日期切換、LINE callback 接到同一個 scope／generation 契約。
6. 將 CRM 寫入改為 alternate key + Upsert 或等價冪等命令；同一業務鍵出現多筆時回報衝突，不自動以姓名刪除。
7. 加入同名、跨登入、並行載入、取消、timeout、重試與 lease drain 測試，再進行 sandbox 部署。

### 10.3 每個教會的部署檢查

- 匯出該教會的名單、週報、出席資料筆數與候選業務重複；先人工核對同名是否為不同 Contact。
- 確認 CRM 欄位 schema、alternate key、plugin／Flow／Power Automate writer 清冊與停用／恢復順序。
- 確認所有教會頁面使用同一組 DevExtreme runtime 資產，並啟用內容版本化，避免舊 WebView 使用舊 JavaScript。
- 以至少兩個帳號及一個 LINE ID 同時登入，交錯切換小組、日期、奉獻清單與稽核頁，確認不會讀到別人的姓名或 contact。
- 完成 Gate C01–C12 後才開放正式流量；任何 session/report 串用、重複 row key 或 active lease 未歸零都必須阻止上線。

## 11. 文件完成與尚未驗證

本版本已完成產品程式修正、Debug build、完整 568 項測試，以及小組相關 51 項測試。尚未由本環境代替各教會執行正式 CRM 清理、alternate key 建立、壓測、實機 LINE WebView 與正式流量 canary；這些是部署手冊中的必要現場步驟，不能以本機測試代替。

## 官方參考

- [Dataverse Alternate Key](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/define-alternate-keys-entity)
- [NULL/唯一性與索引生效限制](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/define-alternate-keys-reference-records)
- [Upsert 行為](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/use-upsert-insert-update-record)
