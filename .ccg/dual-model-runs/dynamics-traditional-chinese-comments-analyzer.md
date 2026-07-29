# CCG analyzer Task: dynamics-traditional-chinese-comments

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Dynamics C# 繁體中文註解與純 UTF-8 改造分析

## 角色與目標

請以 analyzer 角色檢查目前工作樹。使用者要求：自 `58657c0f`（Dynamics 無 SDK Gateway Phase 0）起到目前 HEAD，以及工作區尚未提交的變更中，凡由本任務新增或修改、目前仍存在的 `.cs` 檔案，都必須具備繁體中文且有實質深度的註解，並以純 UTF-8（無 BOM）與 CRLF 儲存。

這是註解與檔案格式改造，不得改變執行行為、公開 API、序列化契約、測試語意或效能路徑。

## 強制品質邊界

1. 註解要優先說明：責任與信任邊界、Session/Profile/Token/Credential/Tenant 隔離、資源擁有與 Dispose、取消與逾時傳播、重試與回壓、容量與 lease fence、記憶體/Socket/Timer/Handler 生命週期、錯誤與記錄遮罩、效能取捨。
2. 不要逐行翻譯顯而易見的程式，也不要以大量空泛註解降低可讀性。
3. DTO、enum、常數與介面以精確 XML summary 說明契約；複雜類別、非直觀分支與測試需要補充「為什麼」及阻擋條件。
4. 測試註解要描述被證明的隔離/生命週期/效能契約、故障注入與基線，不要只重述 Arrange/Act/Assert。
5. 所有新增註解使用繁體中文；程式識別字、協定名稱與必要技術詞可保留英文。
6. 所有檔案最後必須是嚴格 UTF-8、無 BOM、CRLF、結尾有換行、沒有 U+FFFD 或常見 mojibake。
7. 零容忍 Session Leakage 與 Memory/Resource Leakage；註解不得掩蓋現有可信風險。若閱讀時發現 Critical 行為問題，必須另列，不要以註解修飾。
8. `DynamicsAccess:Package01FeeReadsEnabled` 必須維持 `false`，不得因本次工作啟用。

## 盤點方式

請自行使用下列等價命令取得範圍，並排除已刪除檔案：

```powershell
git log '58657c0f^..HEAD' --name-only --format= -- '*.cs'
git status --porcelain=v1 --untracked-files=all -- '*.cs'
```

目前本地初步盤點共有約 103 個現存 `.cs` 檔，其中至少 12 個完全沒有繁體中文註解。請不要只看這 12 個；也要檢查新建 Dynamics 專案中仍為英文的 XML 文件與安全/生命週期註解。

## 請輸出

1. 建議的分批順序（核心 production、host/integration、tests/soak/diagnostics）。
2. 每批的檔案清單或可重現的選取規則。
3. 每個高優先檔案應補註解的具體類型/方法/分支，以及註解應涵蓋的技術重點。
4. 哪些既有英文註解應翻成繁體中文，哪些自明 XML 註解可保留或精簡。
5. 如何驗證「沒有行為改變」、純 UTF-8 無 BOM、CRLF、沒有亂碼。
6. 任何 Critical / Warning / Info 風險，尤其 Session/Token/Tenant 隔離、資源生命週期、測試假陽性與大量註解造成維護成本的風險。

請以繁體中文回答；只做分析，不修改檔案。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.