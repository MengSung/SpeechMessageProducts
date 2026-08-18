Active task: .trellis/tasks/08-17-toolutility-scoped-lifetime

你要執行 **Run 3-A — 遷移 A 類呼叫點，並結束本任務**。

**這是本任務的最後一個 Run。** 使用者已決定把終點線畫在 A 類：19 個 B 類呼叫點不遷移，
改為明確記錄為已知殘留並另開票。**你不准提出、規劃或執行任何新的 Run。**
做完第 3 節的清單就收尾，不要再往下長。

前置：Run 2.5a 必須已完成並 commit。若 `git status` 還有 Run 2.5a 的未提交改動，先停下來回報。

## 0. 先讀（不可略過）

```
.trellis/tasks/08-17-toolutility-scoped-lifetime/prd.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/design.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/implement.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/notes.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/research/findings-run3-holder-lifetimes.md
```

## 1. 為什麼在這裡收斂（背景，不是要你重新評估）

Run 2.5a 之後剩 26 個可執行呼叫點：**A 類 7 個、B 類 19 個**。

19 個 B 類全部卡在同一個原因：`Models/InMemoryDataContextSmallGroup.cs` 把 13 個有狀態的
manager 以 SessionId 為鍵放進程序級 `IMemoryCache`，存活 30 分鐘。那 13 個快取是本任務
**撞上的既有架構問題**，不是本任務造成的；在本任務內處理它會動到使用者可見的表單狀態，
應自成一張票。

因此：A 類遷移完 → 修訂 PRD 驗收標準 → 記錄殘留 → 開後續票 → 結案。

## 2. A 類 7 個呼叫點與各自的建立者（已查證）

| # | 呼叫點 | 建立者（皆 per-request） |
|---|---|---|
| 1 | `Tools/DonationFeePaymentProcessor.cs:119` | `Payments/DonationPaymentProductWorkflowDispatcher.cs:78`（scoped）**已傳入 `_toolUtilityProvider`**；:119 是舊程式用的無參數 fallback |
| 2 | `Tools/RecurringDonationPaymentProcessor.cs:91` | `Payments/DonationPaymentProductWorkflowDispatcher.cs:92`（scoped），目前只傳 `_lineNotificationWorkflow` |
| 3 | `Tools/QrCodeUtility.cs:50` | `Controllers/QrCodeController.cs:95`、`Controllers/PhoneBindingController.cs:129` |
| 4 | `Tools/SmallGroupQrCodeUtility.cs:52` | `Controllers/QrCodeController.cs:264` |
| 5 | `Tools/SundayQrCodeUtility.cs:38` | `Controllers/QrCodeController.cs:339` |
| 6 | `Tools/PersonalQrCodeUtility.cs:39` | `Controllers/QrCodeController.cs:417` |
| 7 | `ViewModels/GalleryViewModel.cs:47` | **MVC model binding**，見第 2.2 節 |

### 2.1 一般做法（#2～#6）

移除欄位初始化式的 `ToolUtilityFactory.GetInstance(...)`，改由建構式接收
`ToolUtilityClass`（或 `IToolUtilityProvider`，與同檔既有形態一致者優先），
由 Controller / Dispatcher 把自己那份 request scope 的實例傳下去。

Controller 已由 DI 管理，`BaseChurchController` 有 `ToolUtility` 可用，取得來源無虞。

**house form —— 照抄 `Tools/DonationFeePaymentProcessor.cs:145-162` 的既有形態**：

```csharp
private readonly ToolUtilityClass m_ToolUtilityClass;

public XxxUtility(IToolUtilityProvider toolUtilityProvider)
{
    if (toolUtilityProvider == null)
        throw new ArgumentNullException(nameof(toolUtilityProvider));

    m_ToolUtilityClass = toolUtilityProvider.GetToolUtility();
}
```

`ToolUtilityClass` 現已註冊為 Scoped，因此直接注入 `ToolUtilityClass` 亦可。
**同一個檔案內採用哪一種，以該檔既有形態為準，不要在同一檔混用。**

**`"DYNAMICS365-9.0"` 這個引數可以安全丟棄**，已查證：
`m_DiscoveryServiceType` 在 `ToolUtilityClass.Core.cs:37` 宣告、`:124` 寫入，
**整個 ToolUtility 組件無任何讀取點**；`InitializeCrmConnection()` 是由
`CrmConnection:Organization` 設定組出 URL，與該引數無關。
因此 `GetInstance("DYNAMICS365-9.0")` → `GetToolUtility()` 不會遺失任何行為。
把這條查證結果寫進 `notes.md`。

