# P2-00 Debug 中繼檔診斷

2026-11-08 08:15 Asia/Taipei (UTC+08:00)。核對使用者回報的 GeneratedMSBuildEditorConfig.editorconfig 與 FileListAbsolute.txt 路徑錯誤。先核對原路徑、VS／CLI 差異及程序干擾；不得手建產生檔、降低檢查或刪除使用者工作偽裝修復。

本項是 P2-00 附屬診斷，不增加第三個 P2 工作包。只改任務紀錄與進度 DATA，不改產品／系統設定，不碰其他未提交變更，不用外部雙模型，不推送。

驗收：原路徑建置／重建與雙宿主回歸有證據；IDE 必須另核對，不把 CLI 成功當成 IDE 已修復。現況 CLI 通過、IDE 最終輸出待驗證，故未歸檔。證據與 SHA 見 P2-00-Debug建置診斷.md。
