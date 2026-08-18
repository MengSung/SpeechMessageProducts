Active task: .trellis/tasks/08-17-toolutility-scoped-lifetime

你要執行 **Run 2.5a — 清除 C 類死碼**。

Run 3.0 把 9 處呼叫點列為 C「未確認」。外部稽核把那 9 處查完了：**其中 7 個是完全零引用的
死碼型別（或整條死鏈），另外 2 個是活型別上的死建構式**。所以這一棒不是「繼續找入口」，
是「刪掉它們」。刪完之後，需要遷移的可執行呼叫點會從 35 降到 26。

**B 類的設計方向（方向 1 / 方向 2）不在本 Run 決定，不要碰。**

## 0. 先讀（不可略過）

```
.trellis/tasks/08-17-toolutility-scoped-lifetime/prd.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/design.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/implement.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/notes.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/research/findings-run3-holder-lifetimes.md
```

## 1. 稽核結論（已驗證，但你必須自己重跑一次確認，不要照單全收）

### 1.1 七個零引用的死碼

| 檔案 | 稽核查到的事實 |
|---|---|
| `WebServiceConnector/DedicationInfo.cs` | 全 solution 零引用 |
| `WebServiceConnector/EquipmentStatusCalculator.cs` | 全 solution 零引用 |
| `WebServiceConnector/HappyGroupUtility.cs` | 全 solution 零引用 |
| `WebServiceConnector/LineBindingUtility.cs` | 專案內 6 個命中**全是 Debug 字串字面值** `[LineBindingUtility.CopyVistorCardInfo]`（`NewPerson.cs:639,648`、`PersonalInfomatioManager.cs:368,377,925,934`），不是型別引用 |
| `WebServiceConnector/UploadData.cs` | 5 個命中全是 `UploadIntegrateData.UploadData(...)` **方法名**，不是 `UploadData` 型別 |
| `WebServiceConnector/WebServiceConnector.cs` | `new WebServiceConnector(`、變數宣告、繼承皆 0；solution 外部 6 個命中全是 `ChurchReport.WebServiceConnector` **命名空間**（在 `ChurchReport.MemberInfo.Tests` 內） |
| `WebServiceConnector/RegisterConnector.cs` ＋ `Models/RegisterManager.cs` | `RegisterConnector` 只由 `RegisterManager.cs:29` 建立，而 `RegisterManager` 全 solution **零呼叫者** → 整條鏈死碼，兩個檔案一起刪 |

`SpeechMessageProducts.ChurchReport` 內沒有任何 `Type.GetType` / `Activator.CreateInstance`
反射進入點，所以「零引用」等同「不可達」。

### 1.2 兩個活型別上的死建構式

| 位置 | 事實 |
|---|---|
| `Models/ListManagementDataManager.cs:86`（`discoveryServiceType` 多載） | 全專案只有 `InMemoryDataContextSmallGroup.cs:933` 的 `new ListManagementDataManager()` 無參數多載被呼叫 → `:86` 這個建構式沒有呼叫者。**型別本身是 B 類，只刪建構式，不刪型別。** |
| `WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:135` | 只有 `DonationPaymentManager.cs:182` 呼叫對應 `:192` 的四參數建構式 → `:135` 這個建構式沒有呼叫者。**型別本身是 B 類，只刪建構式，不刪型別。** |

## 2. 動手前的強制查核（每一項都要有實際輸出）

刪除任何東西之前，對上表 7 個型別 + `RegisterManager` **逐一**跑完下列三項，
輸出貼進 `notes.md`。**任何一項不是 0，就不准刪該型別**，改記錄為保留並寫明原因。

```bash
# (a) 建立、宣告、繼承
grep -rn "new <型別>\s*(\|new <型別>\s*{\|: <型別>\b\|<型別> [a-zA-Z_]" --include=*.cs . --exclude-dir=obj --exclude-dir=bin

# (b) 反射與字串型別名（跨整個 solution，含測試專案）
grep -rn "<型別>" --include=*.cs . --exclude-dir=obj --exclude-dir=bin | grep -vE "//|///"

# (c) 非 .cs 的引用（cshtml view、csproj、設定檔）
grep -rn "<型別>" --include=*.cshtml --include=*.csproj --include=*.json . --exclude-dir=obj --exclude-dir=bin
```

**特別注意**：`ChurchReport.MemberInfo.Tests/Payments/*NamingTests.cs` 用
`Type.GetType("ChurchReport.WebServiceConnector.<型別>, ChurchReport")` 做命名斷言。
刪型別前必須確認沒有任何一個命名測試指涉你要刪的型別 —— 那 22 個既有失敗的測試也算，
不准因為它們本來就紅就忽略。

## 3. 要做的事

- [ ] 對第 1.1 節 7 個型別 + `RegisterManager` 跑完第 2 節三項查核，輸出貼進 `notes.md`
- [ ] 查核全過者刪除整個檔案；任何一項不過就保留並在 `notes.md` 寫明原因與證據
- [ ] 刪除第 1.2 節兩個死建構式（**只刪建構式，型別保留**）
- [ ] 更新 `research/findings-run3-holder-lifetimes.md`：把已解決的 C 類條目改為
      「已刪除（死碼）」或「保留（原因）」，並更新 A/B/C 統計數字
