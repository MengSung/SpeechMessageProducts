# P0-12 佈署專案最小版

沿用使用者已核定的 3.3 計畫、附錄 B／G；本環境 inline，不派實作或檢查代理。

## 檔案與責任
- SpeechMessage.Dynamics.Deployment/*.csproj、*.cs：嚴格 net10 CLI、User Secrets、獨立連線、清單／PE 解析、規劃與執行、受限輸出。
- SpeechMessage.Dynamics.Deployment.Tests/：保留原十七個方法，記憶體 IOrganizationService、暫存惰性 net48 DLL；不連網、不讀正式金鑰。
- d365/registration/plugins.registration.json：只有 DiagnosticsPingPlugin，task/Create/PostOperation/Asynchronous，預設停用；個資及 token 使用佔位。
- Directory.Packages.props：僅加入已核定 UserSecrets 10.0.0；SpeechMessageProducts.sln 納入兩個專案。
- .github/workflows/solution-ci.yml：新增 Deployment/net10 的獨立覆蓋率。
- .trellis/spec/backend/：佈署契約及 GitHub Environment secret 限制。
- 任務、回報、證據與 3.3 進度頁 DATA：記錄實際驗證。

## TDD 次序
1. 原十七項契約與可編譯 NotImplementedException 空殼，執行確認功能紅燈。
2. PE／清單一致性與 plan 冪等、身份衝突、佔位；取得綠燈。
3. apply 防過期／竄改、先備份、預設停用、rollback；取得綠燈。
4. CLI、白名單、secrets-only、連線釋放、smoke finally／期限、機密輸出；取得綠燈。
5. 補安全與資源邊界紅→綠、重構，再完整 §8 與 ≥80% 獨立覆蓋率。

## 執行邊界
每次指令只有一條 disposable 連線；連線例外只顯示型別與固定文字，真實 Exception 交共用入口。只允許 goodshepherdback 的完整 HTTPS Organization.svc；所有驗證注入假 connector。
plan／export 不解析 secret；secure 只記存在性及佔位。只有寫入當下解析 User Secrets，設定不得寫入輸出。SDK 讀取限該組件的中繼資料，拒絕重複身份、超量或不支援結構。
apply 檢查 plan 雜湊、DLL 雜湊、清單一致與當前指紋，再 export 並 flush，最後寫入。寫入指令同組織互斥，finally 清理鎖與連線。
smoke 只建立自己的 task，依非同步作業的 correlation id 找追蹤；有界等待、正常取消可中止，finally 刪除自己的 task，不輸出追蹤原文。

## 後續
本包驗證通過後提交，接著完成使用者 2026-11-03 核定插入的 P0-12A。P0-12A 提交後，再用自癒入口 ClaudeOnly 分七批階段審查；失敗或逾時備妥審查包即停。P0-13 只寫驗收步驟後停止。
