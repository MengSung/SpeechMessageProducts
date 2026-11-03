# 審查報告：stage0 第1批 第3/3份（守門工具與 CI）

**範圍**：`73b6e6cf4..ae29cb40a`，`tools/RepoQuality/*.ps1`（新檔）
**方式**：純靜態閱讀，未執行任何指令、未建立連線、未讀取機密欄位。本份僅涵蓋所附安全差異，不代表全批。

---

## High

### H-1　`Test-PackageVersions.ps1` 缺少 reparse point／junction 邊界檢查，與同批姊妹工具不一致
- **檔案／行號**：`tools/RepoQuality/Test-PackageVersions.ps1:60-64`（讀取迴圈 `foreach ($relativePath in $files) { ... $path = Join-Path $root $relativePath; ... $bytes = [System.IO.File]::ReadAllBytes($path) ... }`）
- **對照**：同批 `Find-DuplicateCode.ps1:25-37`（`Get-SafeRepositoryPath`，逐段檢查 `ReparsePoint` 並拒絕）與 `Test-TextEncoding.ps1:55-62`（`$safe = ...; foreach ($segment ...) { if (ReparsePoint) { $safe = $false } }`）都明確針對同一風險做了邊界防護，且文件註解本身也宣告「不走訪父工作樹…不沿 junction 讀外部檔」，但本檔的文件註解（`Test-PackageVersions.ps1:5`）寫了同樣承諾卻**沒有對應實作**。
- **觸發條件**：此 repo 目前就是巢狀 worktree 結構（`.worktrees\...\.worktrees\...`）。只要 repo 內任一追蹤或未被忽略的路徑含有 NTFS junction／reparse 目錄（意外建立或由其他流程殘留），且該目錄下存在 `*.csproj`，`git ls-files --cached --others --exclude-standard` 有可能回報該路徑，本腳本會直接 `ReadAllBytes`/`LoadXml` 讀取並解析其內容，不會被攔截。
- **具體影響**：違反本輪明訂的「檔案掃描邊界只限本 worktree／方案」要求；可能讀取並把其他 worktree（可能屬於別的任務或尚未送審的工作）之套件參照資訊，透過 `FAIL InlineVersion: ...` 等訊息間接外洩路徑與套件識別碼。即使外洩內容有限，仍是跨邊界讀取，與同批其他三個守門腳本的防護基準不一致。
- **最小修法**：複用 `TextEncoding.Common.ps1` 已有的位元組/路徑工具模式，在 `Join-Path $root $relativePath` 之後，於讀檔前逐段檢查路徑元件是否為 `ReparsePoint`（可直接搬用 `Test-TextEncoding.ps1:55-62` 的邏輯），不符合就計入失敗或跳過並記錄。

---

## Medium

### M-1　`Find-DuplicateCode.ps1` 的型別重名識別未追蹤外層類別，巢狀同名型別會被誤判為跨專案重複
- **檔案／行號**：`tools/RepoQuality/Find-DuplicateCode.ps1:61`（比對模式 `(?<type>\b(?:class|struct|record...)\s+(?<typename>...))` 只定義 `namespace` 群組，沒有任何追蹤「目前所在類別」的機制）與 `:83-86`（`if ($namespace) { $namespace + '.' + $name } else { $name }` — 僅以目前 `namespace` 變數組出型別鍵值，完全不含外層類別名稱）。
- **觸發條件**：例如專案 A 有 `namespace N { class Outer1 { class Inner { ... } } }`，專案 B 有 `namespace N { class Outer2 { class Inner { ... } } }`。兩個 `Inner` 彼此毫無關係，但因為只用 `namespace` 組鍵，都會產生相同識別 `type|N.Inner`，被判定為跨專案重複並寫入基準（或在之後觸發 `NewDuplicate` 假警報）。
- **具體影響**：守門工具的「型別重名」偵測可能對巢狀型別產生假陽性（不相關型別被要求塞進基準、或造成不必要的 CI 紅燈），也可能反過來讓真正重複但巢狀於不同外層類別、恰好不同名的情況被漏判。屬於偵測準確度問題，會增加人工用 `-WriteBaseline` 消音的誘因，間接弱化棘輪檢查的意義。
- **最小修法**：比照 `namespace` 的 frame 堆疊做法，對 `class/struct/record` 的 `{`/`}` 也推入/彈出一個「目前型別路徑」堆疊，組鍵時改用「namespace + 外層型別鏈 + 型別名」。

---

## Info（無需動作，供留意）

