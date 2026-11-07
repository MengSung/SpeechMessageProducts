# P2-00 Implementation Plan

> 使用 executing-plans 在目前隔離 worktree 逐項執行；使用者已授權計畫後直接實作。

**Goal:** 完成 00 §14 九步，不降低測試或額外擴張完成定義。
**Architecture:** 保留既有 API 與歷史證據，修復紅燈後移動文件，現頁只變 DATA。
**Tech Stack:** .NET net462/net10.0，net48/net10.0 xUnit；Node 原生測試；Python 位元組檢查。
**Spec:** 文件/規劃設計書/實作計畫-3.4/00_總覽與執行規則.md §14、§8。

- [ ] 1. dotnet test GoodShepherd.Core.Tests -f net10.0 -c Release，確認 19 個 NotImplementedException 紅燈，保存本機 TRX。
- [ ] 2. 實作 QrCodes/QrCodeContentCodec.cs 現有八入口，執行原兩目標測試；不新增圖片／整合。
- [ ] 3. 修 ArchitectureXmlTests.cs 的 BuiltPlugin 測試，只用 AssemblyConfiguration 決定目前組態；保留 PEReader Dispose。
- [ ] 4. git mv 驗收與四舊目錄；更新 Test-ProgressPage.cjs 的唯讀路徑、progress-page-contract、AGENTS 和活動任務。
- [ ] 5. 新增 Test-ProgressPage34.cjs：84筆、唯一ID、合法狀態、兩包限制、done證據、時間、跳脫、計數；先紅後綠。
- [ ] 6. 核對 hook／User Secrets 鍵是否存在（不輸出值）；流程 JSON 先比語意再正規化 UTF-8/CRLF。
- [ ] 7. 檔案說明、I18n、進度與 bytes focused 預檢，精確暫存本包變更，以 P2-00 訊息提交。
- [ ] 8. 在 C:/smv/<hash> 用 git clone --shared 建乾淨複本，執行 00 §8.1 九命令；保留輸出於原任務 evidence，失敗不推送。
- [ ] 9. 如實更新五項及回報／DATA。清理已解析且限定在 C:/smv 的複本；保持 P2-01、03 續作，不開始階段3。
