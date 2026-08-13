# P7.5 離線前置依賴證據規劃審查報告 (P7.5 Offline Prerequisite Evidence Planning Review)

## 1. 架構評估 (Analysis)

本審查針對在 active Trellis 任務下規劃建立的「離線確定性掃描器/驗證器 (Offline, Deterministic Scanner/Validator)」進行架構與安全性評估。該工具旨在掃描 `ChurchReport` 生產環境中的 `.cs` 與 `.csproj` 檔案，以識別並統計舊有 `ToolUtility` 與 Dynamics CRM SDK 的依賴殘留，作為 P7.5 門檻（Zero Reference Gate）的准入證據。

目前系統的 authoritative gap matrix 包含 70 個 call sites，其中 3 個已遷移並停用 (migrated-disabled)，67 個尚未遷移。P7.5 的阻擋因素包含 49 個未遷移的 consumer、13 個混合型、5 個特殊資源待處理以及 3 個舊版 SDK 依賴。

經過對現有 `build_rebaseline.py` 程式碼與規劃說明的審查，我們發現了數個關鍵的安全隔離、規避風險與誤報漏洞，特別是關鍵字統計未排除註解與字串字面量，這將直接導致驗證機制失效。

---

## 2. 架構決策 (Architecture Decision)

### 決策：強化語法剝離與精確依賴掃描
* **決策內容**：
  1. **強制雙重剝離**：在計算 `legacy_reference_count` 時，必須強制先執行 `strip_csharp_comments_and_literals`，確保註解與 Log 字串不被計入。
  2. **擴充 C# 語法支援**：增強 Regex 模式以支援 C# 8.0+ 插值字串 (`$"..."`, `$@"..."`) 與 C# 11 原始字串字面量 (`"""..."""`)。
  3. **編碼容錯**：讀取檔案時改用 `utf-8-sig`，並加入解碼失敗時的 fail-closed 機制。
* **拒絕的替代方案**：
  * *使用 Roslyn AST 解析器*：雖然最精確，但會引入外部 SDK 依賴與複雜的建置環境要求，違反「純離線、輕量化、無外部依賴」的原則。
  * *僅排除特定註解*：使用簡單的 Regex 排除行註解，但這無法處理多行註解與複雜的字串拼接，容易被規避。
* **假設與前提**：
  * 假設所有生產環境程式碼皆位於 `SpeechMessageProducts.ChurchReport` 目錄下，且測試專案已完全隔離於該目錄之外。
* **潛在副作用**：
  * 增強後的 Regex 在處理極大檔案時可能會有些微效能損耗，但因僅在建置/驗證期執行，對執行期無影響。

---

## 3. 實作計畫 (Implementation Plan)

### 步驟 1：修正 `build_rebaseline.py` 中的文字剝離與讀取邏輯
更新 `strip_csharp_comments_and_literals` 與 `read_utf8`，並確保 `legacy_reference_count` 在掃描前先進行剝離。

### 步驟 2：建立單元測試驗證掃描器邊界
在測試專案中加入合約測試，模擬以下情境以確保掃描器不會被規避或產生誤報：
* 包含 `// ToolUtility` 的註解行（預期計數：0）
* 包含 `$"Calling {nameof(ToolUtility)}"` 的插值字串（預期計數：0）
* 實際呼叫 `ToolUtility.SomeMethod()` 的程式碼（預期計數：1）

---

## 4. 差異補丁 (Unified Diff Patch)

