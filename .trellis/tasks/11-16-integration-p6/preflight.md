# 階段 6 前置缺項清單

2026-11-16 18:42 Asia/Taipei (UTC+08:00)：本輪只檢查鍵名、可達性及唯讀狀態，
未部署、啟停步驟／流程、修改 D365 資料或發送 LINE。缺項不阻擋離線工作包。

| 項目 | 給哪一包 | 實際結果與證據 | 後續處理 |
| --- | --- | --- | --- |
| C:/smv 僅留 guard | 守門 | 已符合；330 筆舊證據／工具原樣移出保留，清單在 artifacts/p6/p6-00-smv-preserved.txt | 保存位置 C:/Users/Administrator/.speechmessage/evidence/p5-smv-20261116-1835；不刪原始證據 |
| P5 基準工作樹 | 頁面驗收 | HEAD 9b7ac0a0、git status 無變更；啟停網站後再次核對仍乾淨 | 保持唯讀，不提交或修改來源 |
| Deployment verify | 頁面驗收 | 初次因 guard 缺少打包 DLL 回 DirectoryNotFoundException；從 P5 原樣借入兩個已打包 DLL 後 verify=0 | 無部署寫入，證據 p6-00-deployment-verify-ready.log |
| 新步驟、舊步驟及流程 | 頁面驗收 | verify 與清單一致；22 個新步驟皆停用。export-legacy=0，舊步驟啟用 0、舊流程啟用 0 | 保持狀態；p6-00-deployment-legacy.log |
| Development 本機啟動／D27 | 頁面驗收 | P5 Release，以 --no-build --no-launch-profile、localhost:5166 啟動，D27 通過且首頁 HTTP 200；Ctrl+C 後觀察到 Application is shutting down，程序已終止 | p6-00-host-probe.txt；沒有同時啟動兩站 |
| 登入方式與測試帳密 | 頁面驗收情境 4 | 原控制器 ProcessLogin 接收 GalleryViewModel.Account/Password，驗證 contact.new_app_acount/new_app_pass；Host User Secrets 無測試登入鍵 | 尚未讀取會友帳密或測試登入；於頁面驗收依 D6-15 的一小時時間盒處理，不能冒稱已可登入 |
| 四個測試秘密鍵 | 頁面驗收 | Deployment User Secrets 的 TestLineUserId、TestChannelToken、ShadowTestChurchId、ShadowTestContactId 鍵均存在；未輸出值 | 真正驗收前由程序使用並核對；Host User Secrets 為空，LINE 測試設定仍須準備 |
| D34 當下資料與 Channel | 頁面驗收 | PowerShell 載入正式部署 SDK 的唯讀探針，及移除載入回呼內 PowerShell 命令後的重試，兩次皆程序堆疊溢位；無 D365 寫入 | 依 06A §7-4 停止同一操作並請使用者核定改用獨立程序；確認前不做簽到／真實推播，不把 P5 舊證據當本次通過 |
| Node／Edge／playwright-core | 頁面驗收 | Node v24.11.1；本機 Edge 存在；repo 外 %USERPROFILE%/.speechmessage/playwright 安裝成功，以 channel msedge 啟動、開 about:blank、close 成功 | 目前未使用 D6-15 的 Playwright 退路；不下載瀏覽器、不在 repo 建 node_modules |

## 等待與執行界線

需要使用者核定的只有 D34 自動操作連續失敗之後的替代執行方式；
已提出「改用獨立程序核對」的建議，未自行第三次重試。
登入與 Host 測試 LINE 設定尚未驗收，保留在同一清單，不要求使用者提供真實秘密值。
等待期間依既定順序繼續 P6-06 等離線包；不把開工檢查當作四份行為不變證據。
