# Hosting 生成語系完成度與啟動政策契約

## 1. 適用範圍

P2-01 的 I18nCheck → Localization 雙目標產物 → Hosting → ChurchReport 組合根。
翻譯完成度必須來自全部原始資源，不能用 SupportedCultures 的固定發布標籤或回退後文字代替。
本契約不代表 R2／R3／R9、P2-04 安全實機或整個 P2 已完成。

## 2. 入口

```powershell
dotnet run --project tools/I18nCheck -c Release -- generate-completeness
dotnet run --project tools/I18nCheck -c Release -- generate-completeness --check
dotnet run --project tools/I18nCheck -c Release -- generate --check
dotnet run --project tools/I18nCheck -c Release -- verify
```

產物 `SpeechMessage.Localization/CultureCompleteness.g.cs` 提供
`TotalKeyCount`、`GetTranslatedKeyCount(string?)`、`IsComplete(string?)`。
Hosting 提供 `ValidateLocalizationConfiguration(IConfiguration, bool)`，回傳
已驗證且與來源分離的 `LocalizationOptions`。

## 3. 資料、發布及生命週期

- 分母為方案全部中性資源的唯一鍵；相同顯示文字但不同鍵完整保留，跨組重複鍵拒絕生成。
  分子只計原始非空白翻譯，不計繁中回退、缺譯或生成偽語系；零鍵不能算完整。
- 延用 ResourceSet／ResourceCatalog 的根內來源、XML、大小與鍵驗證；額外限制全域
  最多兩萬鍵／六十四文化。輸出只含計數，不含來源本文、路徑或時間戳。
- 產生器先完成操作區域候選再寫入，使用 LocalFiles 原子替換與 finally 清理；
  `--check` 永不改檔。`generate --check` 與 `verify` 在正式 Localization 存在時強制比對完成度，
  與其餘資源生成檢查借用同世代完整候選；刪除產物不得跳過守門。
- `Localization:ReleasedCultures` 接受最多六十四項連續 0 起算索引的純字串陣列；
  缺省繁中。`Localization:AllowPseudoLocale` 缺省 false，有值就須能解析為布林。
  scalar、巢狀、稀疏索引、空白、重複、未知或未完成文化皆拒絕，不截短或靜默去重。
- 所有環境都驗完成度。偽語系只允許非正式環境明確 opt-in；正式環境的設定或教會
  啟用語系出現偽語系即拒絕。啟用的普通語系同時必須在 ReleasedCultures 內。
- Main 在唯一診斷 owner 建立後依序驗 ICU、讀取／驗證語系政策並註冊不可變 snapshot，
  再進 Startup／CRM／HTTP；失敗仍走原 exception 與逐階清理。
  不把 IConfiguration/provider 保存到政策，也不訂閱 reload 或增加靜態快取。

## 4. 錯誤矩陣

| 條件 | 結果 |
| --- | --- |
| en 缺譯、空白或只存在回退 | IsComplete=false；任何環境不得當已發布 |
| 空資源／未知／null／qps-ploc | IsComplete=false |
| 重複鍵、全域超預算、非法目的地 | CLI=2，舊產物不變 |
| 缺少或過期完成度產物 | check=1，不自動修復 |
| 錯誤組態或未完成發布語系 | 固定 InvalidOperationException，不回顯原設定值 |
| CultureInfo 解碼拒絕 | 只在組態解碼邊界轉固定錯誤，不附含原值的 InnerException |

## 5. 正常／基準／錯誤案例

正常：所有原始鍵都有非空白 en 翻譯，重生後 en 完成度 100%，Host 才能發布。
基準：目前 45 鍵、en 6 鍵；繁中完整、英文 13.33%，不能開放 en。
錯誤：只數第一組資源、將空白也算完成、用 fallback 取得的繁中冒充英文或只在 Production 驗證。

## 6. 必要驗證

- CultureCompletenessTests：真正 CLI 生成、Roslyn 編譯／執行產物、全組分母、空白／缺譯、
  偽語系／未知／null、stale 唯讀與重生恢復、重複鍵舊輸出保留、兩 repo 並行隔離。
  probe 的 MemoryStream 確定 Dispose，可回收 AssemblyLoadContext 在 finally Unload。
- StartupCulturePolicyTests：所有環境未完成語系拒絕、pseudo opt-in、合成組態形狀、
  六十四上限、固定安全錯誤、來源修改不影響已建立 snapshot。
- ChurchClockStartupTests 保護真 Main 的 owner → ICU → 政策 → DI → Run → catch 順序；
  保留既有 ICU 子程序與診斷清理測試。
- 工具完整測試、Localization net462／net10 建置及雙宿主回歸、Hosting Debug／Release、
  ChurchReport 相關回歸、I18n verify、文件、編碼與進度頁守門。

## 7. 錯誤與正確方式

錯誤：手改 `CultureCompleteness.g.cs`、直接把 SupportedCultures 的狀態當原始翻譯證據，
或 `bool.TryParse` 失敗仍靜默用 false。

正確：修改 resx 後從正式 CLI 重生，check 逐位元組一致；Host 解析受信任啟動組態、
驗證完整度，再借出不可變政策給同一 Host，不保留 provider／使用者狀態。
