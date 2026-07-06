# DediationLineLoginView 404 錯誤診斷報告

## 錯誤資訊

### 發生的錯誤
```
URL: https://jesusback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy
HTTP ERROR 404
This jesusback.speechmessage.com.tw page can't be found
```

### 關鍵觀察
1. **使用的 Port**: 479
2. **使用的協議**: HTTPS
3. **域名**: jesusback.speechmessage.com.tw
4. **路徑**: `/Dedication/DediationLineLoginView/2007156647-OYnN8BKy`
5. **LIFF ID**: 2007156647-OYnN8BKy

---

## 問題診斷

### ? 應用程式層級檢查

#### 1. 路由配置 - 正確 ?
```csharp
// Startup.cs 第 194-197 行
routes.MapRoute(
    name: "dedicationlinelogin",
    template: "Dedication/DediationLineLoginView/{LineIdLoginViewPatameter}",
    defaults: new { controller = "Dedication", action = "DediationLineLoginView" });
```

#### 2. Controller 方法 - 正確 ?
```csharp
// DedicationController.cs
[Route("/Dedication/DediationLineLoginView/{LineIdLoginViewPatameter}")]
public IActionResult DediationLineLoginView(string LineIdLoginViewPatameter)
{
    // 方法實作正常
}
```

#### 3. 向後相容路由 - 正確 ?
```csharp
// HomeController.cs
[Route("/Home/DediationLineLoginView/{LineIdLoginViewPatameter}")]
public IActionResult DediationLineLoginViewRedirect(string LineIdLoginViewPatameter)
{
    return RedirectToAction("DediationLineLoginView", "Dedication", new { LineIdLoginViewPatameter });
}
```

#### 4. 視圖檔案 - 存在 ?
```
ChurchReport\Views\Home\DediationLineLoginView.cshtml
```

---

## ?? 根本原因分析

### 問題 1: 部署配置問題（最可能）

#### Port 479 的問題
- **Port 479** 是一個**非標準**的 HTTP/HTTPS port
- 標準 HTTPS port 是 443
- IIS 或 Kestrel 可能沒有正確配置監聽 port 479

#### 檢查項目
```bash
# 1. 檢查應用程式是否在 port 479 上運行
netstat -ano | findstr :479

# 2. 檢查 IIS 綁定
# IIS Manager → 網站 → 綁定 → 確認有 port 479

# 3. 檢查防火牆規則
netsh advfirewall firewall show rule name=all | findstr 479
```

### 問題 2: IIS 應用程式池未啟動

#### 可能原因
- 應用程式池停止運作
- 應用程式初始化失敗
- 記憶體不足導致應用程式池回收

#### 檢查方法
```powershell
# 檢查應用程式池狀態
Get-IISAppPool | Where-Object { $_.Name -like "*ChurchReport*" }

# 重啟應用程式池
Restart-IISAppPool -Name "ChurchReport"
```

### 問題 3: Web.config 配置問題

#### 可能的問題
- `web.config` 中的 `<handlers>` 配置錯誤
- ASP.NET Core Module 未正確安裝
- `aspNetCore` section 配置錯誤

#### 檢查 web.config
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <handlers>
      <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
    </handlers>
    <aspNetCore processPath="dotnet" 
                arguments=".\ChurchReport.dll" 
                stdoutLogEnabled="true" 
                stdoutLogFile=".\logs\stdout" 
                hostingModel="inprocess">
      <environmentVariables>
        <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
      </environmentVariables>
    </aspNetCore>
  </system.webServer>
</configuration>
```

### 問題 4: SSL 憑證問題

#### HTTPS 要求
- 使用 HTTPS 需要有效的 SSL 憑證
- Port 479 需要綁定 SSL 憑證

#### 檢查方法
```powershell
# 檢查 SSL 憑證綁定
netsh http show sslcert ipport=0.0.0.0:479

