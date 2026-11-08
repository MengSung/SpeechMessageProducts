# P2-03 主代理安全審查

更新：2026-11-07 12:55 Asia/Taipei (UTC+08:00)。

模式：依使用者明確免除外部雙模型，主代理 inline 實作及審查，沒有呼叫
Gemini、Claude、codeagent-wrapper、雙模型 runner 或實作／檢查子代理。
本紀錄不表示獨立雙模型審查，也不表示P2-03或P2正式驗收完成。

## 已修正問題

| 等級 | 問題／根因 | 修正及證據 |
| --- | --- | --- |
| Critical（已修） | SDK主故障在lease.Dispose又拋錯時被using覆蓋 | cli-cleanup-red確實1案失敗；ExceptionDispatchInfo保存原身分／堆疊，依序完整嘗試lease及lock清理，最多三原物件聚合，成功摘要延後；雙組態完整回歸 |
| Critical（已修） | 單純同步stdin等EOF無界，Task.Run替代會遺留讀取與秘密 | 真程序改用ChannelSecretInput；pipe非破壞peek＋總deadline／caller取消，有限byte／char預算，嚴格UTF-8及確定清理；真pipe、128輪不靠GC的handle回基線 |
| Warning（已修） | 尾端加TextReader破壞CancellationToken最後參數；同名多載造成舊反射歧義 | 不停用CA1068，保留唯一Run八參數／RunCommand六參數，新增不同名可注入入口；原157案保留執行 |
| Warning（已修） | 測試反覆配置常值陣列觸發CA1861 | 只讀公開欄位名用私有readonly，個案參數使用操作自有集合，不放寬分析器 |
| Info（已處理） | PluginPackaging.Tests缺project.assets.json | 保留NETSDK1004原始失敗，正常restore保留audit再重跑27案通過，不用忽略還原錯誤 |

## 已檢查不變條件

- 核心先完整驗MAC才建立decryptor；net10官方FixedTimeEquals及net462 XOR累加，
  版本、IV、ciphertext同受MAC保護；不用時間統計冒充固定時間證明。
- key分兩半且每操作複製；成功失敗都清零自有key／明文byte／MAC／中間buffer，
  演算法及transform逐一Dispose。來源key、CLR字串不能由此層可靠清零，文件如實揭露。
- 無static秘密、Session、HttpContext、跨教會／組織cache或持久回呼；
  核心兩key同時運作與retained bytes／handle測試已實際執行。
- parser拒絕未知／重複／敏感選項，固定組織與公開ID先驗；key／輸入容量／加密完成
  才Connect。assign不讀key或stdin，所有SDK請求為當次局部候選。
- Upsert使用new_channelid替代鍵，只有名稱及兩密文欄位；lookup只使用server真正
  new_linechannel ID，不掃全表，不按顯示文字去重、不將操作鍵充當資料庫ID。
- lease→lock清理全部嘗試後才上報；原SDK Exception交共用診斷，
  真Program測試證明Exception.log保留型別而沒有假秘密、密文或key。
- stdout不讀原Message／Data；reporter故障只固定stderr；正常caller取消與內部逾時分開。
- 未新增csproj／套件／分析器豁免／baseline排除；增加直接Core參照不改變既有傳遞閉包。
  本工作樹原有提交376a6172由外部建立，保留且不amend，不冒稱本次主代理提交。

## 尚未解除的正式驗收門檻

1. 真實P2-03資料寫入、替代索引唯一性、欄位安全性、before／可驗證回復、
   專用資料清理尚無證據。現有CLI沒有自動backup／rollback，不得隱含承諾。
2. P2-04使用者推播前置不改變；沒有正式Channel、真實收件人或金流操作。
3. P2-01 R2仍有654筆精確未通過差距；完整Changed既有五份原始機器證據
   換行問題仍追蹤，不能改寫證據或擴大排除取得綠燈。
4. Windows stdin只保證單一reader的pipe有限等待與有限本機檔案；任意失效裝置
   同步I/O不保證可中斷。不要把注入reader介面公開給不受信任的request。

結論：可提交已驗證離線檢查點，P2-03維持進行中，不封存、不標P2完成。

## 2026-11-08 07:57 Asia/Taipei (UTC+08:00) 更高優先設定要求的雙後端審查嘗試

保留前述 3.3 歷史；目前 3.4 已將 P2-03 離線驗收完成，必要寫入實機移往 P2-04 前置與 P2-12，不再沿用歷史額外門檻。此次依更高優先 review 設定，以專案自癒入口同時要求 Gemini 與 Claude reviewer，run `20261108-073101-p203-required-resume-review-reviewer`：runner exit=2、ok=false、dualModelComplete=false；Gemini 403、Claude 未交付有效 findings，沒有任一後端可用審查，不是雙模型成功或可用降級。Claude 衍生重啟也無完成 summary，未遞迴重試。原始全文不提交；本紀錄僅保存嘗試事實，不把審查失敗新增為 P2-03 的實機門檻。
