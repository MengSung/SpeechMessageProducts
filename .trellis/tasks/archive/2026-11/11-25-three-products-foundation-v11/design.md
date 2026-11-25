# 文件設計

三條獨立產品泳道，底部以虛線表示共用程式與版控定義；每條泳道展開 Channel 池、獨立 Webhook/Worker、D365 組織及多 Account。租戶不是 Channel 本身，伺服器必須驗證會員與所選 Account 的關係。

圖解另含資料放置、Form/View 佈建依賴、現有工具能力對照、八步 WinRM 部署、容量試算、實作順序與一功能一驗收關卡。報告同一內容模型輸出 Word 及 HTML 全文，避免不同步。

現有 UiSolutionDeployment 支援固定 SpeechMessageUi 匯出入，UiSolutionArchive 刻意移除 account/contact 表單；匯入目前 PublishAllXml。此現況不可誤述為全部 UI 已自動支援，後續需獨立的 UI 差異與發佈契約。

本次離線文件修改可透過刪除新增版本回復；既有文件與產品檔保持原樣。與已完成計畫3.5僅有歷史證據關係，非新執行依據。
