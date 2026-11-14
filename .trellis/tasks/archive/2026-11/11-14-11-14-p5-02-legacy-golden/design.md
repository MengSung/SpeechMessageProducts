# 技術設計
## 邊界
Compiler只讀明確舊根，尊重Compile及ProjectReference，禁止reparse/越界/備份/金鑰目錄，限數與大小，載入framework/SDK metadata；來源只在記憶體，emit byte[]。
語法樹改寫傳輸/檔案，未證明已攔截的API拒絕；不依名稱字串掃描註解冒充語意。執行在每案例獨立AppDomain，finally卸載、清scratch；逾時不能共用未排空domain。失效proxy只在執行域。同步完成recording傳輸不保留真token或header。
## 資料契約
05A三檔cases；seed穩定alias對應固定GUID，LINE僅於記憶體假ID。trigger含project/type及plugin或activity輸入，queryResults顯式覆蓋未知查詢。每case輸出name/entrypoint/writes/lineCalls/outputs/exception；legacy僅由實際執行生成。expected另含declaredDifferences，逐leaf差異驗證位置類別理由測試。
單次capture有上限，完成候選後秘密掃描、UTF8noBOM/CRLF原子發布。raw錯誤內容不回顯，僅型別及編譯代碼檔名。
## 守門修復
ProductTarget例外只限PluginHarness net48。duplicate父專案止於solution已列子專案根，每個子專案仍自行掃描，不改baseline。
