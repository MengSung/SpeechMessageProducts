# LineMessagingProcessor 升級 - 快速參考

## 一鍵升級

### Windows PowerShell
```powershell
cd "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport"
.\ChurchReport\文件\升級LineMessagingProcessor\升級腳本.ps1
```

## 升級前檢查清單

- [ ] 已關閉 Visual Studio
- [ ] 已安裝 .NET 10 SDK
- [ ] 已備份專案 (或使用 Git)
- [ ] 已審閱變更內容

## 升級後檢查清單

- [ ] 重新開啟 Visual Studio
- [ ] 還原 NuGet 套件
- [ ] 重新建置專案 (無錯誤)
- [ ] 執行測試 (如有)
- [ ] 驗證繁體中文顯示

## 關鍵變更速查

### RestSharp API 變更

| 舊 API (v105.x) | 新 API (v112.x) |
|----------------|----------------|
| `new RestClient(url)` | `new RestClient(new RestClientOptions(baseUrl))` |
| `new RestRequest(Method.POST)` | `new RestRequest(resource)` + `.PostAsync()` |
| `new RestRequest(Method.GET)` | `new RestRequest(resource)` + `.GetAsync()` |
| `restClient.PostAsync(req, callback)` | `await restClient.PostAsync(req)` |
| `restClient.Get(req)` | `await restClient.GetAsync(req)` |

### 方法簽名變更

| 方法 | 舊簽名 | 新簽名 |
|-----|--------|--------|
| SendMessage | `async Task SendMessage(...)` | `async Task SendMessage(...)` ? |
| GetUserProfile | `List<UserProfile> GetUserProfile(...)` | `async Task<UserProfile> GetUserProfile(...)` |
| GetUserDisplayName | `String GetUserDisplayName(...)` | `async Task<String> GetUserDisplayName(...)` |
| NotifyLineBinding | `void NotifyLineBinding(...)` | `async Task NotifyLineBinding(...)` |

## 常見問題

### Q1: 編譯錯誤 "CS0117: 'Method' 未包含 'Post' 的定義"
**A**: 這表示仍在使用舊專案檔案，請執行升級腳本。

### Q2: 如何回滾？
**A**: 
```powershell
Copy-Item "LineMessagingProcessor\LineMessagingProcessor.csproj.backup" "LineMessagingProcessor\LineMessagingProcessor.csproj" -Force
```

### Q3: 繁體中文亂碼？
**A**: 確保檔案編碼為 UTF-8 with BOM，並檢查 HTTP 請求是否包含 `charset=UTF-8`。

## 支援資源

- ?? 詳細指南: `執行指南.md`
- ?? 完整總結: `升級完成總結.md`
- ?? 升級腳本: `升級腳本.ps1`

## 聯絡

如有問題，請查閱文檔或聯絡開發團隊。
