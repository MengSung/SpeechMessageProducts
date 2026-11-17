# 分析結論與實作邊界

- 自癒入口：20261104-131202-p013-apply-fault-diagnostics-analyzer。
- reviewMode=dual；dualModelComplete=true；quotaBlocked=false；degradedFallback=false。
- Gemini／Claude 均支持僅輸出數字診斷並保留原回報、退出碼與資源釋放。
- 兩方的 Critical 為對尚未實作危險模式的預警，不代表已存在資料外洩；已以強型別讀取 Int32、先上報後補診斷、獨立防護區塊實作。
- Gemini 建議 HashSet 與深度雙防護；本次採硬上限四次的非遞迴 for，已用循環注入驗證，無需配置 visited 集合。
- 原 46 例保留。新增 6 例功能紅燈：5 失敗（缺數字碼）、1 通過（缺失 Detail 的既有相容性）；補修後全套 52／52 通過。
- 所有故障、秘密與 SDK 服務均為記憶體假資料；Codex 未連線 D365／LINE。
- 此補修只讓下一輪使用者操作取得數字錯誤碼；真正伺服器根因與 P0-13 實機驗收仍待確認。

## 使用者核定的伺服器唯讀查核（2026-11-04）

- 第二輪 apply 回報兩層 `0x80040216`；使用者另核定 Codex 直接操作已開啟的虛擬機查看事件。此次僅用 Windows GUI，不呼叫 D365／LINE API，不匯出事件全文。
- 應用程式記錄的 `MSCRMWebService`／18176（台灣時間 14:16:19）顯示 `pluginassembly` 的 Create／Stage 30 失敗；`System.FormatException` 由 `String.Format` 進入 `TargetFrameworkVersionValidator.ValidateInternal()`，呼叫鏈含 `PluginValidatorBase.Validate()` 與 `ValidateAssemblyMetadata(...)`。
- 另外唯讀查看 `Microsoft.Crm.ObjectModel.dll` 的詳細資料，元件版本為 `9.1.49.21825`。
- 續查伺服器 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll` 的內容／詳細資料，檔案與產品版本均為 `4.8.4795.0`。這是檔案版本證據，不代表已驗證沙箱程序實際載入版本，也不能當成 smoke 的 FrameworkFileVersion 驗收。
- 證據已定位框架版本驗證階段，但未呈現可接受的版本上限；不能將失敗歸因為未安裝 .NET 4.8。現行官方地端文件列出 4.6.2～4.8，卻未在該段指定所需更新版本，不能直接套用到現場版本。
- D21 已核定 `TargetFramework=net48`；是否採 4.6.2 相容建置，或維持 net48 核對支援更新，須由使用者決定。尚未修改產品目標、守門、伺服器設定、服務或資料；未重試 apply，verify 與 smoke 均未執行。
- 官方來源：https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/visual-studio-dot-net-framework?view=op-9-1 。原始碼專案目前仍明定 net48。

## 使用者途中提交與收尾條件

- 已核對並保留使用者手動提交 `714465fd`：清單唯一差異為 goodshepherdback 的 false→true，其餘為既有診斷任務、文件與審查紀錄，產品程式未改。
- 清單 true 已在 HEAD，不能再稱未提交修改。驗收結束仍須設為 false、完成停用驗證，並另以新提交保存 false 後才檢查此檔工作目錄乾淨；目前不改清單，也不宣稱伺服器停用完成。
