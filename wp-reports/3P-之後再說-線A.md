# 線 A 之後再說

此檔只記錄低於 Critical／High 或不在本階段的觀察，不代表擴充計畫或已取得實機證據。

| 時間（Asia/Taipei） | 工作包 | 觀察與範圍 | 後續建議 | 證據 |
|---|---|---|---|---|
| 2026-11-28 15:09 Asia/Taipei (UTC+08:00) | FS | 原始formxml reader的ClaudeOnly未發現Critical／High；轉換XML超64KiB、descending、多餘元素、預先alias等已有fail-closed但尚無各自負向案例；UI替身轉換位於disposal斷言前 | 後續補精確單條故障控制；本輪不放寬現有上限或改完成條件 | 補充審查輸出SHA-256 8A6A4A6F9A0604F94663A520709871F9282D335A63A80228BC1974555CFC0ED5 |
| 2026-11-27 18:50 Asia/Taipei (UTC+08:00) | F3 | DataverseTrace 並行測試曾有一次依 JSONL acquire／return 索引順序判為 lease 重疊；後續 2 次定向、20 次重複及完整 9,218 項均通過，reviewer 推測 return trace 事件可能晚於 Idle 入列；未有失敗時的索引，不能確認原因，也沒有證據證明服務被同時交付 | 日後以確定性同步點直接量測同一 fake client 的同時執行，或讓 trace 順序明確代表可重用邊界，再執行並行／重複測試；不要因觀測測試排序而放寬產品隔離斷言 | 首次失敗 SHA-256 ED0F666FC487424FA9233DA0B58306926C7A47EB9ED58240FF0872960871AD5A；20 次定向重跑 SHA-256 95DCE738B5CEB8D5DC89A802C80D764826B78AA26C2D10DA544A2A13FBFB0916；ClaudeOnly run 20261127-183305-3p-f3-lane-a-reviewer；非 Critical／High，非已修缺陷 |
| 2026-11-27 13:22 Asia/Taipei (UTC+08:00) | F3 | 登錄型別或狀態被人工改動會使本產品整池fail closed；既有登錄slot帶tenant／used／重號的分支本次為程式審查，未逐一故障注入 | 後續runbook說明與精確負控制；本包不新增型別／狀態Update功能或改核定政策 | ClaudeOnly修正複審SHA F6C31E1B90DBA09F8CA673DAEBEF109CDCE44D138564B7D12A57468F8BADCC4A；無Critical／High |
| 2026-11-27 12:56 Asia/Taipei (UTC+08:00) | F3 | 外產品告警探測TopCount=1時，空白產品列可能遮住另一筆真正外產品；本產品載入與解密仍精確隔離，非隔離失效 | 日後補空白與外產品共存的告警正控制及有限完整探測；本包不擴充 | ClaudeOnly Channels補查 SHA-256 D42849796F6140D1D63166C3AD1D06D17321621979262204E3C4551BEE9464E7 |
| 2026-11-27 12:56 Asia/Taipei (UTC+08:00) | F3 | Directory_FailureRetry的第四次解密故障落在第一筆第二段，未直接覆蓋完成一筆後才失敗與舊secret不可沿用 | 日後補精確故障位置及舊secret否定斷言；現有候選原子發布與資源歸還控制保留 | 同上單模型補查；低於High，不新增本包完成定義 |
| 2026-11-26 21:02 Asia/Taipei (UTC+08:00) | FS | quiesce 的 oversize 案例重複相同 ID；部分 duplicate-next 同時重複 cookie，可能由其他拒絕條件先攔下。既有同頁重複及合法控制仍保留，審查無 Critical／High | 日後以全部不同 ID 及不同 cookie 的精確故障候選加強各獨立上限的鑑別力；本輪不擴充 | ClaudeOnly 測試補查 SHA-256 21F6A141298C570D6D2DA3B0FCBDA15E663D0443512C7E0BB3F556D3DB03C8F6 |
| 2026-11-26 21:02 Asia/Taipei (UTC+08:00) | FS | UiImport 的 attributes-budget 使用 null 元素、options-budget 缺 Label，可能先觸發型別／完整性拒絕而非預算。恰2048欄合法控制已涵蓋，非全面拒絕假綠 | 日後給超量元素合法內容以分離容量條件，不放寬產品上限 | ClaudeOnly UI 測試補查 SHA-256 F758DD15E17D9C8D5B8DDAD37F3181ABDF441FDCB837C3C695E6DDC7C125904B |
| 2026-11-26 21:02 Asia/Taipei (UTC+08:00) | FS | QuiesceSourceBinding fixture 清理案例目錄後留下空的固定 Temp 父目錄；UiSolutionDeployment 的執行緒配置量測可能受首次 JIT 影響 | 日後改善測試 owner 的空目錄整理與配置量測暖身；不宣稱現有量測為長期效能基線 | 同上兩份單模型補查；未讀取 secrets、未連實機 |
| 2026-11-26 21:02 Asia/Taipei (UTC+08:00) | FS | Plugin 新組織未啟用步驟時仍可規劃 assembly／type，沒有 CreateStep／ReconcileImages；缺旗標測試直接覆蓋型別不存在分支 | 契約目前限制執行步驟；日後可補型別已存在分支的直接控制，不新增本包完成條件 | ClaudeOnly 修正複審 SHA-256 877C93DF67B264E800DD1D4515718D010ED0FA3612548266CE3A79D5D939E4D4 |

| 2026-11-28 21:15 Asia/Taipei (UTC+08:00) | F5離線前置 | ClaudeOnly非阻擋觀察：目前測試證明兩handle皆排空但未直接觀察反序；沒有需權限的reparse建立案例；legacy存在性至開檔間仍依賴外部鎖協定 | 日後補特定順序觀察及可用權限下的reparse負向案例，不藉此略過既有EnsureNoLinks／FileShare.Read；未知提交與驗回故障則已列完整Live入口必做復原待辦，不延後該契約 | summary SHA A64F33B1A14F4A6C136EC42582C1D5C907703EBC9CC56FCC7A06471FDAE05D5C；Critical0／High0，未冒稱實機 |

| 2026-11-28 22:04 Asia/Taipei (UTC+08:00) | F5清冊非阻擋觀察 | 專用Thread的WaitSleepJoin診斷理論上可能受暫時型別載入等待影響；CreateAt仍依外部受控根與鎖協定防止link檢查到開檔間競態 | 現有inAppend斷言及真正移除鎖RED保留；不把診斷狀態用作產品同步或把journal action當遠端授權。完整Live的SDK／Channel驗證及清理仍屬必做，不列延後 | ClaudeOnly C0/H0、summary EC155991CBD7A852B56F597C1EC2D0C208A81466E07DEF285C43785C12C6F932 |

| 2026-11-28 22:58 Asia/Taipei (UTC+08:00) | FS非阻擋觀察 | baseline兩次序列化的有界配置；candidate缺key可能KeyNotFoundException仍拒絕；固定補值僅actual等於常數時納入，缺省節點不強迫出現；新負向fixture斷言不相等，最外層Equal拒絕另有既有覆蓋。contact沒有真實fixture、新表端未用舊表baseline比較 | 日後可補精確例外與配置量測及contact實機fixture；本包不放寬guard、不新增完成定義，後兩組織未知正規化仍拒絕 | 兩輪單模型（使用者指定 ClaudeOnly）C0/H0；summary 9B781C9D…／F52848A6…；審查未跑實機 |
