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
- [ ] 核對原始TRX案例集合轉移、編碼／文件／I18n／duplicate、主代理審查及本地提交。

`architecture-relocation-integrity.json` 只證明剛搬移時的本文完整性，後續四個架構檔
有實質修復，不可引用其舊hash宣稱最終本文仍未變更。早期audit關閉的測試只保留
作診斷；接續切片已正常啟用audit還原，正式驗證不得再使用停用選項。
