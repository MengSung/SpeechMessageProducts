# P7.4 認證憑證策略安全邊界設計

## 設計結論

此 child 不新增執行期程式碼。它建立一條不可跨越的邊界：**contact read 與 credential
verification 是兩個不同 capability。** 前者只投影可安全顯示或定位的資料；後者若存在，
必須在 secret owner 內完成比對並只輸出固定、非機密的驗證結果。

## 方案比較

| 方案 | 結論 | 原因 |
| --- | --- | --- |
| A. 將 contact typed-read DTO 接入帳密登入 | 拒絕 | DTO 沒有 password/hash；加入 secret 會讓 ProductClient、wire、log 與 session 增加洩漏面。 |
| B. 新增 `auth.contact.credential.verify` | 未來唯一可行方向 | 驗證在受控 trust boundary；輸出只能是 allowlisted outcome，仍須先有核准的 credential source／migration 設計。 |
| C. 只把 LINE lookup 接入現有登入 | 本 child 不採用 | 現有後段仍需 legacy `Entity` 與 Session 內容；DTO rehydration 或第二次 legacy lookup 都違反 DTO-only/no-fallback。 |

## 未來 B 的資料流（尚未實作）

```text
Browser account/password (untrusted locator + secret)
  -> server authentication boundary validates workload/authorization
  -> fixed operation auth.contact.credential.verify
  -> controlled executor owns secret comparison and uncertain-transport disposal
  -> fixed non-secret outcome
  -> independently designed session handoff
```

- profile、organization、connector、endpoint、credential、operation 與 owner 都由 deployment／
  server composition 決定，不能由 browser、LINE payload、Session 或 DTO 指定。
- 資源 owner 是既有 process-host executor generation；credential verification 不可新建
  每 request provider、pool、client、static cache 或 background retry。
- timeout、cancellation、fault、ambiguous match 或 cleanup uncertainty 均為 fail-closed；
  不把 uncertain client 放回 pool，也不嘗試第二條 legacy 路徑。

## 最小結果合約（僅作設計，不建立型別）

```text
CredentialVerificationOutcome =
  verified | invalid-credentials | ambiguous | profile-unavailable
```

`verified` 不能暗示可輸出 password、hash、CRM entity 或 profile／endpoint。是否需要後續
contact projection 必須由新的、已授權 child 定義為另一個 server-authorized request-local 步驟；
它不能從 browser 指定 target，也不能以 cached/session entity 取代 authorization。

## 回滾與相容性

因為本 child 零執行期改動，回滾就是沒有新行為可回滾。未來 capability 的 rollback owner
必須是 deployment-owned false-by-default gate；gate=false 在 configuration bind 後、任一
profile/client/handler/pool/CE I/O 之前結束 typed path。legacy 行為是否在未來退場，不是
此 child 或 P7.4 local boundary 可以自行決定。

## 安全與效能取捨

不把密碼或 hash 複製到 DTO 可減少 retained-data、logging、cache 和 cross-session exposure。
固定 outcome 與 fail-closed classification 會放棄細節診斷，但可避免帳號存在性、secret state
和上游錯誤成為 oracle。單一 executor owner、bounded cancellation 和 fault eviction 維持既有
Generation／Profile isolation，避免為了驗證而建立無界重試或每 request 長壽 client。
