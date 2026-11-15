# 技術設計

## 邊界與資料流

`appointment` 觸發記錄／活動輸入 → `AppointmentPlugin` 或 `AppointmentSigningActivity` → `GoodShepherd.Core.Appointments.AppointmentSigningRule` → 服務查詢與通知介面；Shadow 將同一 `IOrganizationService` 換成既有 recorder，規則不認識 PluginInput。活動只負責參數與模式路由，Plugin 只負責 context 轉換與深度策略。

## 相容契約

- Plugin 沿舊 `AppointmentPlugIn` 的 `Depth > 1` 返回策略。
- `AppointmentWorkflowPlugin` 的活動入口保留 `ExecuteWorkflow`、`InputParameter`（string）、`OutputResult`（int）及 `InputAppointment`（appointment EntityReference）公開契約；舊碼雖未消費部分輸出，仍保留以避免註冊相容性破壞。
- 新活動使用 `ActivityShellBase` 的選填 `ExecutionMode`；空白為 Live，只有 `Shadow` 進 recorder。
- 不讀 `new_channel_access_token`，所有正常通知經共用 `NotifyComposedAsync`；Live 缺安全金鑰時在任何 SDK token 查詢前丟固定核定缺口例外。

## 生命週期與隔離

每次執行只借用 context/service，結果與 recorder 為操作區域；不使用 static 可變業務狀態、無界背景工作或跨請求快取。通知收件者必須由已驗證 appointment／教會／contact 關聯取得，跨教會、未綁定與重複穩定 ID fail-closed。所有活動、task、stream、timer 與 SDK 借用在外殼結束時排空／Dispose。

## 部署

先以固定 manifest 與新 DLL 的 plan → apply → verify（步驟全停用）核對，必要時 export／export-legacy；P5-08 Workflows 若連續兩次 UpdateAssembly 失敗停止並回報，不自行重啟或改 Deployment 工具。

## 逐行核對後的收斂

舊 Plugin 的 Create／Update 實際空參數，保留零通知；活動才依訊息與 Contains 分派。new_meeting_kind 決定請假／場地／一般標題，內文為 new_signing_content，僅請假附年度統計；不呼叫舊路徑已註解的 SetContent 或 DoesNeedSigning。來源教會由正式申請人獨立確認，先驗收件者再讀摘要或 Channel。摘要採 ThisYear、三個舊狀態及完整最多256列，重複正式 ID／分頁／超量／故障拒絕整批，且統計每次重新建立。

舊無引數 AppointmentSigningWorkflow 是診斷活動，與真正有引數的 AppointmentWorkflowPlugin 不等價；不移植 description 診斷污染及固定帳號通知，不將它假映射到新簽核活動。相容性研究與盤點保留此排除理由，P5-13 再登錄退役去向。

完整 Flex／sender／實際 Retry Key 以 expected.outputs 純值保存，供離線外殼重算摘要；lineCalls 保持既有部署 reader 格式，不擴充部署工具。這是離線完整訊息驗證，不代表目前 CLI 可觸發實機 Flex 比對，live 仍 deferred。
