# Localization 地基契約

## 1. 適用範圍與觸發

P1-05 的 `SpeechMessage.Localization` 同一來源編譯 net462／net10.0；不相依 HTTP、
Extensions 或 D365。後續 host、覆寫來源、快取與產品路由必須使用此已驗證契約。
新增文化或詞彙只修改 resx，再由 I18nCheck 產生，禁止手改生成資料。

## 2. 具體介面

```csharp
new TextContext(Guid churchId, string textCulture, string formatCulture,
    string organization = "default", string product = "ChurchReport");
TextContext WithCultures(string textCulture, string formatCulture);
bool HasSameScope(TextContext other);
IChurchTextOverrideSnapshot GetSnapshot(TextContext context);
string ITextCatalog.GetText(TextContext context, TextKey key, params TextArg[] args);
bool SystemTextTable.TryGetOriginalText(string culture, TextKey key, out string text);
ResolvedCulture IMemberCultureResolver.Resolve(MemberLanguagePreference preference, ChurchLocaleSettings church);
void LocalizationOptions.Validate(bool isProduction, IReadOnlyList<string>? configuredCultures = null);
bool IKeywordCatalog.TryMatch(string memberCulture, IReadOnlyList<string> churchCultures,
    string input, out string intentCode);
```

`KeywordCatalog` 另接受固定語系、穩定 key→Exact/Prefix/Contains 模式，以及 `priorityOrder`。
priority 由產品核定，不能讓翻譯字長或字典枚舉順序變更舊路由優先權。

## 3. 輸入、輸出與生命週期

- host 先授權完整 organization/product/church 邊界，再建立 context；建構值物件不是授權。
  WithCultures 不能修改邊界；catalog 每次核對 provider 快照的完整 scope。
- 五層是教會 exact → 教會 * → system exact → parent/alias → zh-TW。
  原 `GetText/GetTable` 已預展開繁中，不能用它們判斷某語系是否真的有翻譯。
  zh-HK／zh-MO／zh-Hant 使用繁中別名優先，再處理 BCL 父文化。
- 快照先驗候選，再封裝兩層字典；只保存純文字及不可變 context，不保存 SDK Entity／連線。
  正式 ID、同 key/culture 不可重複；不同 key 的相同顯示文字完整保留。
- 模板與 I18nCheck 共用非巢狀 `{name}` 文法。先驗完整參數集合，才可執行任意 IFormattable。
  日期／數字使用 context.FormatCulture；只替換一次，插入值不能再次成為模板。
- enabled/released 交集選偏好，再教會預設，最後 zh-TW；英文預設未發布。
  qps-ploc 須 opt-in、教會啟用且格式 en-US；正式啟動用 Validate 拒絕任何偽語系設定。
- Keyword NFKC、Unicode 空白 trim/collapse、Invariant lower。跨所有可共用文化的同字異意圖建表拒絕。
  會友→教會其他→繁中；各文化 exact 先於 partial，partial 依明確模式與 priority。
- 上限：snapshot 5000 列、模板 32768、參數 256、語系 64、關鍵字別名 20000、輸入 16384。
  不儲存呼叫輸入、偏好、context 或參數值；固定系統資源可長期唯讀共用。
  元件本身無 I/O、timer、queue、背景 Task 或 disposable owner。

## 4. 驗證與錯誤矩陣

| 條件 | 結果 |
| --- | --- |
| 空教會／組織／產品或非法格式文化 | 建構拒絕，尚未查資料 |
| provider 為 null 或回另一 scope | InvalidOperationException |
| 五層全部缺鍵 | KeyNotFoundException |
| 參數缺少／多出／重複／default | FormatException，先於值的格式化 |
| 正式覆寫 ID／key+culture 衝突或外教會 | 整個候選拒絕，不發布部分 |
| provider 真正故障 | 原 Exception 向 host 共用告警入口傳遞，下次可重試 |
| 正式環境偽語系配置 | 啟動拒絕 |
| normalized 同字異意圖 | 建構拒絕，不以順序掩蓋 |
| 關鍵字非法 Unicode／空白／過長／未命中 | false，意圖清空，不記錄輸入 |

## 5. Good／Base／Bad

- Good：en-GB 缺譯、en 有譯，回 en；未知文化只回既有繁中，不能增加動態表。
- Base：英文資源含舊 Pray 等命令，但未發布英文偏好仍回教會繁中。
- Bad：只比 ChurchId，讓另一組織／產品的快照通過；按相同文字去掉不同正式 key；
  使用 Regex.Replace 迴圈重解析插入值，或缺參數仍先執行自訂格式化。

## 6. 必要測試與決定性斷言

`TextCatalogTests`：五層各案、全部缺鍵、alias、參數集合、明確文化、
真實 resolver→生成 sender 資源、三種 foreign scope 拒絕、來源 mutation、
A/B 各千次並行、原例外保真／下次重試，以及長生命 catalog 下 context／值弱參照回收。

`MemberCultureResolverTests`：enabled/released、英文未發布、pseudo opt-in、Production 三種拒絕來源、
來源設定 mutation、未知偏好 fallback。

`KeywordCatalogTests`：全形與空白、六個真英文命令、繁中永遠可用、同／跨文化衝突、
模式及重疊前綴 explicit priority，非法輸入不能留下舊意圖。

保留原 43 字典案例與 net10 架構測試。測試宿主 net48 必須反射產品 net462；
各產品覆蓋率獨立 ≥80%，strict 雙目標、I18n generate --check／verify 及方案回歸均須通過。

## 7. 錯誤與正確方式

錯誤：

```csharp
// 已展開的繁中會掩蓋真正英文父語系；裸教會 ID 也丟失完整範圍。
var text = system.GetText("en-GB", key);
var snapshot = provider.GetSnapshot(context.ChurchId);
```

正確：

```csharp
var snapshot = provider.GetSnapshot(context);
if (!context.HasSameScope(snapshot.Context)) throw new InvalidOperationException();
// catalog 按每層 TryGetOriginalText 的真實存在性解析，再驗完整參數集合。
var text = catalog.GetText(context, key, new TextArg("name", suppliedName));
```

P1-08 的資料來源及 cache 必須以完整 context 分區，並另實作來源授權、驗證、
single-flight、invalidate、失敗保留舊快照與 owner drain；本包的純型別不替代這些工作。
