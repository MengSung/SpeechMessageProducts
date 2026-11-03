# 階段 0 外部審查停止回報

## 模式與範圍

單模型（使用者指定 ClaudeOnly）；73b6e6cf..ae29cb40。八個邏輯批次拆成 11 份，安全差異、提示檔及各份 SHA256 均已備妥。排除證據、基準、進度頁、封存任務、生成 C# 與二進位；敏感欄位已遮罩。本次沒有完成全階段審查，也不是雙模型審查或單模型降級完成。

P0-12A A 段 eb7ff61f、B 段 ae29cb40 已提交。B 段提交前的完整驗證紀錄仍在 P0-12A-license-CI.md；不能把該次 1089／1089 套用到目前含未完成審查修正的工作區。

## 停止原因

第 ③ 批 LineMessagingProcessor.Core 的自癒入口於 2026-11-03 23:48 啟動，兩次嘗試均因 Claude session limit 失敗。summary.json 為 ok=false、quotaBlocked=true、degradedFallback=false、dualModelComplete=false，沒有 completedBackends，入口結束碼 3；兩份 stdout 均為空，timedOut=false。這次是工作階段額度限制，與小組回報先前兩次逾時不同。

原始 probe 回覆額度於 Asia/Taipei 12:30am 重設。若按本次執行日換算，是 2026-11-04 00:30；這只是供應商提示，尚未驗證屆時可用，沒有排程或自動重試。依 00 §6.4 及使用者核定，已停止後續入口、程式修補與 P0-13，等待使用者處理。

## 分份狀態

| 分份 | 區域 | 結果 |
| --- | --- | --- |
| 01a | 守門工具與 CI，第一份 | 入口完成；沒有 Critical／High，1 項 Medium 已本機證實 |
| 01b | 守門工具與 CI，第二份 | 入口完成；1 項 Critical 已重現，修正未完成；另 1 項 Warning 待核對 |
| 01c | 守門工具與 CI，第三份 | 入口完成；1 項 High 尚待本機核對，1 項 Medium 及 2 項 Info 已記錄 |
| 02 | SpeechMessage.Localization | 入口完成；無 Critical／High／Medium，Low／Info 已記錄 |
| 03 | LineMessagingProcessor.Core | 額度限制，兩次皆無可用輸出，未完成 |
| 04 | SpeechMessage.Dynamics.Plugins | 已備包，未呼叫 |
| 05a、05b | SpeechMessage.Dynamics.Deployment | 已備包，未呼叫 |
| 06 | Development 防護 | 已備包，未呼叫 |
| 07 | 套件補修與圖片防護 | 已備包，未呼叫 |
| 08 | 小組回報修正 | 已備包，未呼叫 |

共 4／11 份產生可用結果；已完成入口均為 ok=true、reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false。「入口完成」不表示發現已修復或整體審查通過。

## Critical／High 與未完成修正

- C-01（01b）：RazorScanner.SkipBalanced 將逐字字串尾端反斜線誤認為逸出，會漏算後續 HTML 的中文顯示文字，造成 CJK 守門假綠燈。已取得功能紅燈：新 12 案例中 9 失敗、3 通過。改用 Roslyn token 邊界辨識後，工具單元測試為 87／87（保留原 75）；但完整 repo verify 仍失敗，唯讀探針列出 40 個既有 Razor 模板的 I18N415。目前懷疑候選修法對混合 Razor／HTML 的診斷判斷過嚴，尚未完成根因確認與相容性修正，不可提交或宣稱修復。停止時保留 RazorScanner.cs 與 RazorLexicalBoundaryTests.cs 的未提交變更，未提高 CJK 基準。
- H-01（01c）：外部審查指出 Test-PackageVersions.ps1 未逐段拒絕 reparse point／junction，可能違反工作樹讀取邊界。入口停止時尚未進行本機重現、修補或測試，現列為待核對的 High，不能視為已確認修復。
- C-01 本機證據位於 ignored artifacts/p0-review-fixes/razor/：red.trx 為 12／3／9，green.trx 為 87／87／0，probe.log 結尾為 Boundary failures: 40。87／87 只能證明工具單元案例，不能取代完整 verify 或 00 §8。

