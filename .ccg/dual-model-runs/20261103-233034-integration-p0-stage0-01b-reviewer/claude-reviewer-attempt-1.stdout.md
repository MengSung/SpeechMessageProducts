# 審查報告：階段0 第1批 第2/3份 — 守門工具與 CI

本審查僅以所附安全差異（SHA256 9d98223d…c97ad36）進行靜態閱讀，並只查閱明確非機密之既有原始碼（`Directory.Build.targets`、`Directory.Packages.props`、`SpeechMessage.Localization\Resources\SharedText.resx`、`SpeechMessageProducts.ChurchReport\Services\Images\ImageInputText.resx`、`.trellis\spec\backend\plugin-packaging-contract.md`）以確認既有關聯是否正確銜接。**未執行任何建置、測試、網路或 D365／LINE 連線**，未開啟 `.ccg/dual-model-runs` 其他歷史輸出。本份不代表全批判定。

---

## Critical 🔴

### 1. `tools/I18nCheck/RazorScanner.cs` `SkipBalanced`（約第 72–93 行）— verbatim 字串逸出字元誤判，可讓新增中文顯示文字完全繞過棘輪守門

**觸發條件**：Razor 檔案中，`@{ }`／`@if(...){ }` 等程式區塊內含一個以反斜線結尾的**逐字字串**（`@"..."`），例如：

```razor
@{ var path = @"C:\Logs\"; }
<p>新增中文顯示文字</p>
```

**根因**：`SkipBalanced` 用來跳過 `{}`／`()` 平衡區塊時，以下列邏輯處理字串內容（第 81–86 行）：

```csharp
if (quote != '\0')
{
    if (character == '\\') index++;
    else if (character == quote) quote = '\0';
    continue;
}
if (character is '\'' or '"') { quote = character; continue; }
```

此邏輯把反斜線一律當成跳脫字元（符合一般字串語義），但**完全沒有分辨 `@"..."` 逐字字串**——逐字字串中反斜線沒有跳脫意義，唯一跳脫是 `""`。當逐字字串以單一反斜線結尾（`@"C:\Logs\"`，真正終止於反斜線後的那個 `"`），本函式會把該反斜線視為跳脫，連同真正的收尾雙引號一併跳過（`index++` 再加上 `for` 迴圈自身的 `index++`，共跳過兩個字元），導致 `quote` 狀態未被正確關閉。後續程式碼會被誤判為「仍在字串內」，`SkipBalanced` 會持續往後掃描直到巧合命中另一個引號或讀到檔尾（第 92 行 `return source.Length;`）。

**影響**：`StripExpressions`（呼叫端，第 52 行）會把 `index` 跳到錯誤位置，導致緊接在該程式區塊之後、原本應被計入的真實 HTML 顯示文字（甚至整份檔案剩餘內容）被當成「程式碼」一併從 `Count` 的掃描範圍中移除。結果是：**新增的中文顯示文字在 `scan`／`verify` 中完全不會被計數**，CJK 棘輪（`CjkRatchet.Compare`）看到的數字不會增加，CI 會判定為通過（假綠燈），而實際上有未受審查的新硬編碼中文字串進入程式庫。

此屬性路徑（含反斜線結尾的 Windows 路徑字面值）在本專案情境下相當常見（D365／Windows 服務環境大量使用 `D:\...\` 形式路徑字面值），不是邊緣案例。

**測試缺口**：`tools/I18nCheck.Tests/BoundaryTests.cs` 的 Razor 案例（`Scan_Razor_OnlyCountsDisplayFragments`）只覆蓋註解、script/style、`@{ var x = "中文"; }`（一般字串，非逐字字串）等情境，**沒有任何逐字字串（`@"..."`）案例，也沒有反斜線結尾案例**，因此此缺陷不會被現有測試套件攔截，守門工具本身的測試矩陣存在對應的真實可重現缺口。

**最小修法**：在 `SkipBalanced` 判斷 `character is '\'' or '"'` 時，回頭檢查前一個字元是否為 `@`（且其前一個字元不是識別字的一部分），若是逐字字串則進入「只有 `""` 才跳脫，單一 `"` 直接結束」的狀態機；或更保守地，偵測到 `@"` 時直接線性尋找下一個未被 `""` 配對吞掉的 `"` 作為收尾，不透過反斜線跳脫邏輯處理。同時應在 `BoundaryTests.cs` 增補至少一個「逐字字串以反斜線收尾 + 緊接中文顯示文字」案例，證明修復後計數正確。

---

## Warning 🟡

### 2. `tools/I18nCheck/CommandRunner.cs` `Run`（第 24–38 行）— catch 清單未覆蓋所有可預期的例外型別

**觸發條件**：任何未列於 `catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or XmlException or JsonException)` 清單中的例外（例如未來維護時不慎拋出的其他型別）。

