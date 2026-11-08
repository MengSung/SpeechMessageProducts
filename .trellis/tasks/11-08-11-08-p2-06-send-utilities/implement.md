# P2-06 執行與驗證

1. 已讀 3.4 README/00全文/P2-06、適用 backend 規範及 before-dev。父工作樹來源只讀，rg 盤點5/4/3份並保存266個公開建構式／多載對照。
2. 已先跑原66筆 baseline-fixed 全綠，內容解碼故障紅燈。後依核定範圍加入 Tool csproj 參考、net10 multicast/內容入口及薄殼。
3. 主方案 menu 不得退役：已撤回 NotSupported，依原 HEAD 方法僅改圖片讀取接縫後跑第67筆 wire 全綠，原成功及 upload fault 的 stream Dispose 兩項紅燈；恢復最終 workflow using owner。
4. 執行最終聚焦：ChurchReport 的 LineSharedWorkflow、ReplyUtilityGroupRoomProfileAdapterTests、Payments.PushUtilityTests；processor 既有 Send/Reliable/Profile；Workflows 既有 LineNotificationWorkflowTests。保留所有舊測試。
5. 跑 python tools/RepoQuality/check_file_documentation.py、git diff --check、本包逐位元組 UTF-8無BOM／CRLF／末尾CRLF、fixture 唯一鍵與範圍檢查。
6. 原始 TRX/log/錄製只放 evidence，整個 evidence 被本task .gitignore 排除；正式 synthetic Golden 留測試目錄。wp-reports/P2-06.md 僅草稿，明說尚未乾淨驗收。

主代理另負責 §8.2 全方案乾淨複本／I18n／重複等守門、正式驗收、提交推送與封存；本包不動 workflow state、進度或別人的修改。

## 主會話接續
- [x] 依 PROP-005 收斂唯一 legacy 路由，路由欄位契約先有功能紅燈，再轉綠；Workflows 完整35案通過。
- [x] 同步已核定 Core／legacy 邊界及 D4 Emojis 延後，不再要求重複裁定。
- [ ] 在包含共享提交的短路徑乾淨複本執行 §8.1；不得用先前聚焦201案代替。
- [ ] 守門成功才更新驗收、封存與推送；目前未驗收，不覆寫原證據。
