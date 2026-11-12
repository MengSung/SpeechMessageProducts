# 階段3協調設計

主機遵R2僅Program／*Registration／*Endpoints，業務在GoodShepherd.Core，共用入站契約及基礎設施在既有net10共用層。主機處理body上限→驗簽→解析→去重→佇列→200；驗簽拒絕401、過大413、SQL／queue失敗503，交接失敗要避免永久佔用去重導致重送漏件。必要三個實作缺席拒絕啟動。

## 檔案歸屬及相依
| 包 | 產物／消費 | 歸屬或衝突裁決 |
|---|---|---|
| P3-01 | LegacyGolden、關鍵字／事件／回覆黃金檔 | 新tool csproj需sln；與02串行，不改舊來源 |
| P3-02 | Host、端點、三介面與Host契約測試 | sln與01重疊；先02，對03/04/05發布穩定介面 |
| P3-03 | 驗簽實作／獨立註冊、驗簽測試 | 依02；不動其Program或註冊原檔 |
| P3-04 | Sql去重／清理及LocalDB整合 | 依02；與06檔案不重疊時平行 |
| P3-05 | 有界分區queue、worker及註冊 | 依02；與03獨立檔案時平行 |
| P3-06 | 各事件處理器、Core業務 | 消費01黃金及05queue |
| P3-07 | Keyword字典與路由 | 消費01/06；與08均碰resx所以串行 |
| P3-08 | Reply／letter／exception | 消費06；與07字典衝突後做 |
| P3-09 | 真實IIS／HTTPS／LINE／SQL驗收及清理 | 依02～08全驗收及前置全備才執行 |

所有event／queue不留HttpContext、session、credential；具完整channel隔離、容量／期限與drain／dispose責任，失敗保留真實Exception走共用入口，診斷不暴露事件內容。Test-only替身不得以正式DI預設註冊。各WP引用該層spec，先紅後綠。
