# 線 A 之後再說

此檔只記錄低於 Critical／High 或不在本階段的觀察，不代表擴充計畫或已取得實機證據。

| 時間（Asia/Taipei） | 工作包 | 觀察與範圍 | 後續建議 | 證據 |
|---|---|---|---|---|
| 2026-11-26 21:02 Asia/Taipei (UTC+08:00) | FS | quiesce 的 oversize 案例重複相同 ID；部分 duplicate-next 同時重複 cookie，可能由其他拒絕條件先攔下。既有同頁重複及合法控制仍保留，審查無 Critical／High | 日後以全部不同 ID 及不同 cookie 的精確故障候選加強各獨立上限的鑑別力；本輪不擴充 | ClaudeOnly 測試補查 SHA-256 21F6A141298C570D6D2DA3B0FCBDA15E663D0443512C7E0BB3F556D3DB03C8F6 |
| 2026-11-26 21:02 Asia/Taipei (UTC+08:00) | FS | UiImport 的 attributes-budget 使用 null 元素、options-budget 缺 Label，可能先觸發型別／完整性拒絕而非預算。恰2048欄合法控制已涵蓋，非全面拒絕假綠 | 日後給超量元素合法內容以分離容量條件，不放寬產品上限 | ClaudeOnly UI 測試補查 SHA-256 F758DD15E17D9C8D5B8DDAD37F3181ABDF441FDCB837C3C695E6DDC7C125904B |
| 2026-11-26 21:02 Asia/Taipei (UTC+08:00) | FS | QuiesceSourceBinding fixture 清理案例目錄後留下空的固定 Temp 父目錄；UiSolutionDeployment 的執行緒配置量測可能受首次 JIT 影響 | 日後改善測試 owner 的空目錄整理與配置量測暖身；不宣稱現有量測為長期效能基線 | 同上兩份單模型補查；未讀取 secrets、未連實機 |
| 2026-11-26 21:02 Asia/Taipei (UTC+08:00) | FS | Plugin 新組織未啟用步驟時仍可規劃 assembly／type，沒有 CreateStep／ReconcileImages；缺旗標測試直接覆蓋型別不存在分支 | 契約目前限制執行步驟；日後可補型別已存在分支的直接控制，不新增本包完成條件 | ClaudeOnly 修正複審 SHA-256 877C93DF67B264E800DD1D4515718D010ED0FA3612548266CE3A79D5D939E4D4 |
