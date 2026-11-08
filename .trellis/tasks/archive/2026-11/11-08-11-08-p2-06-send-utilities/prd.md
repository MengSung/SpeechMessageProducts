# P2-06 合併既有送訊副本

## 依據與目標
僅依實作計畫-3.4 README、00 §5/§7、03 P2-06，及使用者本次兩次澄清。盤點完整公開方法，先以假 HTTP 鎖特性，再以 Obsolete 殼委派唯一共用入口，不另造 HTTP。

## 核定範圍與決策
- 盤點主方案及父工作樹兩舊方案，Push 5／Reply 4／LineUtility 3，共 12 份、266 個公開建構式／方法；逐多載與來源 SHA-256 見 research/public-method-inventory.md。
- 正式會友主鏈已由 P2-04 Core 承接；legacy arbitrary、group、多則、multicast、reply、download 保留 net10。禁止 fake 授權 ID、反射讀 token、改 Core/SDK。
- 使用者額外授權獨佔 ToolUtility.csproj，加 processor/Workflows 參考，先核對無循環。
- 外部舊方案純 CRM／未使用 demo 不復活，標示能力差異及承接入口。D4 Emojis 多載依使用者 2026-11-08 核定不搬，列入 LATER-005；不新增 SDK 能力。
- **主方案現有 Add/DeleteRichMenuMessage 不得任意退役或改 throw。** M1 保留原四步／三步，圖片改可注入有限來源；M2/M4 既有 assignment 不倒退。
- 所有既有測試保留，不改父任務／進度／共用建置或方案／Plugins／GoodShepherd／部署與 docs/file-purpose-companions.json。不提交推送、不 spawn、不外審；原始證據僅 evidence（gitignore 排除）。

## 驗收清單
- [x] 完整公開方法與多載盤點、共有／其他入口／產品不搬／外部未用能力分類，明列差異。
- [x] 原 66 筆主方案 wire 黃金於產品修改前全綠；M1 Add 原 HEAD＋圖片接縫補實測第67筆；另五種來源重播三方比對。
- [x] EchoImage 成功／故障的 response、image/thumbnail 及 RichMenu 上傳 stream 在 using owner 釋放。
- [x] 舊四類標 Obsolete；M1 直接送訊改共用 net10；ChurchReport 既有 Workflows/RichMenu 委派保留。
- [x] 最終聚焦201個測試通過，文件／位元組檢查結果及SHA-256落入一頁草稿；legacy／Core 邊界已依 PROP-005 核定；最新唯一路由測試及工作流程35案通過，乾淨守門待完成。
- [x] 主代理24e97b9e乾淨複本全部守門通過；2026-11-08 22:26 Asia/Taipei (UTC+08:00)驗收，回報及DATA同步，封存後推送。

## 非目標與誠實邊界
沒有實機或整包外部舊方案執行；來源重播、原方法圖片接縫及原先已全綠基線分開標示。舊 ToolUtility 刪除仍假設 menu 獨佔，不能用於 ChurchReport 共用 assignment；是否改此業務策略不由本包擅定。

## 核定後的驗收邊界（2026-11-08）
PROP-005 已核定：legacy 任意推播（非會友、群組、多則、multicast、媒體、樣板）一律經 net10 的 LineLegacySendWorkflow；正式會友推播才適用 Core 要求。LineNotificationWorkflow 及 RichMenu 成功通知已歸入同一 legacy 入口，保留既有 Retry Key；不偽造 Contact／Church scope，不改 3.4 正文，待寫入 3.5。

D4 Emojis 不搬已核定，清單 LATER-005 如實保留相容差異；不是仍需使用者裁定的阻塞。
