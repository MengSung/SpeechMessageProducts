# P1-09 設計與執行計畫
- 沿用ResourceCatalog的key/同檔唯一/參數/翻譯驗證，不另實作第二份解析器；原PseudoLocale與Placeholders保留。
- ResourceSet.LoadRepository共用既有方案邊界，物理檔身份唯一、最多4096 resx/64MiB全部候選；neutral/translation先分類再Load，不讓同key跨組被字典覆蓋。CheckAllGenerated與verify共用同份快照，避免掃描漂移。
- ResourceVerifier只持操作區域集合，回傳完整不可變報告；原文化+從zh-TW生成的qps長度同驗。UTF16 code-unit計數與現有Sender守門一致且比Rune保守；偽語系補字仍按既有Rune公式，不改公式。
- LengthLimits嚴格讀JSON，固定Sender20/ChatBar14不可放寬；最長教會名必非空/有界，受限模板只允許churchName，未知參數拒絕而不猜短值。所有替換單次Regex match，不重解析插入值。
- 英文分母為全部唯一中性key，分子為存在且非空白en翻譯，en-US不冒充en；Invariant百分比固定兩位，不因缺譯失敗。
- 關鍵字跨所有原始語系保守驗同字異意圖；不檢qps。重用純文法，不引用host/credential；為避免工具依賴產品新加ProjectReference形成工具層倒置，保留工具區域正規化實作並以與產品相同向量測試鎖定。
- 已發現正式E4字面樣本實為14字；完整保留此字面作保守樣本。現有sender小幫手的pseudo替換後21字；先保留真紅，再將顯示固定後綴縮為助手，14+2+1補字+2括號=19，不改參數/業務/20上限或pseudo公式。這是為滿足原上限的資源調整，不改正式規範與已核定政策；不修改原計畫或縮小名稱預算。
- 分步測試紅綠、資源重生與P1-05流程回歸；主會話自審/審查紀錄，完整證據後本地commit及僅子任務封存。