### I-1　`Find-DuplicateCode.ps1` 首次建立基準時會自動把既有重複收錄為允許項
- **檔案／行號**：`tools/RepoQuality/Find-DuplicateCode.ps1:157-163`（`$created = -not [IO.File]::Exists($Baseline); if ($created -or $WriteBaseline) { ... 寫入目前掃描結果作為基準 ... }`）。
- 這是典型棘輪（ratchet）基準檔案設計，首次執行即把當下所有重複「原樣」收錄為允許清單，不需要人工逐項核准；核准點落在基準檔本身進入版控後的 code review。此行為與文件註解（`WriteBaseline` 說明）一致，屬設計如此，非缺陷，僅提醒：若日後有人在尚未檢視現有重複的情況下刪除/重建基準檔，等同靜默接受當時全部重複。

### I-2　`Test-PackageVersions.ps1` 排除清單未包含 `.worktrees`／參考方案等目錄
- **檔案／行號**：`tools/RepoQuality/Test-PackageVersions.ps1:61`（僅排除 `^(docs/|\.ccg/tasks/)`）。
- 同批 `Find-DuplicateCode.ps1` 與 `Test-TextEncoding.ps1` 都額外排除 `.worktrees`、`其他解決方案` 等目錄作縱深防禦；本檔案依賴「git worktree 各自獨立索引」這個事實上的邊界，實務上多數情況下不會真正讀到其他 worktree 的檔案，但與姊妹腳本的防禦深度不一致，建議補上相同排除規則以保持一致性（與 H-1 可合併一併修）。

---

## 其他檢查結果（未發現問題）

- **資源釋放**：各腳本中 `Process`（`Test-AnalyzerProbe.ps1`、`Test-PackageVersions.ps1`、`TextEncoding.Common.ps1` 的 `Invoke-TextQualityGit`）均以 `try/finally` 確定 `Dispose()`；逾時路徑先 `taskkill /T /F` 或 `Kill()` 後再等待，`Test-AnalyzerProbe.ps1` 的暫存 fixture 目錄亦在 `finally` 內以前綴比對後才 `Remove-Item`，未見資源洩漏或刪除範圍外目錄的風險。
- **例外責任（10.5/10.9）**：各腳本頂層皆以 `try/catch` 包裹、固定輸出 `FAIL ...` 前綴並 `exit 1`，未見吞例外或遺漏 `exit` 碼的情形；`Test-SixLaborsCiLicense.Tests.ps1` 的 `Assert-Contract` 只輸出案例名稱與 PASS/FAIL，從未把例外訊息或機密內容印出。
- **機密外洩**：所附差異中唯一涉及機密的是 `Test-SixLaborsCiLicense.Tests.ps1`，其斷言（`Configured_NeverPrintsLicense`、`PublishFailure_NeverPrintsLicense`）確認測試輸出不含合成授權內容；測試本身也不讀取真實授權檔或 CI secrets。本輪未讀取 `.github/workflows/solution-ci.yml` 實際腳本（未在本差異內，依指示不逐一查核）。
- **net48/net10 行為一致性**：`Test-AnalyzerProbe.ps1` 對 `NuGetAuditMode`、`AnalysisLevel`/`EffectiveAnalysisLevel`、CA1707 命名、CA1716 介面命名均分別對 `net48` 與 `net10.0` 建立對照案例，未見單一框架被遺漏驗證。
- **XML／反序列化安全**：`Test-Coverage.ps1`、`Test-PackageVersions.ps1` 中的 `XmlDocument`/`XmlReader` 皆設定 `XmlResolver = $null`（`Test-Coverage.ps1` 另加 `DtdProcessing.Prohibit` 與 `MaxCharactersInDocument`），可防止 XXE；`Test-Coverage.Tests.ps1` 的 `ExternalEntity_Fails` 案例與程式碼邏輯一致。
- **假綠燈防護**：`Find-DuplicateCode.ps1`（`NoSources`/`NoProjects`）、`Test-PackageVersions.ps1`（`checked -eq 0` 時 throw）均明確拒絕「空掃描視為通過」；`Test-AnalyzerProbe.ps1` 以「結束碼非 0 且錯誤碼恰好只含預期代碼」雙重條件判斷，避免編譯環境問題（例如還原失敗）被誤認為規則命中成功。

---

## 結論

本份（守門工具與 CI，批1/第3份）發現 **1 項 High（跨 worktree 邊界檢查缺漏）**、**1 項 Medium（巢狀型別重名誤判）**、2 項 Info。無 Critical 發現，亦未發現機密外洩或資源未釋放的問題。H-1 建議在合併前修補（補上與姊妹腳本一致的 reparse point 檢查），M-1 可視下一輪排程處理。本份不代表全批審查結果，仍需與同批其餘分份合併判斷。

---
SESSION_ID: 4672dc41-dec3-4519-b477-ec6f033f79c4