## Medium 以下與本機核對

- M-01（01a）：Debug tracer CI 只看結束碼，缺少零測試防護。用不命中任何案例的合成 filter、本機 --no-build 探測，確認 SDK 10.0.403 回傳 exit=0 並產生零測試 TRX；此發現成立。原正常 tracer 仍為 7／7。證據位於 ignored artifacts/p0-review-zero-probe/，未修改 CI。
- M-02（01c）：Find-DuplicateCode.ps1 的型別重名鍵未包含外層型別，可能把不同外層下的同名巢狀型別誤判重複。只有靜態審查發現，尚未本機重現。
- W-01（01b）：CommandRunner 未知例外可能穿透而輸出訊息。審查提供的是依賴其他未來程式錯誤的假設，尚無當前可觸發的案例；不直接增加 catch 吞掉錯誤。
- L-01（02）：精確零配置斷言可能受執行環境影響而假紅；目前沒有已觀察的偶發失敗，不放寬測試。
- I-01（01a）：BASE_COMMIT 固定為階段起點是明確核定行為，下一階段再更新，非本階段缺陷。
- I-02（01c）：刪除重複基準後首次執行會接受當下結果；是目前棘輪行為，須由基準審查把關。套件掃描排除清單與其他工具不一致，併 H-01 核對。
- I-03（02）：生成檔依使用者指示排除於審查輸入；既有雙目標測試及 generate --check 有本機證據，但不能將靜態審查擴大為模型實測。

## 審查包與接續方式

- 目錄：.ccg/dual-model-runs/20261103-232143-integration-p0-review-pack/。
- 壓縮包：同目錄旁的 20261103-232143-integration-p0-review-pack.zip。
- manifest.json 記錄固定 base／head、11 份差異與提示檔 SHA256、完成／失敗／待執行狀態；results/ 保留已完成模型輸出、失敗摘要與 probe 原始副本；STOP-REPORT.md 為本回報副本。原始執行目錄內容不改寫。
- 使用者恢復額度並指示繼續後，從 03 接續，避免重跑已完成的 01a、01b、01c、02；也可由使用者人工轉交剩餘安全提示檔。人工結果必須另註「Claude 單模型人工審查」。
- 解除停止後先核對並修完 Critical／High，重跑完整 00 §8。原包固定至 ae29cb40，新修正須另備差異及 SHA256，經同一 ClaudeOnly 入口複審，不能聲稱舊包已涵蓋。
- 第 ⑧ 批尚未執行；小組回報任務維持 review，不封存。使用者已確認頁面正常，圖表未逐項驗證。
- P0-13 尚未開始；goodshepherdback 清理前置確認保持未勾選。全程沒有 D365／LINE 連線，沒有讀取正式金鑰或 Six Labors 授權內容，沒有推送、合併或改寫歷史。

## 停止整理驗證

- 進度頁測試 3／3；僅修改 3.3 進度頁 DATA，版面程式與 3.2 凍結副本未動。
- Test-TextEncoding.ps1 -Scope Changed -Base 73b6e6cf：451 檔、115 排除、0 違規。任務計數檔 .turns.json 原缺檔尾 CRLF，只補換行並確認 JSON 值完全相同；不改原始模型紀錄。
- 本次修改及新增的來源／報告另逐位元組檢查 UTF-8 無 BOM、CRLF、檔尾 CRLF；git diff --check 通過。
- 11 份原差異 SHA256 未改變，提示檔與原始輸出副本均有雜湊；ZIP 已核對 CRC 及解壓內容與來源逐位元組相同。
- 本次只保存停止狀態，沒有新增提交，也沒有再執行後續審查或完整 §8；目前 Razor 候選修正未通過完整 verify 的狀態保持明示。
