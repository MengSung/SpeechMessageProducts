# P2-01 執行計畫

- [x] 讀取 `.trellis/spec/backend/index.md`、error-handling、cross-user-isolation、
  duplicate-row-publication 及相關 guides。
- [x] 建立 `SpeechMessage.Hosting`／`SpeechMessage.Hosting.Tests` csproj 與 solution 參考。
- [x] 先補四種通知開關、故障注入、取消／逾時／drain／資源基線測試並確認紅燈。
- [x] 加入 Hosting registration、真正 sender、Development 共用委派與 Program 單一 owner。
- [x] 加入 CultureCompleteness 生成、唯讀 check、嚴格語系設定及 Program 真接入。
- [ ] 完成架構測試實際搬移／R2、R3、R9 擴充，同步 CI filter。
- [x] 以 ChurchReport 的既有組合根接入，不重複建立通知 owner。
- [x] 執行目前切片 focused tests、ChurchReport exception tests、Release build；整包變更後須重跑。
- [ ] 執行 file documentation、encoding、duplicate、I18nCheck 與架構驗證。
- [ ] 撰寫 `wp-reports/P2-01.md`，更新 `實作進度.html`，檢查未辨識 dirty files。
- [ ] 載入 `trellis-check` 完成主會話審查；若有 blocker，修復後重跑。
- [ ] 載入 `trellis-update-spec` 評估是否需沉澱新規則，再提交 P2-01。

## 受影響檔案界線

預期新增／修改：

- `SpeechMessage.Hosting/**`
- `SpeechMessage.Hosting.Tests/**`
- `SpeechMessageProducts.sln`
- `SpeechMessageProducts.ChurchReport/Program.cs`、必要 registration／project reference
- 既有架構測試與 P2-01 回報／進度／任務文件

不得在本子任務修改 P2-02～P2-12 的產品規則、D365 schema、正式組織資料或
任何秘密設定值。

## CultureCompleteness 實際範圍補充

正式計畫明列由 I18nCheck 生成完成度，故同步維護 `tools/I18nCheck`、
`tools/I18nCheck.Tests`、`SpeechMessage.Localization/CultureCompleteness.g.cs` 與
`hosting-culture-contract.md`。生成器是 g.cs 的唯一寫入者，沿用全方案來源驗證與原子輸出；
本輪沒有新增 csproj、放寬基準或變更其他 WP 業務契約。
具體紅綠燈與下一步見 `research/culture-completeness-checkpoint.md`。
AGENTS 引用的 backend cross-user-isolation spec 缺檔時，仍直接遵守 AGENTS 同等硬性規則；
不能把未存在的文件標成已讀取。以上第一項代表已檢查可用規範與此缺檔事實。

## 架構測試與 CI 接續切片

由主代理唯一寫入，沿用已隔離的 P2 worktree。先以真實 CI 閉包與 Hosting
覆蓋率配對案例取得 5／7 紅燈，確認缺少的是兩個 Hosting 專案與專屬 collector／矩陣。

1. 將六個共用架構來源實際搬到 `SpeechMessage.Hosting.Tests/Architecture/`，
   只更新檔首合作路徑，保留 namespace 與所有可執行本文位元組；移除兩份 linked Compile。
2. 同步 CI filter、Hosting.Tests collector 與獨立 net10 覆蓋率 case，
   保留既定三個 DevExtreme 排除。更新用途索引與 CI 契約。
3. 執行 CI 閉包、Hosting 完整測試、Localization 雙宿主、實際只讀 R4
   及獨立 Hosting 覆蓋率；檢查案例轉移數，不以零案例或舊 DLL 當成功。
4. 再接 R2／R3／R9；搬移和 R4 通過本身不完成三項新規則，也不完成 P2-01。

## 2026-11-07 架構掃描修復切片

- [x] 保留 f595903d／f4e5bd4d，重現 source.Length 編譯錯誤。
- [x] 防繞過11案取得8失敗／3通過紅燈，改Roslyn後擴充合成與真XML反例。
- [x] 納入官方SDK metadata、global using、Debug／Release、相依閉包與來源大小／UTF-8預算。
- [x] 建立逐項未通過R2清冊，移除整個ChurchReport不限量豁免。
- [ ] 完整Hosting Debug／Release、獨立coverage、Localization雙宿主與CI filter整合。
- [ ] 真正完成R2舊主機搬移並清空未通過清冊；追蹤綠燈不能勾選本項。
- [x] 將 `ChurchReport.Logging.ExceptionLoggerProvider` 真正移入 Hosting，以 ChurchReport `TypeForwardedTo` 保留完整 CLR 身分；先跑相容與 owner 生命週期紅燈，再跑 Debug／Release、既有例外管線及精確 R2 清冊比對，僅通過後移除對應兩筆。Debug／Release 相容＋例外管線各 4／4；R2 清冊比對 2／2，證據見 `evidence/provider-forwarder-verification-20261107-1818/` 與 `evidence/provider-forwarder-r2-green-20261107-1825/`。其他 R2 差距仍未清除。
- [ ] 核對原始TRX案例集合轉移、編碼／文件／I18n／duplicate、主代理審查及本地提交。

`architecture-relocation-integrity.json` 只證明剛搬移時的本文完整性，後續四個架構檔
有實質修復，不可引用其舊hash宣稱最終本文仍未變更。早期audit關閉的測試只保留
作診斷；接續切片已正常啟用audit還原，正式驗證不得再使用停用選項。

## Hosting獨立覆蓋率修復

正常稽核還原後完整Hosting Release96／96；Debug首輪94／96，兩案因缺deps／runtimeconfig
失敗，Rebuild重建產物後focused5／5及完整96／96通過，保留原失敗TRX。獨立coverage
96案通過但行率58.48%低於80%，不可宣告門檻通過。主要診斷路徑的八個舊特性案例
仍由ChurchReport編譯，故將純共用的ExceptionDiagnosticsTests實際搬入Hosting.Tests，
原HTTP管線、型別轉送ABI與產品組合根案例留在ChurchReport，避免反向參照或重複編譯。
Namespace有明確轉移映射；必須以新TRX逐方法確認八案轉移且未刪除，重新完整回歸與
新鮮獨立coverage，不覆寫58.48%的失敗報告。

## 2026-11-07 12:07 Asia/Taipei (UTC+08:00) 接續證據

- Hosting最新Debug104／104；Release coverage完整104／104，模組行率83.29%達80%。
  初次58.48%失敗原始XML與log完整保留，不挑報告、不降門檻。
- ChurchReport精確回歸Debug／Release各82／82；原90案中的8個共用diagnostics案
  真正搬到Hosting.Tests。Localization net10原209＝184保留＋25架構移轉；
  24案身分不變，舊R4方案案擴成Debug／Release兩案，保留Assert.Empty R4斷言。
  net48原182案逐案不變。architecture-case-transfer.json使用多重集合，
  不依顯示名dictionary去重，保留原testId與唯一executionId及Counters核對。
- 文件1266／0、duplicate32專案1046檔零新增／零stale、I18n verify通過；
  最新Localization兩目標重建後184／184、182／182。
- 11份維護文字純換行修復已核對去換行本文／JSON語意不變。三份自動trace與
  兩份原始solution JSON保留原貌，完整Changed守門仍未通過，不擴張排除來變綠。
- R2清冊仍Debug338／Release316筆，架構追蹤綠燈不能勾選R2及整包驗收。
  本子任務維持in_progress，不封存；後續P2獨立需求不等待歷史差距全部解決。
