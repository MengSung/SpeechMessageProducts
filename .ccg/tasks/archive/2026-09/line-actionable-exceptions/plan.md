# Exception.log 與 LINE 共用錯誤管線實施計畫

主會話 inline 執行。既有 worktree 不再建立分支。

- [ ] 在 ToolUtility/Diagnostics 新增 ExceptionDiagnostics：受控程序 owner、固定 Exception.log、5 MiB 輪替及五份備份、純 metadata、weak exception 去重、64 筆 LINE channel、一個 consumer、取消／停止與 fail-safe stderr。
- [ ] 在 ChurchReport/Logging 新增 ExceptionLoggerProvider：Error/Critical 直接委派共用 owner，不保留 formatter/state/scope。
- [ ] 在 ChurchReport/Services 新增 LineExceptionSender：受信任部署 token 與既有 recipient，HttpClient timeout、CancellationToken、request/response disposal，失敗不遞迴。
- [ ] Program.Main 最外層初始化／finally 關閉 owner；不受 DEBUG/DiagnosticsTrace 控制。註冊全域未處理／未觀察 task 事件，ILogger bridge。保留原 Trace 三檔 Release 關閉契約。
- [ ] HTTP middleware 放在標準錯誤處理器內側；BaseChurchController.HandleError 接共用 Report。相容 NotifyDefaultError 轉入 owner，原始文字不外送。
- [ ] 稽核 legacy terminal catch：共用 ERROR Trace 轉接安全事件；僅 Debug／靜默的功能失敗新增 Report，已恢復或往外 rethrow 的失敗由終端處理。
- [ ] 先寫行為測試，驗證 Debug/Release 落檔、LINE 故障仍落檔、取消分類、資料隔離、滿載、輪替、drain、middleware 順序與原例外。
- [ ] 更新永久文件、執行雙模型審查並處理 findings，歸檔任務且只提交本次範圍。