> ⚠️ 上述欄位寫法**只適用於 A 類**（持有者生命週期 ≤ 一個 request）。
> 19 個 B 類的持有者被 session 快取 30 分鐘，把 scoped 實例存成欄位就是製造
> `ObjectDisposedException`。**不准把這個形態套用到任何 B 類。**

**#1 特別處理**：先確認 `new DonationFeePaymentProcessor()` 無參數多載**還有沒有呼叫者**
（該檔 `:99`、`:270` 的註解說「舊程式仍可能直接 new」）。
- 若已無呼叫者 → 刪除該無參數多載，連同 `:119` 的 `GetInstance` 一起移除
- 若仍有呼叫者 → 把那些呼叫端改為傳入 provider，再刪多載
- 兩者皆不可行 → 保留並在 `notes.md` 寫明原因與呼叫點

### 2.2 `GalleryViewModel` 不能用建構式注入

它是 MVC model binding 的 action 參數
（`Controllers/AuthenticationController/AuthenticationController.Login.cs:66`
`ProcessLogin(GalleryViewModel aGalleryViewModel)`），由框架以無參數建構式產生，
**加建構式參數會讓 model binding 失效**。

它在 13 處使用 `m_ToolUtilityClass`（`:81, 92, 94, 105, 111, 112, 113, 114, 116, 123, 192`，
其中 `:129`、`:158` 還取 `m_ToolUtilityClass.m_Crm2011OrganizationService`）。

**做法**：移除該欄位，把使用它的方法改為接收 `ToolUtilityClass` 參數，
由 `AuthenticationController` 在呼叫時傳入自己的 request 實例。
專案已有此形態可循：`Models/ListSmallGroupWeeklyReport.cs:154, 189, 208, 248, 275` 的
`SetPersonalReportViewModel(ref ToolUtilityClass, ...)`。

`GalleryViewModel` **未被 session 快取**（已查證），所以只要參數傳遞正確就沒有跨請求問題。

## 3. 要做的事

- [ ] 遷移第 2 節 7 個 A 類呼叫點（#1 依 2.1 特別處理，#7 依 2.2）
- [ ] 確認遷移後的類別**沒有任何一個** Dispose 注入進來的 `ToolUtilityClass`
      （`Tools/LineUtilityClass.cs:139`、`Tools/RecurringDonationPaymentProcessor.cs:104`
      的 Dispose guard 註解要更新為「不得釋放注入的 scoped 服務」，語意不變但理由變了）
- [ ] 新增測試（`ToolUtility.Dataverse.Tests`）：至少一個斷言遷移後的類別
      建構時不呼叫 `ToolUtilityFactory`，且 Dispose 不釋放注入的 ToolUtility
- [ ] **修訂 `prd.md` 的驗收標準**：
      - A1 改為「`ToolUtilityFactory.GetInstance` 僅存在於已文件化的 19 個 B 類殘留點」
      - A2 改為「`m_Crm2011OrganizationService` 的殘留僅存在於同一份 B 類清單」
      - 新增一條：B 類殘留清單必須逐一列出檔案:行號，且已開後續票
      - 在 PRD「不在範圍」補上：13 個 session 鍵快取的重新設計
- [ ] **在 `notes.md` 寫出 19 個 B 類殘留清單**（檔案:行號 + 所屬 session 快取 holder），
      資料來源是 `research/findings-run3-holder-lifetimes.md` 的 B 類欄位
- [ ] **建立後續票草稿** `.trellis/tasks/08-17-toolutility-scoped-lifetime/followup-session-cache.md`，
      內容需含：問題陳述（13 個 session 鍵快取）、受影響的 19 個呼叫點、
      Run 3.0 已評估的方向 1／方向 2 及各自代價、為何不在本任務處理

## 4. 檔案白名單

```
SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs
SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs
SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs            （只改 Dispose guard 註解）
SpeechMessageProducts.ChurchReport/ViewModels/GalleryViewModel.cs
SpeechMessageProducts.ChurchReport/Payments/DonationPaymentProductWorkflowDispatcher.cs
SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs
SpeechMessageProducts.ChurchReport/Controllers/PhoneBindingController.cs
SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/**
ToolUtility.Dataverse.Tests/**
.trellis/tasks/08-17-toolutility-scoped-lifetime/prd.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/implement.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/notes.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/followup-session-cache.md   （新建）
```

