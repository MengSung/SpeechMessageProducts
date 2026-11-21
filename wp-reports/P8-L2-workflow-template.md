# 遺留 L2：離線活動輸入契約與身份檢核

**結論：依 2026-11-21 核定的 08A 修訂 1，已縮減結案（證據核對完成於 2026-11-21 22:32 Asia/Taipei (UTC+08:00)）。** 本結論只涵蓋可編譯、具離線測試且全方案 Release 通過的 PE 輸入契約、shape 投影、strong-name 身份檢核及相關 CLI／測試；不代表完成 Workflow XAML 樣板或 D365 9.1 實機驗收。

## 需求 → 證據 → 狀態

| 需求 | 證據 | 狀態 |
|---|---|---|
| 讀取封裝 Workflow DLL PE metadata，驗證活動輸入契約及 strong-name 身份 | build 22 DLL FileVersion `1.0.0.22`；七活動 metadata 有輸出，六個既有核定情境符合契約；`AppointmentSigningActivity` 無核定情境，未猜測補值。`p8-l2-pe-contracts-build22-20261121-191330.log`、`p8-l2-strongname-red-20261121-192441.log`、`p8-l2-strongname-green-20261121-192733.log` | 通過（離線部分） |
| shape 投影、指定活動選取、輸入／輸出邊界及敏感值遮蔽 | projector／selection、CLI、PE reader 具合成測試；舊投影器誤分類及診斷結果均留有紅綠與唯讀輸出，未把不完整診斷升格為 golden | 通過（離線部分）；golden 未完成 |
| Workflow XAML 樣板、PREP-09／10 與新流程 lifecycle | 沒有可核准的真實雙繫結 shape golden；XAML builder、probe lifecycle 及完整十案未完成。`88f806bc` 曾將未完成的 `WorkflowTemplateBuilder.cs` 納入版本庫，與修訂 1 的 L2 範圍決定不符；本次依核定指示將 HEAD 版本備份至 `%USERPROFILE%\.speechmessage\evidence\p8-l2-wip\WorkflowTemplateBuilder.cs`（SHA-256 `CB2A9D0CF3D49A69F67B6418FE26ADE339CF2514E17329FDD130052B05D039FF`）並自追蹤檔案移除。先前的日期版備份與差異檔保留未動 | 移至好牧人 9.1 上線前作業 |
| 七個活動的完整情境對照及 9.1 實機建立／啟用／執行／清理 | 六個既有情境通過 build 22 契約單項；Appointment Workflow 情境缺失。沒有本輪 workflow 實機寫入；既有最新唯讀 verify（20:15）exit 1、僅 `UpdateAssembly`，不能宣稱 `off` 或 `verify=0` | 移至好牧人 9.1 上線前作業；七活動維持 dormant |
| 全方案 Release 與程式品質 | L2 單元完整 Release：23 個測試目標合計 7,535/7,535，exit 0；`C:\smv\guard-p8-evidence\p8-l2-full-solution-release-final-20261121-2150.log`，SHA-256 `DABF2986589CC372881E00881A402B1160DA90C3EF017CED87FD815FEC5BEC31`。隨後唯一 `C:\smv\guard-p8` 對乾淨 checkout `477c8136147d7168ebc8faf869a8e1761004db93` 完整 §8.1 守門通過；逐命令 Release 輸出合計 23 個目標、7,542/7,542，Debug tracer 7/7、ProgressPage 28/28、文件說明 1,891 檔零失敗。守門紀錄 `C:\smv\guard-p8-evidence\guard-477c8136-full.log` SHA-256 `D7A98FADFCB88D0BE86C9A9B022890DEBC3DD779F3D9D85FE8AC141DEBA20ED5`；完整輸出 `C:\smv\guard-p8-evidence\guard-477c8136-full-output.log` SHA-256 `7D1003DF99428E214E5FB91FC8CFB3BE5CDA6532408F79FD41DFC0D649941F23`。來源掃描 deleted-path 回歸紅／綠：`p8-l2-source-no-endpoint-red-20261121-2123.log`、`p8-l2-source-no-endpoint-green-20261121-2130.log`；秘密掃描綠燈 `p8-l2-source-secrets-green-20261121-2145.log` | 通過 |

## 明確延後

P7-PROP-007 改為「離線部分完成；樣板產生與實機移至上線前作業」。本次不連 D365，不讀取 workflow shape、不改 `LiveActivityProbe`、不更動 registration tier/evidence；七個 Workflow 活動仍為 dormant。好牧人 9.1 上線前須補齊真實脫敏 golden、XAML builder、PREP-09／10、十個指定測試、七活動情境及實機完整生命週期，並確認組織回到 `off`、`verify=0`、殘留 0。

## 收尾狀態

2026-11-21 21:34 的 L2 縮減範圍核定時間不是完成時間；當時全方案 Release 尚未完成。該時間戳的結案敘述已於 2026-11-21 22:37 Asia/Taipei (UTC+08:00) 更正；本次縮減結案以已保存的全方案 Release 輸出、22:21 通過的完整守門及 22:32 保存的完整輸出為證。Release 檔名為 `p8-l2-full-solution-release-final-20261121-2150.log`，其實際檔案修改時間為 21:31；不將檔名中的數字當成測試事件時間。守門涵蓋 L2 程式提交 `2ace3199581ba0479233fea66fd5a63b331b430f`；守門 checkout `477c8136` 另含後續 L1 封存 JSON 修正，不改寫歷史。2026-11-21 22:45 Asia/Taipei (UTC+08:00) 紀錄同步後 `git diff --check` exit 0、進度頁 28/28、檔案說明檢查 1,891 檔零失敗、封存 task JSON 可解析；所有六份自有變更檔逐 byte 為 UTF-8 無 BOM、純 CRLF、檔尾 CRLF。報告、封存 Trellis 子任務、父清單、`P8-decisions.md` 與進度頁 DATA 已同步；本紀錄收尾提交、推送及遠端 SHA 核對仍待執行。

2026-11-22 07:41 Asia/Taipei (UTC+08:00)：依使用者核定修正 L2 範圍不一致。`88f806bc` 的實際檔案清單包含 `SpeechMessage.Dynamics.Deployment/Live/WorkflowTemplateBuilder.cs`，雖然修訂 1 的 B 明定未完成／無測試覆蓋的 builder 不提交，封存任務紀錄也寫明不提交；本次不改寫既有歷史，將 HEAD 版本另存至 `%USERPROFILE%\.speechmessage\evidence\p8-l2-wip\WorkflowTemplateBuilder.cs`，SHA-256 `CB2A9D0CF3D49A69F67B6418FE26ADE339CF2514E17329FDD130052B05D039FF`，並以後續 `P8-L2 fix:` 提交移除追蹤檔案。既有日期版備份及 diff 備份均保留。此為紀錄與範圍更正，不新增組織操作；本次 scoped check 不等同完整 §8.1 守門、L2 重新驗收或遠端推送。
