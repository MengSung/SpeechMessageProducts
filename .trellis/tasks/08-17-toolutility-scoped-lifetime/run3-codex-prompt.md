Active task: .trellis/tasks/08-17-toolutility-scoped-lifetime

你要執行 **Run 3.0 — 遷移前的地形調查**。這是調查任務，**不准修改任何 `.cs`**。

原本的 `implement.md` 把 Run 3 寫成「分批遷移 35 個 `ToolUtilityFactory.GetInstance()` 呼叫點」。
外部驗證發現那份計畫少了一個前置條件，若照原計畫直接遷移，會重現前置任務的
`ObjectDisposedException` 與跨請求共用連線。你這一棒的工作就是把這件事查清楚並更新計畫。

## 0. 先讀（不可略過）

```
.trellis/tasks/08-17-toolutility-scoped-lifetime/prd.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/design.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/implement.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/notes.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/research/findings-scope-boundaries.md   ← Run 0 的同類調查，照這個格式寫
```

## 1. 現況（已由外部驗證，可直接採信）

- Run 0 / 1 / 1.5 / 2 皆已完成並 commit，工作樹乾淨（`.turns.json` 除外）。
- `ToolUtilityClass` 已是 **Scoped**，由 `ServiceCollectionExtensions.AddToolUtility()`
  以 factory lambda 註冊，連線由 DI 注入，`_ownsConnection = false` 時不釋放連線。
- `ToolUtilityFactory` 的 legacy 自建連線路徑仍在，供尚未遷移的呼叫點使用。
- `SpeechMessageProducts.ChurchReport` 內仍有 **39 處** `ToolUtilityFactory.GetInstance(...)`，
  分佈於 35 個檔案：WebServiceConnector 20、Tools 7、Models 6、ViewModels 1、Controllers 1。

## 2. 已發現的阻礙（你要驗證、量化並擴充，不是照抄）

`Models/InMemoryDataContextSmallGroup.cs` 目前仍把 **13 個物件**以
`GetCurrentSessionId() + "_<型別名>"` 為鍵，放進程序級 `IMemoryCache`，
絕對過期 30 分鐘、滑動過期 30 分鐘：

```
577  ListManager                 662  SmallGroupDataList        717  WeeklyReportData
771  NewPersonModel              825  PersonalInfomationModel   880  HappyGroupDataManager
934  ListManagementDataManager   989  EquipmentDataManager     1044  FeeList
1099 LineBindingViewModel       1153  AppointmentsListManager  1211  DonationPaymentManager
1267 PollManager
```

其中至少這條鏈已確認會**傳遞性持有** ToolUtility：

```
ListManager（session 快取 30 分鐘）
  └─ m_ListSmallGroupWeeklyReport : ListSmallGroupWeeklyReport   （ListManager.cs:53 欄位初始化式）
        └─ m_UploadIntegrateData : UploadIntegrateData           （ListSmallGroupWeeklyReport.cs:31）
              └─ m_ToolUtilityClass = ToolUtilityFactory.GetInstance(...)（UploadIntegrateData.Core.cs:34）
  └─ m_DownloadIntegrateData : DownloadIntegrateData             （ListManager.cs:60 欄位初始化式）
        └─ m_ToolUtilityClass = ToolUtilityFactory.GetInstance(...)（DownloadIntegrateData.Core.cs:37）
```

`ListManagementDataManager`、`EquipmentDataManager`、`DonationPaymentManager`、`PollManager`
本身就直接持有 `ToolUtilityFactory.GetInstance(...)`，而它們同時也在上面那份 session 快取清單裡。

**若照原 Run 3 計畫，把這些欄位改成建構式注入的 Scoped `ToolUtilityClass`**：

```
請求 1  建立 Scoped ToolUtilityClass → 塞進 ListManager → ListManager 存入 IMemoryCache 30 分鐘
請求 1 結束  DI 釋放 scope → ToolUtilityClass 被 Dispose、租約歸還連線池
請求 2（同 session，30 分鐘內）
        → 從快取取回 ListManager → 用到它欄位裡那個已釋放的 ToolUtilityClass
        → ObjectDisposedException，或用到一條已租給別人的連線
```

這正是 Run 1.5 為 `_ToolUtilityClass` 直接快取所消滅的模式，只是往下沉了一層。

## 3. 你要回答的問題

### Q1 逐一分類那 39 處呼叫點

對 35 個檔案的每一處，判定其**持有者的最長生命週期**，分成三類並給出檔案:行號與判定依據：

