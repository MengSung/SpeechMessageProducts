# P2-01 方案登錄與永久規則

最後更新：2026-11-07 08:47 Asia/Taipei (UTC+08:00)

## 問題與原因

專案檔已建立但方案只有原30項。先以csproj跑focused測試並非加入方案的必要前提，沒有理由延後方案可見性；本次立即補齊，仍不把骨架當成驗收完成。

## 實際結果

- dotnet sln add加入Hosting及Hosting.Tests；成員檢查由缺兩項紅燈轉為32項綠燈，原30名稱／路徑／GUID保留。
- 新項目各一筆、Debug／Release ActiveCfg及Build.0存在。
- dotnet build SpeechMessage.Hosting.Tests/SpeechMessage.Hosting.Tests.csproj -c Release --no-restore：0警告／0錯誤；不是功能測試通過。
- 證據：../evidence/solution-before.json、solution-after.json、solution-registration-build.log。
- 使用者核定未來新增csproj同一步加入sln；規則保存於AGENTS.md及backend/directory-structure.md。

## 執行限制與未完成

目前inline限制；兩個已啟動代理停止並關閉。未修改產品，僅一名留下進度頁12通過／3失敗基線。現行外部流程仍強制，分析084232與審查084422均quotaBlocked=true、dualModelComplete=false、degradedFallback=false、completedBackends空；無可採納外審，不能宣稱成功。未連D365或LINE、無外部資料／資源變更。P2-01功能與全部P2驗收保持未完成。

## 補充品質證據

檔案說明守門先7份CrlfRequired；僅正規化7份Hosting來源／專案檔後1257檔0失敗。進度頁仍12通過／3失敗（P1固定快照），未刪除或放寬斷言。上述紅燈、綠燈輸出保留在evidence/solution-registration-*。