**影響**：例外會穿透 `Run`／`Main`，由 .NET 預設的未處理例外處理器印出完整 `Exception.ToString()`（包含堆疊、訊息）到 stderr。工具本身在正常路徑刻意只印固定代碼、不印來源內容或路徑（如 `InputIntegrityTests.Cli_InvalidInput...Fails` 等測試所驗證的設計意圖），但此設計意圖在未覆蓋的例外型別上沒有對應防護，可能意外洩漏本機路徑等診斷資訊到 CI 記錄。目前測試只驗證清單內的例外路徑，未驗證「未知例外型別」情境，屬可重現但尚未被證實會實際觸發的缺口（需要程式本身先有其他 bug 才會命中）。

**最小修法**：在最外層加一個通用 `catch (Exception)` 兜底，統一輸出固定代碼（如 `I18N999`）並回傳非零，不印出 `exception.Message`／`ToString()`，確保「不洩漏來源值」的設計意圖對所有例外型別一致成立。

---

## Info 🟢

### 3. `build/StrictCode.globalconfig` 與 `Directory.Build.targets` 既有銜接已確認生效（非本批問題，記錄供交叉確認）

`Directory.Build.targets` 第 39 行 `GlobalAnalyzerConfigFiles Include="$(MSBuildThisFileDirectory)build/StrictCode.globalconfig"` 在本次之前即已存在，本批新增的即是該被引用的實體檔案本身。確認兩者路徑一致、三項比較規則（CA1307/1309/1310/RS0030）確實以 `severity = error` 寫入，`global_level = 100` 可覆蓋分析器內建設定，沒有發現「引用了不存在的檔案」型假綠燈。

### 4. `tools/ToolUtility.Tests.csproj` 移除內嵌版本號已確認與 `Directory.Packages.props` 的中央版本一致

`Microsoft.NET.Test.Sdk`(17.8.0)、`xunit`(2.6.6)、`xunit.runner.visualstudio`(2.5.6)、`Moq`(4.20.70)、`FluentAssertions`(6.12.0)、`coverlet.collector`/`coverlet.msbuild`(6.0.0)、`Microsoft.CrmSdk.CoreAssemblies`(9.0.2.56) 均在 `Directory.Packages.props` 有對應 `PackageVersion`，不會因改用中央套件管理而還原失敗或悄悄改變版本（`Microsoft.Extensions.Logging.Abstractions` 從舊的顯式 9.0.0 變為中央 10.0.0，屬於刻意升級，非遺漏）。未發現 NuGetAudit／還原假綠燈風險。

### 5. `build/PluginMerge.targets`、`tools/I18nCheck`、`tools/PluginPackaging` 其餘邊界檢查（路徑逃逸、重新解析點、重複身分、PE 讀取生命週期）

`LocalFiles.Resolve` 的父目錄字串比較正確加上目錄分隔符號再比對，避免前綴混淆型路徑逃逸；`RepositoryScanner.ProjectPaths`／`Files` 對排除目錄、方案外部專案參照、重複身分均 fail closed 並有對應測試；`PluginPackaging/Program.cs` 以 `using var stream` / `using var pe = new PEReader(stream)` 確定性釋放，測試以立即獨佔重新開檔驗證釋放時機。上述均未發現資源未釋放或機密外洩問題。

### 6. 既有 `.resx` 檔案現況核對

目前 repo 中僅 3 個 `.resx`（`SharedText.resx`、`ImageInputText.resx`，另一個在暫存目錄），兩者鍵名皆符合 `I18nCheck` 要求的分層格式，`generate --check`／`verify` 在目前狀態下不會被非此體系的舊版 resx 誤判中斷。此為現況確認，非既存問題；若未來引入未遵循此鍵名慣例的 `.resx`（例如 WinForms 設計工具產生、含 `type`/`mimetype` 的資源檔），`ResourceCatalog.Load` 會以 `InvalidDataException` 整批中斷 `verify`，這只是記錄供日後留意，目前未發現可重現觸發情境，不列為缺陷。

---

## 總結

本份（守門工具與 CI，第2/3份）發現 **1 項 Critical**：`RazorScanner.SkipBalanced` 對逐字字串反斜線處理錯誤，會讓新增中文顯示文字在特定且非罕見的程式碼模式下完全跳過 CJK 棘輪計數，構成守門假綠燈，且現有測試矩陣未覆蓋此情境。另有 1 項 Warning（例外型別覆蓋不全的資訊洩漏防護缺口）及數項 Info（確認既有關聯正確、未發現問題）。跨使用者／跨教會隔離、機密外洩（正式金鑰僅讀環境變數、未複製內容）、net48/net10 行為一致性（`PluginMerge.targets`／`AnalyzerProbe` 雙目標設定）等其餘審查項目**未發現問題**。建議在合併前修復 Critical 項目並補上對應測試案例；Warning 項目可視優先序排入後續批次。

本份不可代表全批結論，需待同批其他分份一併完成後綜合判定。

---
SESSION_ID: d823a447-472c-4f1d-8562-815469f6d894
