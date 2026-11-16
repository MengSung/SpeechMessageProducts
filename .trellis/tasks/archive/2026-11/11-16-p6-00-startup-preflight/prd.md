# P6-00 開工切換與前置缺項檢查

## 需求來源

使用者 2026-11-16 已授權實作；直接依
`文件/規劃設計書/實作計畫-3.5/06A_階段6_執行細則.md` §5「P6-00」。
本包不計入 84 包，時間盒 1.5 小時，父任務 integration-p6。

## 完成條件

- 核對續跑狀態、分支與起點；只納入原掛鉤紀錄與原樣的 06A。
- AGENTS 授權段落精確切換至階段 6，不啟動階段 7。
- 進度只改 DATA：P5 停止 current、P6 current 且 0 done／0 active，
  一張 P6 info 卡、P6-00 與頁面驗收里程碑、六項 P6 提案及後續事項。
- 三個指定 P5 殘留任務完成封存；父任務含 prd、design、implement、
  四個開工基準數字及 preflight。
- 前置核對只輸出鍵名與狀態，D365 唯讀；缺項記錄，不妨礙後續離線包。
- git add 後通過 00 §8.3 三項檢查，提交訊息
  `P6-00 chore: 階段 6 開工切換與前置缺項檢查`，本包不推送。

## 指定測試與驗證

本包沒有產品測試或紅綠迴圈；僅切換紀錄與核定授權。
命令為 Test-TextEncoding.ps1 Changed/Base=9b7ac0a0、
check_file_documentation.py、node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs。
不修改產品程式、計畫正文或部署設定。