```diff
--- a/.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/build_rebaseline.py
+++ b/.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/build_rebaseline.py
@@ -118,3 +118,3 @@
 def read_utf8(path: Path) -> str:
-    """霈€?摰?repository 靘?嚗€€?helper 銝??怎垢臬?嚗????撖? deployment ???"""
-    return path.read_text(encoding="utf-8")
+    """讀取指定 repository 來源，支援 UTF-8 with BOM 以避免編碼解析錯誤。"""
+    return path.read_text(encoding="utf-8-sig")
 
@@ -193,8 +193,10 @@
 def strip_csharp_comments_and_literals(source: str) -> str:
-    """隞亙摰摨衣征?賢?隞?C# line/block comments ??quoted literals嚗??擗?token ??靘移蝣?OperationIds ??嚗?銝遣衜?parser cache ?鈭怠霈??€?"""
+    """清除 C# 程式碼中的註解與字串字面量，支援插值字串與原始字串字面量。"""
     pattern = re.compile(
-        r'//[^\r\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\\r\n])*"',
+        r'//[^\r\n]*|/\*.*?\*/|'
+        r'\$@"(?:""|[^"])*"|@"(?:""|[^"])*"|'
+        r'\$"(?:\\.|[^"\\\r\n])*"|"(?:\\.|[^"\\\r\n])*"|'
+        r'""".*?"""',
         re.DOTALL,
     )
     return pattern.sub(lambda match: "".join("\r" if char == "\r" else "\n" if char == "\n" else " " for char in match.group()), source)
@@ -239,3 +241,3 @@
-        text = read_utf8(path)
+        text = strip_csharp_comments_and_literals(read_utf8(path))
         count += sum(len(pattern.findall(text)) for pattern in patterns)
```

---

## 5. 審查發現與補救措施 (Findings & Remedies)

### 5.1 [Critical] `legacy_reference_count` 未清除註解與字串字面量
* **檔案路徑**：`.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/build_rebaseline.py` (第 225-241 行)
* **判定理由**：該函數直接讀取原始檔案內容並進行關鍵字匹配，完全跳過了 `strip_csharp_comments_and_literals`。這會導致程式碼註解（例如 `// 舊版 ChurchReport 流程曾用這個特殊字串...`）或 Log 訊息中的關鍵字被計入，產生嚴重的 False Positive，導致即使實際程式碼已無依賴，驗證仍會失敗。
* **補救措施**：在進行關鍵字匹配前，必須先將讀取的文字傳入 `strip_csharp_comments_and_literals` 進行清理。

### 5.2 [Warning] C# 註解與字串清除 Regex 無法處理現代 C# 語法
* **檔案路徑**：`.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/build_rebaseline.py` (第 193-200 行)
* **判定理由**：現有的 Regex 模式無法識別 C# 8.0+ 的插值字串（如 `$@""`）與 C# 11 的原始字串字面量（`"""..."""`）。若開發人員在這些字串中寫入關鍵字，將會繞過清除機制，被 scanner 誤判為實際程式碼依賴。
* **補救措施**：更新 Regex 模式以包含對插值字串與多引號原始字串字面量的支援，如上方 Diff 所示。

### 5.3 [Warning] 檔案讀取未處理 BOM 與非 UTF-8 編碼
* **檔案路徑**：`.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/build_rebaseline.py` (第 118-120 行)
* **判定理由**：`read_utf8` 強制使用 `utf-8` 解碼。若專案中包含 UTF-8 with BOM 或舊有 Big5 編碼的檔案，將會導致解碼崩潰或讀入亂碼，使關鍵字匹配失效（False Negative）。
* **補救措施**：改用 `utf-8-sig` 讀取以自動處理 BOM，並在遇到解碼錯誤時拋出明確的錯誤訊息以實現安全關閉 (Fail Closed)。

### 5.4 [Warning] 缺乏針對 Scanner/Validator 本身的測試策略
* **檔案路徑**：規劃階段 (TBD)
* **判定理由**：若無針對掃描器本身的單元測試，無法保證 Regex 剝離邏輯在面對複雜 C# 語法時的正確性，容易因漏配導致安全門檻失效。
* **補救措施**：在測試專案中新增專門測試 `strip_csharp_comments_and_literals` 的測項，涵蓋各種邊界字串與註解組合。

### 5.5 [Info] 關鍵字匹配過於寬鬆導致命名衝突
* **檔案路徑**：`.trellis/tasks/archive/2026-08/08-12-p7-remaining-work-rebaseline/build_rebaseline.py` (第 227-232 行)
* **判定理由**：使用 `\bToolUtility\b` 會匹配到如 `LegacyToolUtilityAdmissionHostedService` 等類別名稱。即使該類別已完成重構且不依賴舊 SDK，也會因為命名包含關鍵字而被計入。
* **補救措施**：在後續重構中，應避免在新設計的類別名稱中直接包含舊有關鍵字，或在 scanner 中建立排除清單。
