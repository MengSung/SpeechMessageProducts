# Phase 4 Claims／IFD 精靈畫面證據 — 2026-07-31

## 已直接觀察到的事實

- CE 9.1 的正式 Web API root 是
  `https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1/`。
- D365APP01 上的唯讀 `WhoAmI` 已到達 CRMWeb，但曾得到 HTTP 500；事件記錄的第一個
  CRM frame 是
  `Microsoft.Crm.Authentication.Claims.CrmFederatedAuthenticationModule.UpdateRedirectingEventArgsNonPathBasedUrl`。
- Claims-Based Authentication 精靈的 system check 顯示 Federation Metadata URL 與加密憑證皆通過。
- IFD 精靈的現行輸入畫面使用裸主機名：
  `auth.speechmessage.com.tw`、`speechmessage.com.tw`、
  `speechmessage.com.tw`、`discodev91.speechmessage.com.tw`；IFD system check 亦全部通過。
- 另一張 IFD 摘要畫面把 external domain 呈現為
  `https://auth.speechmessage.com.tw`。摘要的 URI 呈現方式不能單獨證明持久化欄位含有
  scheme，因此不能把它當成已證實的設定缺陷。

## 判讀與安全邊界

目前證據把問題定位於 CRM 的 Claims／IFD federation redirect URI 建構，而非 Gateway、DNS、
Kerberos、WinRM、SQL、AD FS OAuth client、IIS 或 Registry。這仍是候選設定問題，不是已驗證的
單一欄位根因；不可因為精靈 system check 通過就宣告 CRMWeb 已修復。

精靈的欄位契約如下：所有 IFD domain/root 欄位都必須是無 scheme、path、port 與空白的裸
hostname/domain；Federation Metadata URL 則必須是絕對 HTTPS URI。

## 唯一一次可接受的設定動作

1. 若本次精靈工作階段確實把 External domain 從完整 URL 改為裸主機名，僅套用這一個既有變更
   一次，然後等待精靈成功完成。
2. 若精靈一開啟就已經顯示裸主機名，取消／關閉，不重套、不重跑精靈。
3. 不修改其餘 IFD 欄位，也不以 DNS、AD FS、IIS、SQL、Registry、Basic、NTLM、TrustedHosts 或
   未加密 WinRM 作為替代修正。

## 套用後的最小驗證順序

1. 在既有核准的 D365APP01 Deployment Manager／DWS 管理工作階段重新讀取 Claims 與 IFD 設定，
   確認裸主機名已持久化；沒有該權限時，只記錄此 gate，勿以 SQL、Registry 或 IIS 代替讀取。
2. 從同一個核准的 D365APP01 網域工作階段僅執行一次 connector-owned smoke：

   ```powershell
   .\docs\scripts\Invoke-DynamicsLiveSmoke.ps1 `
     -EnableLive `
     -WebApiRoot 'https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1/' `
     -CeVersion '9.1' `
     -CredentialSource HostIdentity `
     -NoRestore
   ```

   此工具會在 `finally` 還原全部 `DYNAMICS_SMOKE_*` Process 環境變數；不會留下 profile、
   endpoint、credential-source 或 secret-reference 名稱到互動 PowerShell session。
3. `WhoAmI` 成功才是 CRMWeb gate 已通過的證據。若仍為 500，保留新的事件時間點並繼續受支援的
   CRM 管理診斷，絕不再次套用同一設定。若改為非 500 的認證／redirect 回應，代表 URI 建構可能已
   越過原先的例外，但非密碼 service-workload authentication 仍須另外證明，Phase 4 仍不可結案。

## Phase 狀態

- Phase 4 的本機 isolation、lifecycle、bounded smoke harness 與測試證據仍有效。
- 真機 CE 9.1 Claims／IFD gate 仍為 in progress，且 Phase 5 的產品流量與 Phase 6 的 SDK 移除
  仍保持鎖定。