# 如果沒有綁定，需要添加
netsh http add sslcert ipport=0.0.0.0:479 certhash=[憑證指紋] appid={[GUID]}
```

### 問題 5: URL Rewrite 或反向代理配置

#### 可能情況
- Nginx 或 IIS URL Rewrite 規則錯誤
- 反向代理未正確轉發請求到後端應用程式

#### 檢查 Nginx 配置（如果使用）
```nginx
server {
    listen 479 ssl;
    server_name jesusback.speechmessage.com.tw;
    
    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

---

## ??? 修復步驟

### 步驟 1: 檢查應用程式是否運行

```powershell
# 1. 檢查進程
Get-Process | Where-Object { $_.Name -like "*ChurchReport*" }

# 2. 檢查 port 監聽
netstat -ano | findstr :479

# 3. 檢查 IIS 應用程式池
Get-IISAppPool
```

**預期結果**:
- 應該看到 ChurchReport 進程正在運行
- Port 479 應該處於 LISTENING 狀態
- 應用程式池狀態應該是 "Started"

### 步驟 2: 檢查 IIS 配置

#### 2.1 檢查網站綁定
```
1. 開啟 IIS Manager
2. 展開 Sites
3. 右鍵點擊網站 → Bindings
4. 確認有以下綁定：
   - Type: https
   - Host name: jesusback.speechmessage.com.tw
   - Port: 479
   - SSL certificate: [有效的憑證]
```

#### 2.2 檢查應用程式設定
```
1. IIS Manager → Application Pools → ChurchReport
2. 確認 .NET CLR Version: No Managed Code
3. 確認 Managed Pipeline Mode: Integrated
4. 確認 Start Mode: AlwaysRunning (可選)
```

### 步驟 3: 檢查日誌

#### 3.1 應用程式日誌
```powershell
# 檢查 stdout 日誌
Get-Content "D:\網頁APP雲端線上版本\...\ChurchReport\logs\stdout*.log" -Tail 50

# 檢查 Trace.log
Get-Content "D:\網頁APP雲端線上版本\...\ChurchReport\Logs\Trace.log" -Tail 50
```

#### 3.2 Windows 事件日誌
```powershell
# 檢查應用程式日誌
Get-EventLog -LogName Application -Source "IIS*" -Newest 20 | Format-Table -AutoSize

# 檢查系統日誌
Get-EventLog -LogName System -Newest 20 | Where-Object { $_.Source -like "*IIS*" }
```

### 步驟 4: 測試不同的 URL

#### 4.1 測試根路徑
```
https://jesusback.speechmessage.com.tw:479/
預期: 重導向到登入頁面
```

#### 4.2 測試其他已知路徑
```
https://jesusback.speechmessage.com.tw:479/Home/Login
預期: 顯示登入頁面或重導向
```

#### 4.3 測試 DediationLineLoginView
```
https://jesusback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/test
預期: 顯示奉獻 LINE 登入頁面
```

### 步驟 5: 重啟服務

```powershell
# 1. 重啟應用程式池
Restart-WebAppPool -Name "ChurchReport"

# 2. 重啟 IIS
iisreset

# 3. 等待 30 秒後測試
Start-Sleep -Seconds 30
```

### 步驟 6: 檢查 DNS 解析

```powershell
# 1. 檢查 DNS 解析
nslookup jesusback.speechmessage.com.tw

# 2. Ping 測試
ping jesusback.speechmessage.com.tw

# 3. Traceroute
tracert jesusback.speechmessage.com.tw
```

---

## ?? 常見修復方案

### 方案 1: IIS 未啟動或配置錯誤

```powershell
# 啟動 IIS
Start-Service W3SVC

# 檢查狀態
Get-Service W3SVC

# 重啟網站
Restart-WebSite -Name "ChurchReport"
```

### 方案 2: Port 綁定問題

```powershell
# 檢查 port 綁定
netsh http show urlacl

# 添加 URL 保留
netsh http add urlacl url=https://+:479/ user=Everyone

# 添加 SSL 憑證綁定
netsh http add sslcert ipport=0.0.0.0:479 certhash=[指紋] appid={[GUID]}
```

### 方案 3: ASP.NET Core Module 未安裝

```powershell
# 1. 下載並安裝 ASP.NET Core Module
# https://dotnet.microsoft.com/download/dotnet/thank-you/runtime-aspnetcore-2.1.30-windows-hosting-bundle-installer

# 2. 安裝後重啟 IIS
iisreset

# 3. 驗證安裝
Get-WindowsFeature -Name Web-Server | Select-Object Name, InstallState
```

### 方案 4: 權限問題

```powershell
# 1. 設定應用程式目錄權限
$path = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport"
$acl = Get-Acl $path
$permission = "IIS_IUSRS","FullControl","ContainerInherit,ObjectInherit","None","Allow"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
$acl.SetAccessRule($accessRule)
Set-Acl $path $acl

# 2. 驗證權限
Get-Acl $path | Format-List
```

### 方案 5: 應用程式池身份問題

```
1. IIS Manager → Application Pools → ChurchReport
2. Advanced Settings → Identity
3. 設為: ApplicationPoolIdentity 或自訂帳戶
4. 確保該帳戶有足夠權限訪問應用程式目錄
```

---

## ?? 診斷檢查清單

### IIS 配置檢查
- [ ] IIS 服務正在運行 (`Get-Service W3SVC`)
- [ ] 網站正在運行 (`Get-IISSite`)
- [ ] 應用程式池正在運行且狀態為 "Started"
- [ ] 網站綁定包含 port 479
- [ ] SSL 憑證已正確綁定到 port 479
- [ ] 應用程式池使用 "No Managed Code"
- [ ] 物理路徑正確指向應用程式目錄

### 應用程式檢查
- [ ] `ChurchReport.dll` 存在於部署目錄
- [ ] `web.config` 存在且配置正確
- [ ] `appsettings.json` 存在且配置正確
- [ ] 依賴的 DLL 檔案都已部署
- [ ] ASP.NET Core Runtime 已安裝

### 網路檢查
- [ ] Port 479 沒有被防火牆阻擋
- [ ] DNS 解析正確
- [ ] 可以從伺服器本機訪問 `https://localhost:479/`
- [ ] SSL 憑證有效且未過期

### 日誌檢查
- [ ] 檢查 `stdout` 日誌有無錯誤訊息
- [ ] 檢查 `Trace.log` 有無錯誤
- [ ] 檢查 Windows 應用程式事件日誌
- [ ] 檢查 IIS 日誌 (`C:\inetpub\logs\LogFiles\`)

---

## ?? 緊急修復（如果需要立即恢復服務）

### 臨時方案 1: 使用標準 HTTPS Port (443)

如果 port 479 持續有問題，可以暫時使用標準 port：

```
1. IIS Manager → Sites → 右鍵 → Bindings
2. 添加新綁定:
   - Type: https
   - IP address: All Unassigned
   - Port: 443
   - Host name: jesusback.speechmessage.com.tw
   - SSL certificate: [選擇有效憑證]
3. 更新 LIFF URL 為: https://jesusback.speechmessage.com.tw/...
```

### 臨時方案 2: 重新部署應用程式

```powershell
# 1. 停止應用程式池
Stop-WebAppPool -Name "ChurchReport"

# 2. 清理舊檔案
$deployPath = "D:\網頁APP雲端線上版本\...\ChurchReport"
Remove-Item "$deployPath\*" -Recurse -Force -Exclude "appsettings.json","web.config"

# 3. 複製新的部署檔案
Copy-Item "發布目錄\*" -Destination $deployPath -Recurse -Force

# 4. 啟動應用程式池
Start-WebAppPool -Name "ChurchReport"

# 5. 等待並測試
Start-Sleep -Seconds 30
```

---

## ?? 報告與記錄

### 問題記錄範本
```
日期時間: _________________
問題描述: DediationLineLoginView 404 錯誤
錯誤 URL: https://jesusback.speechmessage.com.tw:479/Dedication/DediationLineLoginView/2007156647-OYnN8BKy

執行的診斷步驟:
1. [ ] 檢查 IIS 服務狀態
2. [ ] 檢查應用程式池狀態
3. [ ] 檢查 port 479 綁定
4. [ ] 檢查 SSL 憑證
5. [ ] 檢查應用程式日誌
6. [ ] 檢查 Windows 事件日誌

發現的問題:
_______________________________________

執行的修復:
_______________________________________

修復結果:
□ 成功 □ 失敗 □ 部分成功

後續行動:
_______________________________________
```

---

## ?? 相關資源

### Microsoft 文檔
- [Troubleshoot ASP.NET Core on IIS](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/troubleshoot)
- [ASP.NET Core Module](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/aspnet-core-module)

### 內部文檔
- `DediationLineLoginView登入失敗修復報告.md`
- `DediationLineLoginView向後相容路由報告.md`
- `根目錄404錯誤診斷.md`

---

## ?? 支援聯絡

### 技術支援
- Email: tech@jesus.org
- 緊急電話: [填入]

### 伺服器管理員
- 姓名: [填入]
- 聯絡方式: [填入]

---

**重要提示**: 這是一個**部署環境問題**，不是程式碼問題。應用程式層級的配置都是正確的。問題出在 IIS/伺服器配置上。

**建議優先檢查**:
1. ? IIS 服務和應用程式池狀態
2. ? Port 479 的網路綁定
3. ? SSL 憑證配置
4. ? 應用程式日誌

**快速修復方向**: 重啟 IIS 和應用程式池，如果仍然失敗，檢查 port 479 的綁定和憑證配置。
