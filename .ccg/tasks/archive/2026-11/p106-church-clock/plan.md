# P1-06 Implementation Plan

> 執行技能：superpowers:executing-plans；現行 inline，禁止 implement/check 子代理。

**Goal:** 交付正式 P1-06 的雙目標教會時鐘與可再生對照，並保存可核對驗收證據。
**Architecture:** Localization 純時間型別及明確 zone 轉換，不引用 host／SDK；net10 借用 TimeProvider。
**Spec:** 主 Trellis design.md／implement.md Task 6、正式 02 P1-06、90 C.1。

## 檔案與責任

- `SpeechMessage.Localization/Time/`：IClock、IChurchClock、local／date-only 型別、ChurchClock、TimeProvider adapter、ICU guard、generated mapping。
- `SpeechMessage.Localization.Tests/ChurchClockTests.cs`：正式向量、Kind／DST 拒絕、來源故障、跨時區並行及回收。
- `tools/I18nCheck/TimeZoneGenerator.cs`、`CommandRunner.cs`：受界限 XML 讀取、唯一確定映射、deterministic 生成／check。
- `tools/I18nCheck/data/`：釘選 CLDR release 原始 XML、授權及來源 SHA 說明。
- `tools/I18nCheck.Tests/TimeZoneGeneratorTests.cs`：生成正反例、衝突拒絕、DTD 不解析、輸出不可變／位元組相同。
- `SpeechMessageProducts.ChurchReport/Program.cs`、`ChurchReport.MemberInfo.Tests/Contracts/ChurchClockStartupTests.cs`：共用通知 owner 建好後、業務 Host 前實際檢查 ICU，保留原例外與 finally cleanup。
- 主任務、進度頁、驗收／review／journal：只記真實執行狀態，不更改既有完成證據。

## 切片

- [x] 讀完規範與官方來源，確認 IClock 舊相容介面不搬業務；保存分析與設計理由。
- [x] 寫正式向量與拒絕測試，先執行取得缺型別／缺行為真紅證據。
- [x] 實作純 clock／local／date-only 契約與 net10 adapter。
- [x] 生成器先真紅，再保存釘選來源／授權，生成對照；接入 CLI 與 verify。
- [x] 雙目標綠、ICU 子程序正反例、並行及回收、每目標獨立 coverage。
- [x] 主會話審查、完整方案回歸／00§8、編碼／duplicates／進度頁；修正後重跑。
- [x] 需求→位置→測試→證據矩陣、更新一般資訊、本地 commit、子任務封存。

## 已決定

- 不把 legacy `ToolUtilityNameSpace.TimeOperations.IClock` 的 `DateTime LocalNow` 改為教會時間，避免 P1-04 相容契約破壞；正式最小 IClock 位於 Localization，後續業務遷移才轉接。
- 不快取 caller 時區／教會／profile；內建表只有固定公共 CLDR 資料。
- 真正來源故障原樣向 host 傳播；純函式不建立自己的告警 client。
- CLDR 升級與 OS 時區規則更新為不同責任；八區向量證明本機支援，不冒稱 D365 沙箱實機驗收。
- 重新核對正式 Word 8.2／E.5 及階段1國際化步驟後，確認不能只留下「未來 Hosting 呼叫」helper；本包即接入現有 ChurchReport，後續 Hosting 沿用。這是修正實作遺漏，不改正式需求，無 Word 修正報告需求。
