# P5-05 實施順序與檔案歸屬

1. 一小時內盤點WeeklyReportPlugIn、WeeklyReportLinePlugIn、WeeklyReportWorkflow；活動參數、欄位metadata、防遞迴、文字及外部寫入逐條列明。
2. 主代理建立活動外殼測試及Workflows專案整合，紅燈後最小實作、打包檢查。
3. 依盤點產生3～6情境與reading期望；Core規則測試紅燈後實作統計／通知／生成，舊文字不改。
4. Plugin及Activity薄外殼、同情境Shadow與部分信任測試；Workflows覆蓋80%。
5. 清單、停用部署verify=0、固定guard八項、一頁回報、封存驗收、推送與ls-remote。

檔案歸屬：盤點代理只寫wp-reports/P5-05-inventory.md；主代理處理共用外殼、專案整合及測試。待盤點回來再分配Core規則與測試，寫入範圍不重疊。

方案整合核對：Workflows加入.sln恰一次，Debug／Release及原有組態均含ActiveCfg／Build.0；CI及System.Text.Json中央套件守門已納入。ArchitectureRules.Layer與IsSandbox原已明列Workflows，沿用不重複登錄。測試ProjectReference只給Workflows別名，避免連結共用internal型別與Plugins身分衝突；打包仍沿原七輸入目標。

實作接線（2026-11-15）：基礎綠燈提交2b7a8dbc；Core純統計/通知由Arendt、兩Plugin及測試由Kepler、純Create交易Recorder及測試由Mencius、生成Service/活動及測試由Banach，主代理負責共用期望、資源、清單、整合與固定guard。各代理範圍互斥，包內禁止外部模型分析與審查。

生成以church/list/當地Sunday映射正式資料庫主鍵，再以report/contact產生正式出席主鍵；實際Create的資料庫主鍵唯一約束才是跨程序保證。每份週報與出席列用一筆純Create ExecuteTransaction原子發布；先驗完所有資料再寫入，既有多筆拒絕。owner直接Create寫入，避免Create後Assign的部分可見。活動讀一次UTC，從正式名單領袖contact.parentcustomerid推導教會並驗所有領袖/成員，不增加活動引數。不寫四個9.1缺欄位；以上與舊碼差異記expected。Shadow只支援有界且完整先驗的純Create交易，其他批次仍拒絕。

流程偏差紀錄：打包子代理最終回報曾外審（Claude無Critical/Gemini503），但未交付本次run證據，主代理未於本地找到對應本次run；不算包驗收，已關閉該代理並重申D5-20，未再呼叫外審。

整合驗證：216f8cd8週報外殼與規則、6d18f6e4正式註冊；Core週報net48/net10各15通過，Plugins完整427通過含CAS。Coverlet插樁在CAS域會要求EnvironmentPermission，另跑未插樁完整測試，覆蓋量測只略過CAS測試且不排除產品檔案，Workflows86.50%。
外審偏差補記：Rawls、Mencius、Kepler、Banach皆在回報聲稱嘗試外部分析/審查，主代理沒有找到本次run證據；全部不算驗收、不再呼叫外審查證，代理已關閉，D5-20維持。
