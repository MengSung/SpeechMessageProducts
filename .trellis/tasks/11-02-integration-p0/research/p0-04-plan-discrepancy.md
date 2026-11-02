# P0-04　StrictCode 屬性載入順序與計畫差異

## 停止依據與現況
00 §9 第 6 點：「發現本計畫與 v3.2 設計、或與程式現況有矛盾。」
P0-03 已完成並提交 d1c92d83。P0-04 僅核對既有設定與執行隔離的 MSBuild 評估，尚未修改產品／建置設定或加入正式探測專案。

## 已證實的差異
P0-04 第 1 步要求將條件區段加入 Directory.Build.props 末端，以各新專案本體的 SpeechMessageStrictCode=true 決定啟用嚴格設定。
SDK 專案會先載入 Directory.Build.props，再評估 csproj 的 PropertyGroup。因此在 props 中做此條件判斷時，專案本體的開關尚不存在。

隔離試驗複製現有 Directory.Build.props，原樣追加計畫六個設定與一個唯讀觀察值；同目錄的 SDK 專案在本體設定 SpeechMessageStrictCode=true。只執行 dotnet msbuild 的 -getProperty，不建置、不還原、不使用網路或任何機密。

| 屬性 | 實際結果 |
| --- | --- |
| props 末端看見的 SpeechMessageStrictCode | 空字串 |
| csproj 評估結束後的 SpeechMessageStrictCode | true |
| EnableNETAnalyzers | false |
| AnalysisMode | None |
| TreatWarningsAsErrors | false |
| Nullable | 空字串 |
| GenerateDocumentationFile | false |

重現輸入與輸出見 wp-evidence/P0-04/ordering-proof.json。試驗專案只留在被忽略的 .ccg/tmp/p004-ordering-proof/，不納入套件盤點、方案或提交。
這是計畫可行性證據，不冒充 AnalyzerProbe 的 RS0030 紅燈；正式 TDD 尚未開始。

## 建議核定的計畫修正
1. P0-04 第 1 步改為新增根目錄 Directory.Build.targets，讓嚴格設定在 csproj 的 SpeechMessageStrictCode 已知後才套用；既有 Directory.Build.props 的舊專案預設保持原樣。
2. 六個嚴格屬性、CA1307／CA1309／CA1310、BannedApiAnalyzers 與 BannedSymbols AdditionalFiles 均受同一開關控制；不得以命令列 -p:SpeechMessageStrictCode=true 掩蓋載入順序問題。
3. 其餘 P0-04 範圍與驗收不變：禁用 API、net48 polyfills、DateTime.Now 的 AnalyzerProbe；先取得 RS0030 契約測試紅燈，再實作與完整驗證。
4. 補充 MSBuild 設定回歸檢查：探測專案不帶額外啟用參數時即為嚴格模式；既有方案專案仍沿用原設定。

使用者已回覆「核定建議，更新計畫後繼續 P0-04」。已先更新權威計畫並執行原 TDD；本紀錄保留原停止原因，最新完成結果見 wp-reports/P0-04.md。