清單外一律不動。**特別是：不准動 `InMemoryDataContextSmallGroup`、不准動任何 B 類持有鏈、
不准刪 `ToolUtilityFactory`。** `ToolUtilityFactory` 與其 legacy 建構式因 19 個 B 類殘留
而**必須保留**，這是本任務的最終狀態，不是待辦事項。

若遷移後編譯器指出清單外檔案有殘留引用，只准修那一筆，並在 `notes.md` 列出檔案與行號。

## 5. 四條硬規則

1. 只改白名單內的檔案。
2. 連續 3 次驗證失敗 → 走第 8 節的失敗處理程序，不要試第 4 次。
3. 發現清單外的問題 → 寫進 `notes.md`，絕不順手修。
4. 通過第 6 節全部門檻才 commit；本 Run 一個 commit。

## 6. 品質門檻（commit 前必須全過，輸出原文貼進 notes.md）

```bash
dotnet build SpeechMessageProducts.sln -c Debug
```

期望 0 錯誤 0 警告。

```bash
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
```

期望 63 通過 0 失敗。

```bash
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
```

期望 >= 11 通過 0 失敗（本 Run 會再增加）。

```bash
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
```

**基準線 22 失敗 / 304 通過 / 共 326。失敗數不得 > 22，通過數不得 < 304。**

**G3 繁體中文文件**：新增或實質修改的 `.cs`，其 public/internal 型別、介面、建構式、
方法、重要屬性需有完整繁中 XML 註解。改為接收注入實例的建構式，必須寫明
「此 ToolUtility 由呼叫端的 request scope 提供，本型別不擁有、不釋放」。

**G4 編碼 / G4b 行尾**：沿用 `implement.md` 的兩段 Python 檢查，
必須分別輸出 `ENCODING OK` 與 `CRLF OK`。

## 7. 完成判定（機械可判，全部要有實際輸出）

```bash
grep -rn "ToolUtilityFactory.GetInstance" --include=*.cs SpeechMessageProducts.ChurchReport | grep -vE ":[0-9]+:\s*(//|///)"
```

輸出的每一行都必須出現在 `notes.md` 的 B 類殘留清單裡，**一行不多、一行不少**。
數量應為 19（若 #1 的無參數多載可刪，則 A 類 7 個全數歸零）。

```bash
grep -rn "ToolUtilityFactory" --include=*.cs SpeechMessageProducts.ChurchReport/Tools/ SpeechMessageProducts.ChurchReport/ViewModels/ | grep -vE ":[0-9]+:\s*(//|///)"
```

必須 0 行（A 類所在的兩個目錄已完全遷移）。

```bash
git status --porcelain
```

除白名單檔案外必須乾淨。

加上 G1 / G2 / G3 / G4 / G4b 全過。

## 8. 失敗處理程序（絕不使用無範圍的 git clean）

1. `git restore -- <本 Run 清單中原已存在的檔案>`
2. `rm -f .trellis/tasks/08-17-toolutility-scoped-lifetime/followup-session-cache.md`
3. `notes.md` 記錄失敗原因與最後的完整錯誤訊息
4. 標記 SKIPPED 並**停止**

## 9. commit

```
refactor(toolutility): A 類呼叫點改由 DI 取得，收斂本任務範圍
```

## 10. 明確不做

- **不准提出任何新的 Run。** 本任務到此結束。
- 不要遷移任何 B 類呼叫點
- 不要動 `InMemoryDataContextSmallGroup` 的 13 個 session 快取
- 不要刪除 `ToolUtilityFactory` 或其 legacy 建構式（B 類殘留仍需要它）
- 不要碰明文密碼與憑證輪替
- 不要修那 22 個既有失敗的 Payments 測試
- 不要重新設計 `ToolUtilityClass` 的公開 API
- 不要在後續票裡直接動手實作

## 11. 交付

在 `notes.md` 追加一節「Run 3-A 結果（本任務結案）」，寫明：

- 7 個 A 類呼叫點各自的遷移方式與結果；#1 的無參數多載是刪除還是保留（含理由）
- 19 個 B 類殘留清單（檔案:行號 + 所屬 session 快取 holder）
- `prd.md` 驗收標準的修訂前後對照
- 第 6、7 節每一道指令的**實際輸出原文**，不要摘要
- 後續票的路徑與摘要
- 「等待人工回歸」：登入、會友查詢／編輯、奉獻、影像上傳、LINE 綁定、批次下載、
  QR Code 產生（本 Run 動到 4 個 QrCode utility 與登入 ViewModel，回歸範圍要涵蓋這兩塊）
