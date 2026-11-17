# 階段 7 核定設計引用

設計已由使用者核定，直接引用 07A §4～4.7，不另提方案。組織清單、ProductionGuard、SwitchTarget 純函式、具雜湊/現況指紋的 plan、可反向回復的 SwitchLog、流程改指共用 ActivityProbe 邏輯、唯讀 observe、只補空白的 ChurchDefaults，各在指定單元實作。
MemberNotifyRelay 只保存四欄識別碼/數值；來源教會從記錄推導，主機重讀並驗證教會後才推播。每筆有獨立 DI 範圍、無跨教會可變狀態或無界資源；停止時排空再釋放。
LiveCheckRunner 沿用 Shadow 投影/比對，測試流程為 SMV-TEST- 複本，finally 清理與回復使用未取消 token。組織寫入單一 owner；秘密僅由 User Secrets 在使用瞬間讀取。
正式組織情境全部以合成名稱/假服務驗證，不連正式環境。安全/回復優先，時間盒依 D7-28 處理。
