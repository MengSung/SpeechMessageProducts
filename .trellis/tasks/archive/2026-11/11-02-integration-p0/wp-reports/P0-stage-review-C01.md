# 階段 0 審查修正 C-01：Razor 程式邊界

依使用者 2026-11-04 核定，保留 d25fd2cc 並取代其中純 C# token 候選修法。計數範圍不擴張：運算式或純程式區塊內的範本標記仍不計數。

## 根因與修正

C# 詞法器不能直接跨過 Razor @<text>／@<div>。改為單 token 推進 C#，在 Razor 轉換處切換到標籤配對；支援同名巢狀、void／自閉合、屬性內運算式、@: 行、程式區塊內直接 HTML 與遞迴轉換。C# 逐字、插值、原始字串、字元常值及註解仍由 Roslyn 處理。

每次計數只保留本次來源與局部堆疊；沒有來源／身分／帳密快取。混合遞迴上限 96、標籤／括號堆疊上限 512，未知或截斷邊界回 I18N415 固定文字，未輸出來源。

## TDD 與驗證

- 保留原 75 案與前次新增 12 案，再加 18 個混合語法案例與 3 個截斷案例。
- 功能紅燈：Razor 33 案中 14 失敗、19 通過；不是編譯失敗。
- 綠燈：工具全部 108／108，無跳過；完整 repo verify 通過，84 個追蹤 Razor 零 I18N415，generate --check 實查 2 檔。
- 原始 TRX／輸出只在 ignored artifacts/p0-stage-review-20261104/，沒有真實照片、個資、連線或金鑰內容。

## CJK 逐檔比較

以 ae29cb40 的原始掃描器與修正後掃描器，對完全相同的 84 個目前追蹤 Razor 執行唯讀比較；沒有改任何產品模板。Razor 合計 467→421，ChurchReport 全專案 3682→3636，其餘專案不變。以下九檔皆含程式內範本委派；舊手寫引號／括號處理提早結束程式範圍，將後段模板誤算為顯示文字；本次完整配對模板，移除既有範圍定義本來就要排除的誤算。沒有任何計數增加，既有 CJK 基準維持 3685，不更新基準。

| 檔案 | 修正前 | 修正後 | 原因 |
| --- | --- | --- | --- |
| SpeechMessageProducts.ChurchReport/Views/Dedication/DedicationFeeView.cshtml | 6 | 3 | 程式內範本邊界完整配對，排除原本誤算 |
| SpeechMessageProducts.ChurchReport/Views/Dedication/DedicationFeeViewWeb.cshtml | 7 | 4 | 程式內範本邊界完整配對，排除原本誤算 |
| SpeechMessageProducts.ChurchReport/Views/Dedication/KeyInDedicationFeeView.cshtml | 4 | 2 | 程式內範本邊界完整配對，排除原本誤算 |
| SpeechMessageProducts.ChurchReport/Views/Dedication/KeyInDedicationFeeViewWeb.cshtml | 5 | 3 | 程式內範本邊界完整配對，排除原本誤算 |
| SpeechMessageProducts.ChurchReport/Views/DedicationAudit/DedicationFeeAuditViewLine.cshtml | 8 | 3 | 程式內範本邊界完整配對，排除原本誤算 |
| SpeechMessageProducts.ChurchReport/Views/DedicationAudit/DedicationFeeAuditViewWeb.cshtml | 7 | 4 | 程式內範本邊界完整配對，排除原本誤算 |
| SpeechMessageProducts.ChurchReport/Views/FeeManagement/Fee.cshtml | 8 | 1 | 程式內範本邊界完整配對，排除原本誤算 |
| SpeechMessageProducts.ChurchReport/Views/FeeManagement/Present.cshtml | 13 | 2 | 程式內範本邊界完整配對，排除原本誤算 |
| SpeechMessageProducts.ChurchReport/Views/Home/FeeView.cshtml | 12 | 2 | 程式內範本邊界完整配對，排除原本誤算 |

## 審查狀態

本修正尚待第 09 份 ClaudeOnly 複審；不能把至 ae29cb40 的原審查包說成已涵蓋。最後另跑完整 00 §8，尚未宣稱階段完成。