- **A 類 — 持有者是 per-request**：可安全改為建構式注入 Scoped ToolUtility
- **B 類 — 持有者被 session 快取／存活跨請求**：不可注入為欄位，需另行設計
- **C 類 — 無法判定**：寫「未確認」＋卡點，不要猜

判定要追到底：欄位持有者本身若被誰以欄位持有，就繼續往上追，直到抵達
Controller（per-request）或 `IMemoryCache`（跨請求）為止。把鏈寫出來。

### Q2 B 類的可行設計，以及各自代價

至少評估這兩個方向，各給出影響檔案數與風險，不要只寫一個：

- **方向 1：改為方法參數傳遞。** 專案裡已有這個既有形態可循，例如
  `ListSmallGroupWeeklyReport.SetPersonalReportViewModel(ref ToolUtilityClass, Entity)`
  （`Models/ListSmallGroupWeeklyReport.cs:154、189、208、248、275`）。
  長命物件不持有連線，改由呼叫端在每次呼叫時把當前 request 的 ToolUtility 傳進去。
- **方向 2：先移除 session 鍵快取**（比照 Run 1.5 對 `_ToolUtilityClass` 的做法），
  讓這些物件本身變成 request 範圍。要說明移除後哪些狀態會遺失、是否有讀取端依賴
  「跨請求保留」這個行為（`SetSessionDirtyFlag` 的讀取端已在 Run 1.5 查過為 0，
  但這 13 個物件的跨請求狀態不同，要重新查）。

### Q3 批次切法

依 Q1 的分類，重新提出 Run 2.5 / Run 3 的批次順序與每批的完成判定。
原 `implement.md` 的「3a Models / 3b Tools / 3c ViewModels / 3d WebServiceConnector / 3e 刪 Factory」
是按目錄切的，**如果 Q1 顯示同一目錄裡 A 類與 B 類混在一起，就要改成按生命週期切**，
並說明為什麼。

查 3 輪仍無結論的項目 → 寫「未確認」＋卡點，不要猜。

## 4. 允許修改的檔案

只有這兩個：

```
.trellis/tasks/08-17-toolutility-scoped-lifetime/research/findings-run3-holder-lifetimes.md   （新建）
.trellis/tasks/08-17-toolutility-scoped-lifetime/implement.md                                 （更新 Run 3 章節）
```

**不准修改任何 `.cs`。** 這一棒是調查，不是實作。
發現的任何程式碼問題寫進 findings 文件，絕不順手修。

## 5. 完成判定

```bash
git status --porcelain
```

除上述兩個檔案（與既有的 `.ccg/.../.turns.json`）外必須乾淨。

```bash
grep -c "" .trellis/tasks/08-17-toolutility-scoped-lifetime/research/findings-run3-holder-lifetimes.md
```

必須存在且非空。

內容判定：
- Q1 的分類表涵蓋全部 39 處，每一處都有 A/B/C 判定與持有鏈
- Q2 兩個方向都有影響檔案數與風險
- Q3 有具體批次順序與每批完成判定
- `implement.md` 的 Run 3 章節已依 Q3 更新

本 Run **免** G1～G4（沒有 `.cs` 改動）。但新增的 `.md` 要是 UTF-8 without BOM + CRLF。

## 6. commit

```
research(toolutility): 盤點 ToolUtility 持有者的生命週期與遷移批次
```

## 7. 明確不做

- 不要遷移任何 `ToolUtilityFactory.GetInstance()` 呼叫點
- 不要刪除 `ToolUtilityFactory`
- 不要動 `InMemoryDataContextSmallGroup` 的任何快取
- 不要碰明文密碼與憑證輪替
- 不要修那 22 個既有失敗的 Payments 測試
- 不要重新設計 `ToolUtilityClass` 的公開 API

## 8. 失敗處理程序

1. `git restore -- .trellis/tasks/08-17-toolutility-scoped-lifetime/implement.md`
2. `rm -f .trellis/tasks/08-17-toolutility-scoped-lifetime/research/findings-run3-holder-lifetimes.md`
3. 在 `notes.md` 記錄失敗原因與最後錯誤訊息
4. 標記 SKIPPED 並**停止**

## 9. 交付

在 `notes.md` 追加一節「Run 3.0 結果」，寫明：

- Q1 / Q2 / Q3 各自的結論摘要（詳細內容放 findings 文件）
- A 類 / B 類 / C 類各幾處
- 第 5 節每一道指令的**實際輸出原文**
- 若有「未確認」項目，逐一列出卡在哪裡
