# P6-03 進行中一頁回報（非驗收）

2026-11-17 02:29 Asia/Taipei (UTC+08:00)：四個相容殼與專用測試已按06A移除，產品Release建置及指定十名稱16實例通過；尚未提交、固定guard、驗收或推送。

| 需求 | 證據 | 狀態 |
| --- | --- | --- |
| 四種通知政策、來源、可靠RetryKey、多播統計先送後、200交錯隔離 | Notifier六項與移除反射，共7實例 | 通過 |
| P2-06原始SHA及逐案歸屬 | 主檔67案、來源5案；6重播位元組相等、66逐案明列退役 | 通過 |
| 既有產品測試 | 639通過、唯一1紅為SourceBuilders清冊11與實際10 | 待P6-PROP-008核定 |
| ToolUtility、Dataverse、共用workflow、架構 | 1411＋1268、79、39、82全通過 | 通過 |
| 文字／重複／文件／編碼／進度 | I18n exit0；重複new0；文件1743/0；編碼85/0；進度28/28 | 通過 |
| 提交後固定guard、驗收、推送 | P6-03未產生程式提交，HEAD仍6630b321 | 未做 |

待核定的唯一程式差異：DevelopmentSafetyTests.cs:298的count.Should().Be(11)改為Be(10)，同步方法註解；不更動逐檔Development覆寫、token隔離、設定優先序等其他斷言。依06A §3地雷10，原測試期望保留未改。其他選項記在進度DATA的P6-PROP-008；核定前不以綠燈過濾冒稱完整通過。

盤點與刪測試案例數見P6-03-inventory.md。三個profile／reply案例已移共用測試，RichMenu catalog經編譯證明Startup仍用而保留；未改legacy workflow／型別轉送、NoWarn、duplicate-baseline.json或計畫正文。本包測試未連D365或發真實LINE；未啟動P7。

2026-11-17 02:34 Asia/Taipei (UTC+08:00)：D34另以已核定的獨立程序完成唯讀核對、writes=0，詳P6-00-d34-readonly.md；頁面驗收尚未完成。

原始證據位於artifacts/p6，不進版控：

- p6-03-named-contracts-green.log：`12cb2ef2fe2d87f9428f10d41475562d49d926695d6fe8b55047eefd9942d823`
- p6-03-churchreport-first-full.log：`2feef3ad9c562fe234e045398d193065b24402db02e5c3c823ebf4eef9caa566`
- p6-03-toolutility-tests.log：`a06031d450be060e642c2161f9d99833a1cf62f55119fd41e64a0748d6662c2f`
- p6-03-dataverse-tests.log：`8a8c74344ce8a6d60a430c8883f59d0a5f0524a4ec20118edc69a156ea6cd2f4`
- p6-03-workflows-tests.log：`178e453984e4ac6fd180f1ed35e1fcc66a4c763f10fac8774c94d2dcecd9486c`
- p6-03-architecture-green.log：`f962be3ff1bcf138da23a393e28327baad7854c973131d0d7bb3cfce7d32612e`
- p6-03-duplicates.log：`c51e87afb539d5a180ee8a523b62cb6e15e15717150c6cf346044e7b9ffe9dde`
- p6-03-file-documentation.log：`e86ecdaae23c02893eb391a62c1bdc90e7a850f6a543d2b906424bd5020aa617`
- p6-03-progress-tests.log：`af4da987b29f66ae88426b9a1953b3a6ace1a572662f8ad9378a623af3774154`
- p6-03-encoding.log：`1c8f6f11b4e335e65aa3c172e4acfcd564ec34d42a882979ea375e90367f4960`
- p6-03-i18n-before.log：`1cf6a4f5b61312db118c1b479fbeb658e10442e8be4d627d6635eaea206fed6f`
