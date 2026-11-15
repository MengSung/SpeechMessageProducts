# P5-02 fix 限時修正回報

2026-11-15 08:48 Asia/Taipei (UTC+08:00)：依 05A 修訂 2 與使用者三小時上限執行；08:14 開始，本次提前收斂，不再擴充工具。程式提交 e19d3baf、3e040d32；本回報為修正收尾，原 P5-02 驗收不重做。

| 項目 | 結果與證據 |
| --- | --- |
| 執行隔離 | 每情境獨立執行域；反射允許，Task、Thread、Timer有界追蹤、等待與清理；非同步void及無擁有者ThreadPool仍拒絕。此工具處理可信退役來源，並非敵意程式的作業系統沙箱。 |
| 診斷與來源 | Trace／Debug／EventLog導向有容量上限的scratch；舊BSU檔案寫入沿用scratch攔截。Line.Messaging SDK預設C#來源納入同次編譯，不載入舊DLL；Rest反序列化相容介面納入。 |
| 實際矩陣 | 核定36專案、29入口：Generated 1（WorkflowTemplate）、CompileFailed 28、BlockedByNetworkApi 0。Dynamics365Line.csproj零來源方案殼排除；Generated僅代表編譯成功。 |
| 出席目標 | PresentRecord generate退出1、CompileFailed，沒有候選檔與attendance/legacy.json；dynamic及保守切片遇反射無法證明可達範圍，完整閉包仍含未攔截的設定／CRM連線helper。不延長擴充；basis維持reading，各模組依D5-3閱讀逐條對照。 |
| 驗證 | 新增安全測試先紅後綠；Plugin最終302項通過。固定C:/smv/guard於3e040d32完整八項全通過，文件1571檔零失敗；UTF-8無BOM、CRLF與結尾CRLF通過。 |

原始矩陣：artifacts/p5-02-fix/compile-matrix.json，SHA-256 D0BA3A3119D1EE3D187C18C12898FB2021BCC145C80478FEABCBA40241228919；出席結果artifacts/p5-02-fix/presentrecord-generate.log。固定守門artifacts/p5-02-fix/guard-3e040d32-full.log，SHA-256 1E438F2D60860E346E6B5163349F6D849777EE9201A094AE97BF4E4B7DB0C9B9。

沒有D365／LINE舊入口實跑，不把合成測試當黃金。P5-03已採D5-19的live=deferred；接續P5-04。未發現已啟用的提交自動推送設定或有效hook，本次兩提交未自動推送；歷史五次來源仍無證據可歸因。收尾輕量檢查通過後才明確推送階段分支並ls-remote核對，不改寫已推送歷史。
