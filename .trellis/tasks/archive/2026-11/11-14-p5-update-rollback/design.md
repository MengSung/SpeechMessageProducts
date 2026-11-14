# 設計
保留既有 Diagnostics 行為，只新增自身 AssemblyFileVersionAttribute 追蹤。
smoke 從當次對應 trace 取出嚴格四段版本後輸出，不持有或輸出原始 trace、秘密、SDK session；保留既有 bool 契約與 finally 清理。
部分信任 AppDomain 僅 Execution 與輸出目錄唯讀，父測試 finally 卸載；子域用真 Shell、Shadow 記錄與既有 member 鏈，全部合成依賴且禁止真HTTP。
重用既有部署命令與匯出回復，實機證據和離線證據分開記錄。規範補遺一併納入本次程式完整守門。
