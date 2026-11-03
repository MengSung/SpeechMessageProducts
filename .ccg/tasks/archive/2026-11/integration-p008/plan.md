# P0-08 Localization 雙目標實作

- [x] 讀 AGENTS.md、00、01、附錄 C.1／E 及既有工具契約；永久三條件命名規則已由 cea2958d 提交。
- [x] 新增 SpeechMessage.Localization 與雙目標測試，兩者本體開啟嚴格模式及 XML，測試明確 IsTestProject=true；沒有個別 NoWarn。
- [x] 保留原八方法，使用只丟 NotImplementedException 的空殼分別跑 net48／net10.0 真正紅燈。
- [x] 實作 TextKey、ISystemTextCatalog、SystemTextTable 及 SupportedCultures；僅系統文字，不做教會覆寫、時間或主機開放檢查等階段 1／2 功能。
- [x] 加真實三筆 SharedText.resx，由既有工具產生 SharedText.g.cs；不嵌入 resx／衛星組件，生成檔提交。
- [x] 候選完整成功才發布；生成鍵／語系重複拒絕，兩個相同顯示值但不同鍵均保留；建立時複製並凍結資料，不保留呼叫者陣列。
- [x] net10 使用 FrozenDictionary、net48 使用 ReadOnlyDictionary，兩者共用唯讀介面；一百個實際執行緒各讀一萬次，全部 join／drain；net10 另量每次查詢零配置。
- [x] 兩目標測試及覆蓋率 >=80%；完整 §8、產生檔非零檢查數、編碼與重複掃描，報告後提交，才進 P0-09。

## 檔案清單
- SpeechMessage.Localization/{csproj,TextKey.cs,ISystemTextCatalog.cs,SystemTextTable.cs,SupportedCultures.cs,Resources/SharedText.resx,Resources/SharedText.g.cs}
- SpeechMessage.Localization.Tests/{csproj,SystemTextCatalogTests.cs}
- SpeechMessageProducts.sln；本包任務、證據、回報及 3.3 進度 DATA。

## 決定
- 未知語系（含空值）回 zh-TW，不建立動態快取；字典使用 Ordinal 鍵與 OrdinalIgnoreCase 語系識別。
- 支援語系只提供 Released／Shell／Pseudo 中繼資料，不代替後續主機的語系開放或授權檢查。
- 所有例外訊息用固定英文診斷；使用者顯示文字只放 resx，C# 繁中 XML 註解不列入棘輪。

## CA1716 新停止（2026-11-03）
原 CA1707 已由永久規則解除。兩目標還原成功；net48 真正執行 33 案例全部因 NotImplementedException 失敗，紅燈證據已保存。net10.0 編譯 ISystemTextCatalog.Get 時報 CA1716（跨語言保留字），尚未開始測試。依 §9.5／§9.6 停止，沒有建立 resx／g.cs 或加入方案，沒有寫實作或停用其他分析器。建議尚未發布的取字 API 統一改 GetText，原八個測試方法名稱不變；此改名需使用者核定，設計原檔仍不動。

## 完成
58245c6f 補修後重新取得雙目標各 33 個功能紅燈，再實作各 33／33；覆蓋率 net48 94.87%／net10 100%。真實資源生成檔檢查 1 檔，完整方案 837／837；原八方法及歷史證據保留。GetText／richMenuAlias 同步核定計畫，沒有 CA1716 抑制。
