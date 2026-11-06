# P1-09 完整資源守門驗收

2026-11-06，Asia/Taipei；依據02 P1-09、90 E.3/E.4及Word5.7/5.9。P1-05前置已完成，本包不等P1-08。

| 需求 | 實作 | 正反例及決定性驗證 | 狀態 |
| --- | --- | --- | --- |
| key一致 | ResourceCatalog/ResourceSet | 翻譯已知key通過，未知key拒絕；孤兒翻譯不冒充中性 | 通過 |
| 參數集合 | ResourceCatalog/Placeholders | 換序及重複名同集合通過，name→other拒絕；P0案例保留 | 通過 |
| 跨檔唯一 | ResourceVerifier/ResourceSet | 同組翻譯合法；跨中性同key拒絕，不同key同顯示值保留；物理檔重述拒絕 | 通過 |
| Sender20/ChatBar14及pseudo | LengthLimits/ResourceVerifier | 原文化界線20/21及14/15、pseudo8/9、UTF16 emoji、完整14字名稱及未知參數/非法配置 | 通過 |
| 關鍵字衝突 | ResourceVerifier | NFKC/Unicode空白/Invariant大小寫，同意圖合法，單/跨語系異意圖拒絕；空別名拒絕 | 通過 |
| en資訊報告 | ResourceVerifier | 0/50/100%均通過；真正repo18.18%(6/33)，未把別語系或缺譯當完成 | 通過 |
| 不改舊守門 | CommandRunner共享候選 | 所有119個原工具案保留、生成check/時區check/CJK基準無改 | 通過 |
| 隔離/資源 | 操作區域候選/using/預算 | 20 CLI交錯、1000快照A/B、100弱參照、獨占把手、全檔唯讀/故障重試/組與repo預算 | 通過 |
| 真實語言與sender流程 | Localization生成資源/原測試 | resolver→字典→生成sender；zhTW/en回退/qps完整名稱不截字，兩宿主162/188 | 通過 |

## 原始及最終證據
原始根：artifacts/p1-09-20261106/；可追蹤摘要/SHA：.ccg/tasks/integration-p1/evidence/p109-final/。
- 真紅：v2原28案21紅/7綠；真repo sender qps長21；resolver/sender六案紅；孤兒/保留語系3紅；group budget1紅；JSON高surrogate2紅/8綠。CS1573及首輪三個舊sender精確golden失敗保留，全部修正後重跑，沒有放寬斷言。
- 工具最終 **170/170**（含v2 **51**），獨立coverage **97.54%**；工具strict重建0警告/0錯誤。
- 完整方案 **17TRX4203/4203**、0失敗/略過；Localization **162/188**，Framework原反射守門實載net462；ChurchReport629、ToolUtility1214/1345。
- Localization strict雙目標0/0；獨立coverage **98.17%/98.52%**，各恰一份且正式>=80%守門通過。
- 完整方案0錯誤、23既有測試警告，未隱藏／聲稱消除。Debug7/7含正式TRX守門；PE27/27，Plugin metadata10/build6（一次性測試key）、CI6；套件36/167零違規、duplicates零新增。
- 真repo verify、全部5組資源生成check及CLDR check通過；原CJK基準完全不改。
- 最後編碼與進度頁結果見summary.json，交付收尾再次驗證。

## 唯一可見文案差異與理由
原E4字面名稱雖含「十二字」但實為14字，完整保留。`{churchName}小幫手`依原pseudo公式展開為21，確實超20；只縮固定後綴成「助手」，普通16/pseudo19。沒有縮名、改公式、提高20/14、開放英文或改業務；原精確golden只調指定字面並保留所有測試。
正式計畫及核定政策不變，無Word計畫修正報告需求。生成namespace錯誤參數由diff發現並用正式工具改回，這不是聲稱曾發生編譯失敗。

## 審查及交付界線
主會話自審（子任務review.md），沒有外部模型或獨立第二人；使用者免雙模型不代表外審已完成。
未push／部署／連VM或D365／發LINE；本證據不是LINE實機版面或hostedCI驗收。
本地工作commit `9f9b2b29`，本地封存commit見Git紀錄；只封存p109-i18n-v2，主P1增9/12並保持Goal active，仍須P1-08/11/12。
P1-09已知阻斷缺陷：**無**。
