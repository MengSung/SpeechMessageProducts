# Dynamics CE 9.1 AD FS OAuth permission 最小權限審查

## 角色與目標

請以 AD FS、Dynamics 365 CE 9.1 IFD、OAuth 2.0、零 Session Leakage 與最小權限架構師身分，審查目前實機變更是否安全，並提出可執行的保留、修正或回滾決策。

## 已確認環境

- DC／AD FS：`D365DC01`（`192.168.50.10`），Windows Server 2022，AD FS service 正常。
- Dynamics：`D365APP01`（`192.168.50.20`），IIS、SQL Server 與 Dynamics 服務正常。
- AD FS hostname：`adfsdev91.speechmessage.com.tw`。
- Dynamics CE 9.1 IFD resource：`https://sunnyvalechback.speechmessage.com.tw/`。
- ChurchReport 設定中的 public ClientId：`2ad88395-b77d-4561-9441-d0e40824f9bc`。
- 固定 Redirect URI：`http://localhost:43371/diagnostics/adfs-callback`。
- `Package01FeeReadsEnabled=false`，目前禁止真實 fee reads。

## 已執行的加法式變更

1. 使用 `Add-AdfsClient` 建立：
   - Name：`SpeechMessage-ChurchReport-LocalDev`
   - ClientType：`Public`
   - ClientId：上述固定 GUID
   - Redirect URI：上述固定 localhost callback
   - 無 client secret
2. 原先 authorize request 回傳 `MSIS9605: The client is not allowed to access the requested resource.`。
3. 依 Microsoft 官方 `Grant-AdfsApplicationPermission` 文件，執行：

```powershell
Grant-AdfsApplicationPermission `
  -ClientRoleIdentifier "2ad88395-b77d-4561-9441-d0e40824f9bc" `
  -ServerRoleIdentifier "https://sunnyvalechback.speechmessage.com.tw/"
```

4. 變更後 authorize endpoint 回 HTTP 200 ADFS 登入頁，不再回 MSIS9605。
5. `Get-AdfsApplicationPermission` 顯示 AD FS 將 ServerRoleIdentifier 正規化為：
   `https://auth.speechmessage.com.tw/`。

## 重要風險事實

`Dynamics 365 IFD External` 是一個共用 Relying Party Trust，Identifiers 包含：

- `https://auth.speechmessage.com.tw/`
- `https://david.speechmessage.com.tw/`
- `https://discodev91.speechmessage.com.tw/`
- `https://elijah.speechmessage.com.tw/`
- `https://solomon.speechmessage.com.tw/`
- `https://speechmessage.speechmessage.com.tw/`
- `https://sunnyvalechback.speechmessage.com.tw/`

因此 permission 可能是對整個共用 RP，而不是只對 sunnyvalechback identifier。Gateway 程式正在新增 server-owned principal → workload → alias → operation 授權；產品 JSON 不具授權效力，且 queue/runtime 不保留 user、session、token 或 raw principal。Local Gateway 僅供 Development，正式目標仍是 Central Gateway。

## 必答問題

1. 此 public-client permission 是否實際允許相同 ClientId 對共用 RP 內其他 identifiers 取得 token？請區分 AD FS permission、OAuth resource/audience、Dynamics 使用者權限與 Gateway policy 各層。
2. 在 `Package01FeeReadsEnabled=false`、固定 localhost callback、Gateway 嚴格 alias/operation policy、Token 僅由 profile-generation owner 保管且確定性清理的條件下，暫時保留這個 permission 作 Development E2E 是否可接受？
3. 若不可接受，最小且 Microsoft 支援的替代方案是什麼：
   - 將 sunnyvalechback 拆成獨立 Relying Party Trust；
   - 建立 AD FS Application Group／Web API role；
   - 使用另一個官方 OAuth／ServiceClient 認證路徑；
   - 或完全回滾 public client permission？
4. 請列出具體回滾命令與回滾後驗證。
5. 請列出 Local Gateway browser E2E 前必須完成的 security／session／resource-lifecycle gates。
6. 是否需要修改 permission Description，使其明確記錄「permission 綁定共用 Dynamics IFD RP，實際路由由 Gateway policy 限制」？

## 硬性限制

- 不得建議 Header principal、ROPC、TrustedHosts=`*`、Basic、AllowUnencrypted、CredSSP、明文密碼或 token 寫入 JSON／repo。
- 不得讓產品直接取得 CRM endpoint、credential 或任意 alias。
- 不得把 LocalDB、單機 Local Gateway 或 public client 驗證宣稱成 Central multi-host production 證據。
- Session／Token／Memory／Resource Leakage 為 release blocker。

## 輸出格式

1. 決策：KEEP / KEEP_WITH_GATES / ROLLBACK。
2. Critical／Warning／Info 分級。
3. 精確 AD FS 命令與驗證命令。
4. Browser E2E 前置 Gate 清單。
5. 明確指出哪些結論需實機 token／WhoAmI 才能證明。
