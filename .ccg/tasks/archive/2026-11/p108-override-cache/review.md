# P1-08 主會話審查

需求、實作、全部實際結果及限制見 wp-reports/P1-08-acceptance.md。
已完整讀取本包來源／validator／cache／DI／manager adapter及對應測試。

## 已關閉阻斷

- 故障lease3紅/4綠後補MarkFaulted，傳真原例外，先淘汰再歸還。
- 壞取消callback及deadline callback紅後改安全timer，取消／Dispose皆排空。
- 第一caller AsyncLocal真紅後抑制timer與Lazy的ExecutionContext流動。
- 百scope48/200未回收，讓出排程仍失敗；同步內容擷取獨立真紅0/2，兩處Task.Yield改ForceYielding，75/75及五輪10/10通過。
- 不以display name去重、不發布部分候選、不把caller token傳共享來源；容量與generation競態均有實測。

## 本機審查結論

未留已知Critical／High程式缺陷。覆蓋率五份均≥80%、strict零警告／錯誤、方案17TRX4317/4317；
既有ChurchReport630、雙宿主來源／validator及完整00§8均通過。所有新檔UTF-8無BOM／CRLF按位元組檢查。
不是獨立第二人審查，不宣稱VM／D365／LINE實機或hosted CI成功。

## 外部審查缺證據

依developer最新要求仍使用自癒入口同時呼叫Gemini與Claude；run20261106-161834-p108-cache-reviewer。
ok=false、dualModelComplete=false、quotaBlocked=true、degradedFallback=false，completedBackends空。
兩後端皆無可用結果，不算任何外部審查通過，不重複修理供應商額度；依使用者免雙模型與自癒降級規則繼續本機驗證及P1交付。