- [ ] 更新 `implement.md` 的 Run 2.5 / Run 3 章節：反映新的呼叫點總數與剩餘批次

## 4. 檔案白名單

```
SpeechMessageProducts.ChurchReport/WebServiceConnector/DedicationInfo.cs                （刪除）
SpeechMessageProducts.ChurchReport/WebServiceConnector/EquipmentStatusCalculator.cs     （刪除）
SpeechMessageProducts.ChurchReport/WebServiceConnector/HappyGroupUtility.cs             （刪除）
SpeechMessageProducts.ChurchReport/WebServiceConnector/LineBindingUtility.cs            （刪除）
SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadData.cs                    （刪除）
SpeechMessageProducts.ChurchReport/WebServiceConnector/WebServiceConnector.cs           （刪除）
SpeechMessageProducts.ChurchReport/WebServiceConnector/RegisterConnector.cs             （刪除）
SpeechMessageProducts.ChurchReport/Models/RegisterManager.cs                            （刪除）
SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs                  （只刪 :86 建構式）
SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs  （只刪 :135 建構式）
.trellis/tasks/08-17-toolutility-scoped-lifetime/research/findings-run3-holder-lifetimes.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/implement.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/notes.md
```

清單外一律不動。**特別是：不准動任何 B 類的持有鏈、不准動 `InMemoryDataContextSmallGroup`
的 13 個快取、不准遷移任何 A 類呼叫點。** 那些是後續 Run 的事。

若刪除後編譯器指出清單外檔案有殘留 `using` 或引用，只准移除該筆引用，
並在 `notes.md` 逐一列出檔案與行號。

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

期望 11 通過 0 失敗。

```bash
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
```

**基準線 22 失敗 / 304 通過 / 共 326。失敗數不得 > 22，通過數不得 < 304。**
刪除死碼最可能在這裡出事（命名測試以字串比對檔案路徑與型別名），
若這裡惡化，代表你刪了測試在斷言的東西 —— 還原該檔案，記錄原因。

**G3 繁體中文文件**：本 Run 以刪除為主。若有實質修改的 `.cs`（兩個死建構式所在檔案），
其變更處的 XML 註解要保持完整且正確，不可留下指涉已刪建構式的過時說明。

**G4 編碼 / G4b 行尾**：沿用 `implement.md` 的兩段 Python 檢查，
必須分別輸出 `ENCODING OK` 與 `CRLF OK`。

## 7. 完成判定（機械可判）

```bash
git status --porcelain
```

除白名單檔案與既有的 `.ccg/.../.turns.json` 外必須乾淨。

```bash
grep -rc "ToolUtilityFactory.GetInstance" --include=*.cs SpeechMessageProducts.ChurchReport | awk -F: '{s+=$2} END {print s}'
```

刪除前為 **39**（35 可執行 + 4 註解）。刪除 7 個死碼型別後應降為 **32**
（28 可執行 + 4 註解）；再刪兩個死建構式後應為 **30**（26 可執行 + 4 註解）。
**實際數字若與此不符，先停下來查清楚原因再繼續，不要硬改數字去湊。**
真實數字以你的實際輸出為準，並在 `notes.md` 說明差異來源。

```bash
grep -rn "DedicationInfo\|EquipmentStatusCalculator\|HappyGroupUtility\|LineBindingUtility\|RegisterConnector\|RegisterManager" --include=*.cs SpeechMessageProducts.ChurchReport/
```

只應剩下 Debug 字串字面值（`[LineBindingUtility.CopyVistorCardInfo]`），無型別引用。

## 8. 失敗處理程序（絕不使用無範圍的 git clean）

1. `git restore -- <本 Run 修改的既有檔案>`
2. 誤刪的檔案用 `git checkout HEAD -- <路徑>` 還原，逐一列出路徑
3. `notes.md` 記錄 Run 編號、失敗原因、最後的完整錯誤訊息
4. 標記 SKIPPED 並**停止**

## 9. commit

```
refactor(toolutility): 刪除 C 類死碼，縮小 ToolUtility 遷移面
```

## 10. 明確不做

- **不要決定 B 類採方向 1 或方向 2** —— 那由使用者決定，不是你
- 不要遷移任何 A 類或 B 類的 `ToolUtilityFactory.GetInstance()` 呼叫點
- 不要刪除 `ToolUtilityFactory`
- 不要動 `InMemoryDataContextSmallGroup` 的 13 個 session 快取
- 不要碰明文密碼與憑證輪替
- 不要修那 22 個既有失敗的 Payments 測試
- 不要重新設計 `ToolUtilityClass` 的公開 API

## 11. 交付

在 `notes.md` 追加一節「Run 2.5a 結果」，寫明：

- 第 2 節三項查核對每個型別的**實際輸出原文**（這是刪除的唯一依據，不可摘要）
- 實際刪除的檔案清單、保留的檔案與保留原因
- 第 6、7 節每一道指令的**實際輸出原文**
- 刪除後的 A/B/C 統計數字，以及剩餘待遷移呼叫點總數
- 範圍外發現（不要順手修）

> 人工回歸不列為你的完成條件，但要在 `notes.md` 標明「等待人工回歸」。
