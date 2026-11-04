# 審查結論（第 09 份：階段 0 審查修正複審）

審查範圍：`ae29cb40aa5c395f8..e3f89eb6a77aca4` 的 27 個檔案，唯讀靜態分析，未啟動網站、未連線 D365/LINE/DevExpress、未讀取任何 secrets/appsettings。針對 C-01、H-01、M-01、M-02、.gitignore、CI 過渡範圍、06 兩項 Critical、05b-C1 逐項覆核，並以獨立暫存專案（repo 外、無網路、不含任何產品程式碼）對 `SyntaxFactory.ParseToken(text, offset)` 的位置語義做了一次空白對照實驗，以驗證 C-01 修正的核心機制是否正確。

## Critical 🔴
無。本次新增的 27 個檔案中未發現可驗證的 Critical 缺陷。

## High 🟠
無。

## Medium 🟡
無新增 Medium。（H-01 已依使用者要求改列 Medium，見下方 Info 確認其邊界正確。）

## Low / Info 🟢

- **TOCTOU 殘留風險（非本次新增，屬既有設計選擇）**：`tools/RepoQuality/Test-PackageVersions.ps1` 的 `Get-SafePackagePath` 與 `Find-DuplicateCode.ps1` 的 `Get-SafeRepositoryPath` 在「檢查 reparse point」與「實際讀取 XML/文字內容」之間存在理論上的競態窗口（另一行程可在檢查後、讀取前置換目錄為 junction）。這屬於本機開發工具對抗「意外 junction」的防呆，不是對抗敵對行程的安全邊界，且與既有模式一致，不建議在本輪擴大實作，僅記錄。
- **Razor/C# 邊界掃描（C-01）核心機制已實測確認**：`RazorCodeBoundary.SkipCode` 以 `index + token.SpanStart` 還原絕對位置的寫法，經獨立 Roslyn 探針驗證——`SyntaxFactory.ParseToken(text, offset)` 回傳的 `Span`/`SpanStart` 確實相對於 `offset` 歸零（非絕對座標），程式碼的手動還原是正確用法，不是 bug。並手動逐步追蹤 `a<b && b>c`（泛型/比較運算子不誤判為標籤）、`<div>` 內含假 `}`/`)` 的範本排除、`SkipTransition` 遞迴等多個測試案例的狀態機轉換，邏輯一致。受限於「唯讀審查、不得啟動建置」的限制，我沒有實際執行 108 個 I18n 測試案例本身，僅以靜態追蹤＋一次範圍外的 API 語義實測來源佐證；完整 108/108 綠燈仍為使用者提供的本機證據，非本次獨立重跑驗證。
- **M-02 型別 owner 追蹤邏輯**：手動逐 token 追蹤 `Find-DuplicateCode.ps1` 新增的 `typeFrames`/`owners` 堆疊，涵蓋巢狀 sibling 還原（`OuterA.First.Inner` vs `OuterA.Second.Inner`）、body-less record 分號清除 `pendingType`（`record Marker;` 後的 `class OuterA` 不會被誤判為 Marker 的巢狀成員）兩類情境，結果與測試期望一致，無發現迴歸。
- **CI 過渡依賴閉包（CI transition）**：已直接讀取 `SpeechMessageProducts.ChurchReport.csproj`（直接 `PackageReference Include="DevExtreme.AspNet.Core"`）、`ChurchReport.MemberInfo.Tests.csproj`、`ToolUtility.Dataverse.Tests.csproj`（兩者皆直接 `ProjectReference` 到 ChurchReport）三個專案，確認排除範圍恰好等於依賴閉包，且 `SpeechMessageProducts.CI.slnf` 的 23 個專案與方案總數 26 個減去這 3 個完全對應，`NuGet.config` 的 `<packageSources />`（無 `<clear/>`）正確繼承使用者層來源而非清空繼承鏈。
- **06 兩項 Critical（基底別名 / CRM URI）**：`ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration` 已將「別名存在性檢查」改用 `baselineAliases`（不論 token 是否清空），「憑證值比對」維持用 `baseChannels`（僅比對有值的），兩個用途正確分離；`ValidateTestHost` 新增 scheme=https、`IsDefaultPort`、UserInfo/Query/Fragment 為空、CRM 另強制 `/XRMServices/2011/Organization.svc` 完整路徑，逐一比對新增的 8 組測試案例與既有 `SafeValues()`/`DevelopmentGuard_UnsafeSetting_RejectsWithoutSecret` 既有案例皆不衝突,無迴歸。
- **05b-C1 秘密驗證範圍**：`DeploymentCli.WriteVerificationResult` 與 `DeploymentEngine.NonSecretConfigurationMatches`/`Markdown` 的邏輯追蹤確認：`Plan()`/`Verify` 路徑從不呼叫 `secret` 委派（讀取數為 0）、metadata 相符但含秘密佔位時回傳 3 並明確揭露「未比較秘密／非輪替工具」，metadata 有差異時仍優先回傳 1，兩者皆無秘密值或已輪替值外洩於輸出字串。與既有 `Fixture.SecretReads` 計數器、既有 17 個契約測試交叉核對無衝突。

## 結論

本份新增/修正的 27 個檔案，就審查重點（跨使用者/教會隔離、資源釋放、機密外洩、例外責任、net48/net10 一致、守門假綠燈）逐項追蹤後，**未發現可驗證的新漏洞或假綠燈**；C-01、H-01、M-01、M-02、.gitignore、CI 過渡、06 兩項 Critical、05b-C1 的修正方向與其聲稱的邊界修復在程式碼層級均可驗證成立，沒有發現把既有核定的「過渡期範圍縮減」或「秘密責任排除」誤判為缺陷的情況，也沒有發現新引入的真實漏洞。**可予放行**。

殘留僅為既有設計取捨下的 Info 等級觀察（TOCTOU 競態窗口、既有 regex 型別掃描對 `record`/泛型約束的理論誤判風險），兩者皆非本次修正引入的迴歸，依指示僅記錄、不建議本輪擴大實作。完整 hosted CI 綠燈仍待使用者推送確認，本份審查不對 GitHub 執行結果做出宣稱。

---
SESSION_ID: e21eb2de-37b8-4b12-9382-ad65b7cbbb7b
