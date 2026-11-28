# 三大產品共通層 1.3 線 B：之後再說

| 時間（Asia/Taipei） | 工作包 | 項目 | 本階段處置 |
|---|---|---|---|
| 2026-11-27 08:10 Asia/Taipei (UTC+08:00) | F8 | BackOffice_TenantAUser_CannotSeeTenantB | D17已核定只有音訊科技營運直接登入D365，租戶人員走LINE／LIFF／網頁；日後另行開放時以每租戶owner team與最小角色作真正登入驗證，本包不冒稱已完成原生登入隔離 |
| 2026-11-26 16:37 Asia/Taipei (UTC+08:00) | F4 | 第三輪非阻擋觀察：Cancel可明確解除Uncertain一次性工作後重排；同鍵長工作使Schedule／Cancel在5秒鎖逾時失敗 | 審查未找到外洩或重複觸發路徑，不列Critical／High；handler業務副作用仍須權威原子冪等。後續操作介面須明示取消不是已完成對帳，不在本包擴張產品UI |
| 2026-11-26 09:56 Asia/Taipei (UTC+08:00) | F10 | RegularQuery 的 V2.6 期數／重試子單語意與 ExpBillingDays 參數名稱共用常數建議 | 本機 V2.4 §8.9 支持目前保守完整性檢查，V2.6 與實機仍待補；不把舊版核對寫成新版驗證。參數名稱耦合已有 Gateway 行為測試，非 Critical／High 的常數整理本輪不擴張實作 |
| 2026-11-26 04:16 Asia/Taipei (UTC+08:00) | F10 | Payments core 的 `Logs_NoCardNumberOrBindToken` 宿主 `Exception.log` sink capture | 核心沒有 logger 或檔案 writer；本包只驗證 diagnostics sanitizer 邊界。由後續 host／告警整合補做實際 sink capture，不在 F10 建立第二個日誌 owner |
| 2026-11-26 04:16 Asia/Taipei (UTC+08:00) | F10 | callback 最終狀態的跨請求資料庫冪等 | provider parser 保持無狀態，只交付查詢線索；由產品持久層以銀行交易穩定鍵及原子 create-or-update 實作，F10 不虛構完成 |
| 2026-11-26 04:16 Asia/Taipei (UTC+08:00) | F10 | BindToken 的永豐實際效期與清除期限 | 本包只使密文 profile-bound 且憑證輪替可失效；正式效期列 G4 前置問題，未把未知限制寫成銀行規格 |
| 2026-11-26 04:16 Asia/Taipei (UTC+08:00) | F10 | 五個永豐沙箱情境 | 已更正原先「缺交接設定」的原因：JesusTest 必要欄位與本機測試卡文件均存在，未輸出或複製值；五項沙箱及隔日到期查詢尚未實跑，V2.6 完整規格尚未核對，仍標「實機待補」，不能以合成 handler 取代 |
| 2026-11-28 14:27 Asia/Taipei (UTC+08:00) | F6 | ClaudeOnly非阻擋觀察：無Liff來源區段仍允許F2骨架部署；封裝讀JSON主流程與64KiB界限尚無獨立回歸；Live預期app取自本線來源 | 既有骨架缺省拒絕登入，不新增完成定義；本輪補三鍵保留及兩個唯讀探針，未以匿名401冒稱過期／aud／真身分驗證。來源目前已核定app，後續部署設定治理再強化獨立基準；審查未核對文件／數字，由本線守門證據負責 |
| 2026-11-28 23:42 Asia/Taipei (UTC+08:00) | F7 | 舊版公開Logo清理 | 依01 F7明定每次替換產生新URL且保留舊檔，不在本包加刪圖工作；後續需以正式租戶與檔案身份、引用狀態及快取保存期限訂出可稽核清理政策，不能按檔名顯示或跨租戶掃除。跨release保存是產品契約，當次候選、串流、像素、timer仍須確定排空。 |

| 2026-11-28 23:54 Asia/Taipei (UTC+08:00) | F10 | 正式ClaudeOnly非阻擋Warning：日期時間過遮罩、碰撞拒絕的宿主行為 | 過遮罩偏安全但降低可讀性，之後需以安全邊界回歸改善；鍵碰撞必須保留完整候選拒絕，真正失敗告警與宿主sink尚未驗證，不把本輪離線C0／H0當入帳或告警實機驗收。 |

| 2026-11-29 00:41 Asia/Taipei (UTC+08:00) | F11 | 正式ClaudeOnly非High：關機時從未建立Gateway的queue首次解析；readiness持續故障診斷量 | 現有InactiveEnvelopeHandler無發送路徑，queue後續自身排空且真例外會告警。本包依02只記錄不擴張；F9啟用前須在正式Host接線證明Gateway先建立，或獨立修正停止順序並測首次解析／停機競爭。readiness事件去重與持續故障量後續量測；不聲稱64筆queue等於完整限流。 |
