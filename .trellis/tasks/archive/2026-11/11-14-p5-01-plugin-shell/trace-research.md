# PluginTraceLog 容量查證

2026-11-14 Asia/Taipei (UTC+08:00) 唯讀查 Microsoft 官方 Logging and tracing 文件：
https://learn.microsoft.com/en-us/power-apps/developer/data-platform/logging-tracing

官方說明 ITracingService 會在成功或拋出例外後，把當次累積資訊寫入新的 PluginTraceLog；每次 Trace 為一行，文字上限 10 KB，超限刪除較舊行、保留最近行。

本包依 05A §4.3 使用合計不超過 8,000 UTF-16 字元的版本化影子行，計入前綴與 CRLF，保留 overflow 及最後 end 標記；不能用多次 Trace 規避整次容量。這是本機契約測試，並未證明 goodshepherdback 的 D365 9.1 實際保存政策；依 05A §8 在更新回復前置再做實機確認。若實機截斷，不得將不完整紀錄當成零差異。
