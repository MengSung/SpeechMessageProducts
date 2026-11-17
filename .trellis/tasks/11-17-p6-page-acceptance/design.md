# 驗收隔離與資源設計

Node腳本擁有Edge、HTTP session、主機子程序及本輪記憶體憑證；只序列執行兩輪。SDK資料fixture借用正式Deployment白名單連線，建立的ID立即加入精確owner清單，finally逆序刪除並讀回確認零殘留。不把既有會員資料或token輸出。實際秘密只在程序記憶體取用；不可把密碼放CLI、截圖或輸出。
主機原始stdout僅在有界記憶體觀察啟動狀態，證據只記固定狀態與合成資料。對照ID映射成同一代號、時間只比存在，其他逐欄原值。
已用合成假token重現D6-20環境注入與Development UserSecrets provider守門衝突；等待使用者核定，不能偽造provider或繞過檢查。等待期間準備不受影響的fixture、腳本及清理。

2026-11-17 10:30 Asia/Taipei (UTC+08:00)：使用者已明確核定選項1與DPAPI備份條件，原設定逐位元組備份repo外，測試token只由記憶體直接寫指定User Secrets，finally還原比對；已完成兩輪並獨立核對還原一致。前述等待已解除，不改產品與安全守門。
