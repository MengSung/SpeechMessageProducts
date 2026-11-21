# 遺留 L2 流程樣板工具

## Goal

依 08A §4.6／§5 完成安全的自訂 Workflow 活動樣板建立、輸入契約驗證、執行及確定性清理，並在 goodshepherdback 實機一次取得「可行」或「不可行」結論。

## Requirements

- 全程只允許核准測試組織 `goodshepherdback`；正式組織不得連線。秘密只由既有 User Secrets／環境變數入口讀取，不得進入命令列、輸出、golden 或報告。
- 先唯讀取得一個停用舊流程的自訂活動 XAML 結構；只保存命名空間、ActivityReference 型別結構及固定值／主要記錄欄位繫結的形狀，不保存流程名稱、ID、業務值、原始 XAML 或敏感欄位。黃金檔固定為 `SpeechMessage.Dynamics.Deployment.Tests/Golden/WorkflowTemplate/activity-reference-shape.xml`。
- `WorkflowTemplateBuilder` 以工作流程活動完整型別名稱、主要實體及情境 `inputs` 建立只有單一活動、手動 on-demand、關閉自動觸發的新 XAML；AssemblyVersion 固定為 `1.0.0.0`，型別與參數契約以打包後 `SpeechMessage.Dynamics.Workflows.dll` 為權威，不新增套件。
- 多給、少給、未知、重複或型別不符的輸入一律拒絕；不得載入或執行 DLL 來讀取 metadata。
- `LiveActivityProbe` 只建立預配 ID 的本輪新流程，啟用、以 `ExecuteWorkflowRequest` 對本輪 seed 記錄執行、最多等待 60 秒並讀回作業結果；所有成功、故障與取消路徑均以不可取消 token 停用並刪除此流程，按 ID 驗證零殘留。不讀取、複製、啟用或修改舊流程。
- `LIVE-PREP-09` 只在輸入契約完整時允許活動繼續；沒有完整契約仍拒絕。樣板產生或啟用失敗使用既有原因碼 `LIVE-PREP-10`，不更改原因碼數字。
- 七個活動情境逐一比對部署 DLL 的 `[Input]`；只補缺少的情境 inputs，不改任何期望值。
- 08A 指定十個測試須以完全相同名稱存在且通過；至少保留可核實的先紅後綠輸出。
- goodshepherdback 實機一次使用資料最單純的活動（預定 `MeetingScanCountActivity`），操作「建立→啟用→執行→刪除」並保存安全證據。若 9.1 第一次拒絕啟用，只依錯誤修正一次；第二次仍拒絕則結論為「不可行」，七活動維持 `dormant`，原因改為 `P8-PROP-002 LIVE-PREP-10：<fault 碼>`，L3 僅驗步驟。
- 實機收尾必須有流程殘留 0、`verify=0` 且組織狀態為 `off`。

## Acceptance Criteria

- [ ] 08A §4.6 第 1～4 步及 §5 L2 全部完成；結論明確為可行或不可行。
- [ ] 十個指定 Deployment.Tests 案例以原名存在、先紅後綠且最後全通過。
- [ ] 七個活動的情境 inputs 與打包 DLL `[Input]` 名稱／型別一致；缺漏只補情境，不更動期望。
- [ ] goodshepherdback 實機操作一輪並驗證清理、`verify=0`、`off`；第二次啟用仍拒絕時依核定不可行規則結案。
- [ ] 一頁 `wp-reports/P8-L2-workflow-template.md`、`P8-decisions.md`、P7-PROP-007 進度 DATA 與 Trellis 父子紀錄一致。
- [ ] 程式／文件範圍分離，完整守門與提交、推送、`git ls-remote` 核對按 08A 及當下 guard 安全規則執行。

## Notes

- 測試不得連接 D365 或載入／執行 Workflow 活動；它們只使用合成 SDK 與 fixture。
- 實機拒絕與離線成功必須分開報告；不得把離線 XAML golden 或模擬 ExecuteWorkflow 稱為實機證據。
