# P5-12 執行順序

1. 更新進度 DATA 與任務；只讀舊來源、遮蔽秘密；逐檔盤點並查證狀態欄位及來源教會關聯。
2. 雙目標紅燈覆蓋單一收件者、來源隔離、原文、Retry Key 與結果分類；實作 OutboundLetterRule 與外殼。
3. 增補快速行銷活動重送、100 活動及生命週期測試；實作 Hosting 失敗彙總與 LineWebhook 組合。
4. reading 六案、真 Plugin/CAS、字典生成、註冊清單與文件檢查；小步綠燈提交。
5. 兩 DLL 打包；只在 goodshepherdback plan/apply/verify 全停用；固定 C:/smv/guard 八項；一頁報告、任務封存併收尾提交，乾淨守門後才 push 與 ls-remote。

命令：dotnet test Core.Tests -f net10.0／net48；dotnet test Plugins.Tests；dotnet test Hosting.Tests；node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs；python tools/RepoQuality/check_file_documentation.py；其餘固定 guard 沿用 00 §8.1。無關 .turns.json 改動不提交、不還原。
