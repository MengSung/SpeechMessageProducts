# CLDR 時區映射來源

- 釘選 Unicode CLDR **release-49**，不是執行時下載，也不追蹤 main／latest。
- `windowsZones.xml` 完整取自 Unicode 官方 `unicode-org/cldr`：
  `https://raw.githubusercontent.com/unicode-org/cldr/release-49/common/supplemental/windowsZones.xml`
- `UNICODE-LICENSE.txt` 完整保留同 release 的 LICENSE：
  `https://raw.githubusercontent.com/unicode-org/cldr/release-49/LICENSE`
- 取得日期：2026-11-06。`cldr-source.json` 分別記錄上游原始 bytes 與 repo bytes 的 SHA-256。
  唯一正規化為 UTF-8 無 BOM／CRLF；保留完整 XML、DTD 宣告、註解、授權與資料。
  XML 的 `typeVersion` 為該份 Windows 映射使用的 tzdb 版本，不是 CLDR release，不能混為一談。

## 再生與驗證

```powershell
dotnet run --project tools/I18nCheck -c Release -- generate-timezones --cldr tools/I18nCheck/data/windowsZones.xml
dotnet run --project tools/I18nCheck -c Release -- generate-timezones --cldr tools/I18nCheck/data/windowsZones.xml --check
dotnet run --project tools/I18nCheck -c Release -- verify
```

預設產物：`SpeechMessage.Localization/Time/IanaWindowsZones.g.cs`。
`--cldr-version` 可明確指定數字版號，缺省為程式釘選 49；升級時須一併更新原始 XML、完整授權、
來源 SHA、PinnedCldrVersion、重生產物及跨目標時間測試，不能只換檔頭版本。

來源列最多 4096、唯一 IANA 最多 2048、檔案最多 16 MiB。不同 territory 重述相同 IANA→Windows
是 CLDR 規則的同值重述；若同一 IANA 指向不同 Windows ID 則整份拒絕，不最後寫入者勝出。
輸出固定 Ordinal 排序且不含時間戳記／來源列序或動態容器。生成時只 Ignore DTD，絕不解析
外部 DTD／實體；未宣告的實體引用拒絕。檔案只由一次命令持有，驗證完才原子替換。

對照只處理 ID 名稱，不內嵌 DST 規則；實際規則由 OS／ICU 擁有。八個正式時區的 2027 向量
須在 net48（實載 net462 產品）及 net10.0 都通過。本機驗證不能冒充 D365 沙箱實機結果。
