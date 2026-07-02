# LINE Messaging API Official Matrix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a complete official LINE Messaging API comparison matrix that identifies SDK correctness, missing coverage, unsafe code, wrong hosts, wrong endpoints, partial models, and implementation priorities.

**Architecture:** This plan creates one authoritative Markdown matrix document under `Line.Messaging/文件/` and does not change SDK source code. The matrix is built from the official LINE Messaging API reference, then cross-checked against `ILineMessagingClient`, `LineMessagingClient`, webhook models, message models, action models, and object models.

**Tech Stack:** .NET 10 solution, C# SDK source files, Markdown documentation, PowerShell inspection commands, official LINE Messaging API reference.

---

## Scope And Guardrails

This plan implements the approved design spec:

- `docs/superpowers/specs/2026-07-02-line-messaging-api-official-matrix-design.md`

This plan must create or modify only this delivery document:

- `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`

This plan must not modify these source areas:

- `Line.Messaging/*.cs`
- `Line.Messaging/**/*.cs`
- `LineMessagingProcessor/*.cs`
- `ChurchReport/**/*.cs`

If a source-code defect is found, record it in the matrix. Do not fix it in this plan.

Official reference:

- `https://developers.line.biz/en/reference/messaging-api/`

## File Structure

### Create

- `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`
  - Owns the official Messaging API comparison matrix.
  - Contains fixed status values and priority values from the design spec.
  - Contains one section per official category.
  - Contains row-level evidence for SDK mapping and code location.

### Read Only

- `docs/superpowers/specs/2026-07-02-line-messaging-api-official-matrix-design.md`
  - Source of scope, status values, priority values, and known initial risks.

- `Line.Messaging/ILineMessagingClient.cs`
  - Source of public SDK interface method coverage.

- `Line.Messaging/LineMessagingClient.cs`
  - Source of concrete endpoint, host, method, payload, and `NotImplementedException` evidence.

- `Line.Messaging/Webhooks/*.cs`
  - Source of webhook event, message event, source, parser, and signature coverage.

- `Line.Messaging/Messages/**/*.cs`
  - Source of message object, action object, flex, template, quick reply, rich menu object coverage.

- `Line.Messaging/LineObjects/*.cs`
  - Source of response/request object coverage for quota, insights, audience, coupon, membership, webhook endpoint, and bot info.

- `LineMessagingProcessor/LineMessagingProcessorClass.cs`
  - Source of product-layer overlap and hardcoded token evidence.

---

### Task 1: Create The Matrix Document Skeleton

**Files:**
- Create: `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`
- Read: `docs/superpowers/specs/2026-07-02-line-messaging-api-official-matrix-design.md`

- [ ] **Step 1: Read the approved design spec**

Run:

```powershell
Get-Content -LiteralPath "docs\superpowers\specs\2026-07-02-line-messaging-api-official-matrix-design.md" -Encoding UTF8
```

Expected:

```text
The file opens and contains the approved matrix columns, status values, priority values, and twelve official categories.
```

- [ ] **Step 2: Create the matrix file with the fixed status and priority definitions**

Create `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md` with this exact opening structure:

```markdown
# LINE Messaging API 官方對照矩陣

## 1. 文件目的

本文件以 LINE Messaging API 官方文件為唯一基準，逐項比對目前 `Line.Messaging` SDK 的支援狀態。這份矩陣只做審查與分類，不修正 SDK 程式碼。

官方基準來源：

- https://developers.line.biz/en/reference/messaging-api/

## 2. 狀態值

| 狀態 | 定義 |
| --- | --- |
| `Correct` | SDK 已對應官方規格，host、path、method、payload、response model 沒有已知問題。 |
| `WrongEndpoint` | SDK 方法或類別存在，但 endpoint path 與官方規格不符。 |
| `WrongHost` | SDK 方法或類別存在，但 host 與官方規格不符。 |
| `Missing` | 官方項目存在，但 SDK 沒有對應方法、類別或 enum。 |
| `Partial` | SDK 有部分支援，但 payload、response、欄位、enum 或例外處理不完整。 |
| `NotImplemented` | 介面或方法宣稱存在，但實作仍拋出 `NotImplementedException` 或等同未完成。 |
| `Obsolete` | SDK 使用舊版官方規格、舊 endpoint、舊欄位或過時語意。 |
| `Unsafe` | 存在安全風險，例如硬編碼 Channel Access Token、錯誤 signature 驗證、未保護 secret。 |
| `NeedsOfficialVerification` | 初步看起來可疑，但必須再查官方文件細節才能判斷。 |

## 3. 優先級

| 優先級 | 定義 |
| --- | --- |
| `P0` | 安全風險或目前會打錯 LINE API 的問題。 |
| `P1` | SDK 宣稱支援但實際不完整或未實作。 |
| `P2` | 官方功能缺漏，但不影響最基本傳訊、Webhook、Profile 等核心流程。 |
| `P3` | 進階、方案限制、低使用頻率或可延後實作的官方功能。 |

## 4. 矩陣欄位

| 欄位 | 說明 |
| --- | --- |
| 官方分類 | 官方文件分類。 |
| 官方 endpoint / object | 官方 endpoint path 或 object 名稱。 |
| HTTP method | endpoint 使用的 HTTP method；非 endpoint 類項目填 `N/A`。 |
| host | 官方要求 host；非 endpoint 類項目填 `N/A`。 |
| 官方用途 | 官方功能用途摘要。 |
| 目前 SDK 對應方法/類別 | 目前 SDK 中對應的方法、類別或 enum。 |
| 目前狀態 | 固定狀態值。 |
| 問題類型 | host 錯誤、endpoint 錯誤、缺類別、欄位不完整、安全風險等。 |
| 風險等級 | `P0`、`P1`、`P2`、`P3`。 |
| 建議修正 | 下一階段 SDK 修正方向。 |

## 5. 官方對照矩陣
```

- [ ] **Step 3: Add the twelve matrix section headings**

Append these headings under `## 5. 官方對照矩陣`:

```markdown
### 5.1 Client 基礎與安全

### 5.2 Message API

### 5.3 Content API

### 5.4 User / Bot / Group / Room

### 5.5 Webhook

### 5.6 Message Objects

### 5.7 Action Objects

### 5.8 Rich Menu

### 5.9 Audience / Narrowcast Conditions

### 5.10 Insights / Statistics

### 5.11 Coupon / Membership

### 5.12 OAuth / Token
```

- [ ] **Step 4: Commit the skeleton**

Run:

```powershell
git add -- "Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md"
git commit -m "docs: 建立 LINE Messaging API 官方對照矩陣骨架"
```

Expected:

```text
Commit succeeds and only the matrix file is included.
```

---

### Task 2: Build The SDK Evidence Index

**Files:**
- Modify: `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`
- Read: `Line.Messaging/ILineMessagingClient.cs`
- Read: `Line.Messaging/LineMessagingClient.cs`
- Read: `Line.Messaging/Webhooks/*.cs`
- Read: `Line.Messaging/Messages/**/*.cs`
- Read: `Line.Messaging/LineObjects/*.cs`
- Read: `LineMessagingProcessor/LineMessagingProcessorClass.cs`

- [ ] **Step 1: List public interface methods**

Run:

```powershell
Select-String -Path "Line.Messaging\ILineMessagingClient.cs" -Pattern "Task<|Task " |
    Select-Object LineNumber,Line
```

Expected:

```text
The output lists every public SDK interface method and line number.
```

- [ ] **Step 2: List concrete endpoint calls**

Run:

```powershell
Select-String -Path "Line.Messaging\LineMessagingClient.cs" -Pattern "_uri|api-data|api.line.me|HttpMethod|GetAsync|PostAsync|Put|DeleteAsync|NotImplementedException" |
    Select-Object LineNumber,Line
```

Expected:

```text
The output lists endpoint construction, HTTP verbs, host usage, and any NotImplementedException.
```

- [ ] **Step 3: List webhook event coverage**

Run:

```powershell
Select-String -Path "Line.Messaging\Webhooks\*.cs" -Pattern "class |enum |webhookEventId|deliveryContext|mode|replyToken|destination|unsend|video|membership|markAsReadToken" |
    Select-Object Path,LineNumber,Line
```

Expected:

```text
The output shows which webhook classes and fields exist, and highlights missing modern fields when no matching lines appear.
```

- [ ] **Step 4: List message and action object coverage**

Run:

```powershell
Select-String -Path "Line.Messaging\Messages\*.cs","Line.Messaging\Messages\**\*.cs" -Pattern "class |enum |TextMessage|FlexMessage|QuickReply|Mention|Emoji|quoteToken|sender|Clipboard|RichMenuSwitch|Datetime|Camera|CameraRoll|Location" |
    Select-Object Path,LineNumber,Line
```

Expected:

```text
The output shows message object and action object coverage with file paths and line numbers.
```

- [ ] **Step 5: List object model coverage**

Run:

```powershell
Select-String -Path "Line.Messaging\LineObjects\*.cs" -Pattern "class |enum |Audience|Coupon|Membership|Insight|Quota|Webhook|BotInfo|Token|Statistics|Aggregation" |
    Select-Object Path,LineNumber,Line
```

Expected:

```text
The output shows request and response model coverage for official API families.
```

- [ ] **Step 6: Record the evidence commands in the matrix**

Add this section after `## 4. 矩陣欄位`:

```markdown
## 4.1 SDK 證據來源

本矩陣使用以下檔案作為 SDK 現況證據：

- `Line.Messaging/ILineMessagingClient.cs`
- `Line.Messaging/LineMessagingClient.cs`
- `Line.Messaging/Webhooks/*.cs`
- `Line.Messaging/Messages/**/*.cs`
- `Line.Messaging/LineObjects/*.cs`
- `LineMessagingProcessor/LineMessagingProcessorClass.cs`

每個 `WrongEndpoint`、`WrongHost`、`Unsafe`、`NotImplemented`、`Partial` 項目都必須在建議修正欄位附上可追溯的程式位置。
```

- [ ] **Step 7: Commit the evidence-index section**

Run:

```powershell
git add -- "Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md"
git commit -m "docs: 補充 LINE SDK 對照矩陣證據來源"
```

Expected:

```text
Commit succeeds and only the matrix file is included.
```

---

### Task 3: Fill P0 Known-Risk Rows

**Files:**
- Modify: `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`
- Read: `Line.Messaging/LineMessagingClient.cs`
- Read: `LineMessagingProcessor/LineMessagingProcessorClass.cs`

- [ ] **Step 1: Add the Client 基礎與安全 table**

Under `### 5.1 Client 基礎與安全`, add:

```markdown
| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Client 基礎與安全 | Channel Access Token storage | N/A | N/A | SDK 不應內建產品 token 或 secret。 | `LineMessagingProcessor/LineMessagingProcessorClass.cs` | `Unsafe` | 程式含硬編碼 Channel Access Token。 | `P0` | 移除硬編碼 token；改由呼叫端透過設定或 DI 傳入 token；`LineMessagingProcessor` 不應保留產品密鑰。 |
| Client 基礎與安全 | Base API host | N/A | `api.line.me` | JSON API 使用 LINE API host。 | `Line.Messaging/LineMessagingClient.cs` `_uri` | `Partial` | 單一 `_uri` 同時承擔 JSON API 與 binary content API，容易造成 host 分流錯誤。 | `P0` | 下一階段拆分 `ApiBaseUri` 與 `ApiDataBaseUri`。 |
```

- [ ] **Step 2: Add the known wrong endpoint risk rows**

Still under `### 5.1 Client 基礎與安全`, append:

```markdown
| Client 基礎與安全 | Duplicate `/v2` path check | N/A | `api.line.me` | SDK endpoint path 不應重複 API version segment。 | `Line.Messaging/LineMessagingClient.cs` Insights、Coupon、Membership methods | `WrongEndpoint` | `_uri` 預設為 `https://api.line.me/v2`，但部分方法又接 `/v2/bot/...`，可能形成 `/v2/v2/bot/...`。 | `P0` | 下一階段將所有 endpoint 組合規則統一成 base URI 不重複版本；用測試鎖定 URL。 |
```

- [ ] **Step 3: Add the binary content host risk rows**

Under `### 5.3 Content API`, add:

```markdown
| 官方分類 | 官方 endpoint / object | HTTP method | host | 官方用途 | 目前 SDK 對應方法/類別 | 目前狀態 | 問題類型 | 風險等級 | 建議修正 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Content API | `/v2/bot/message/{messageId}/content` | GET | `api-data.line.me` | 下載使用者傳送的內容。 | `GetContentStreamAsync`, `GetContentBytesAsync` | `WrongHost` | 目前走 `_uri`，預設為 `https://api.line.me/v2`。 | `P0` | 下一階段改走 `api-data.line.me` base URI，並新增 URL 組合測試。 |
| Content API | `/v2/bot/message/{messageId}/content/preview` | GET | `api-data.line.me` | 下載圖片或影片預覽。 | `GetContentPreviewAsync` | `WrongHost` | 目前走 `_uri`，預設為 `https://api.line.me/v2`。 | `P0` | 下一階段改走 `api-data.line.me` base URI，並新增 URL 組合測試。 |
```

- [ ] **Step 4: Commit P0 known-risk rows**

Run:

```powershell
git add -- "Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md"
git commit -m "docs: 標註 LINE SDK P0 已知風險"
```

Expected:

```text
Commit succeeds and only the matrix file is included.
```

---

### Task 4: Fill Official Endpoint Matrix Rows

**Files:**
- Modify: `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`
- Read: `Line.Messaging/ILineMessagingClient.cs`
- Read: `Line.Messaging/LineMessagingClient.cs`

- [ ] **Step 1: Open the official Messaging API reference**

Open:

```text
https://developers.line.biz/en/reference/messaging-api/
```

Expected:

```text
The official LINE Messaging API reference is available for endpoint-by-endpoint reading.
```

- [ ] **Step 2: Fill Message API rows**

Under `### 5.2 Message API`, create a table with the standard ten columns and include rows for every official Message API endpoint in these groups:

```text
Reply message
Push message
Multicast message
Narrowcast message
Broadcast message
Validate message objects
Message quota
Number of sent messages
Narrowcast progress
Loading animation
Mark messages as read
```

For each row:

```text
If method exists and endpoint host/path/method/payload match official reference, mark Correct.
If method exists but path or host differs, mark WrongEndpoint or WrongHost.
If method exists but payload model is too loose or missing required official fields, mark Partial.
If no method exists, mark Missing.
If official behavior now differs from the SDK method semantics, mark Obsolete.
```

- [ ] **Step 3: Fill User / Bot / Group / Room rows**

Under `### 5.4 User / Bot / Group / Room`, create rows for:

```text
Get profile
Get bot info
Get group summary
Get group member profile
Get group member user IDs
Get number of users in a group
Leave group
Get room member profile
Get room member user IDs
Get number of users in a room
Leave room
```

Use the same status rules as Step 2.

- [ ] **Step 4: Fill Rich Menu rows**

Under `### 5.8 Rich Menu`, create rows for:

```text
Create rich menu
Validate rich menu object
Upload rich menu image
Download rich menu image
Get rich menu list
Get rich menu
Delete rich menu
Set default rich menu
Get default rich menu ID
Cancel default rich menu
Link rich menu to user
Link rich menu to users
Get rich menu ID of user
Unlink rich menu from user
Unlink rich menu from users
Batch control rich menus
Get rich menu batch progress
Validate rich menu batch request
Create rich menu alias
Delete rich menu alias
Update rich menu alias
Get rich menu alias
Get rich menu alias list
```

For upload and download image rows, explicitly verify host against the official reference and mark `WrongHost` if the SDK uses the JSON API base host for binary content.

- [ ] **Step 5: Fill Insights / Statistics rows**

Under `### 5.10 Insights / Statistics`, create rows for:

```text
Get number of message deliveries
Get number of followers
Get friend demographics
Get user interaction statistics
Get statistics per unit
Get aggregation info
Get aggregation unit name list
```

Flag any `/v2/v2` path risk as `WrongEndpoint`.

- [ ] **Step 6: Fill Coupon / Membership rows**

Under `### 5.11 Coupon / Membership`, create rows for all coupon and membership endpoints listed in the official Messaging API reference.

Flag any `/v2/v2` path risk as `WrongEndpoint`.

- [ ] **Step 7: Fill Audience / Narrowcast Conditions rows**

Under `### 5.9 Audience / Narrowcast Conditions`, create rows for all official audience endpoints and narrowcast recipient/filter/limit object requirements.

For SDK methods in `LineMessagingClient.cs` that throw `NotImplementedException`, mark `NotImplemented` with `P1`.

- [ ] **Step 8: Fill OAuth / Token rows**

Under `### 5.12 OAuth / Token`, create rows for official token endpoints listed in the Messaging API reference:

```text
Issue channel access token
Revoke channel access token
Verify channel access token
Get all valid channel access token key IDs
Issue stateless channel access token
Revoke stateless channel access token
```

Mark missing v2.1 or stateless-token support according to the current SDK code.

- [ ] **Step 9: Commit endpoint rows**

Run:

```powershell
git add -- "Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md"
git commit -m "docs: 補齊 LINE 官方 endpoint 對照矩陣"
```

Expected:

```text
Commit succeeds and only the matrix file is included.
```

---

### Task 5: Fill Official Object And Webhook Matrix Rows

**Files:**
- Modify: `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`
- Read: `Line.Messaging/Webhooks/*.cs`
- Read: `Line.Messaging/Messages/**/*.cs`
- Read: `Line.Messaging/LineObjects/*.cs`

- [ ] **Step 1: Fill Webhook rows**

Under `### 5.5 Webhook`, create rows for official webhook request-level fields and event families:

```text
destination
events
webhookEventId
deliveryContext
mode
replyToken
source
timestamp
message event
follow event
unfollow event
join event
leave event
member joined event
member left event
postback event
beacon event
account link event
things event
unsend event
video viewing complete event
membership event
```

Mark missing event families or fields as `Missing` or `Partial` with `P1`.

- [ ] **Step 2: Fill Message Objects rows**

Under `### 5.6 Message Objects`, create rows for official message object families:

```text
Text message
Text message v2
Sticker message
Image message
Video message
Audio message
Location message
Imagemap message
Template message
Flex message
Quick reply
Sender
Mention
Emoji
Quote token
File message event object
```

Mark SDK support as `Correct`, `Partial`, or `Missing` based on actual C# classes and fields.

- [ ] **Step 3: Fill Action Objects rows**

Under `### 5.7 Action Objects`, create rows for official action object families:

```text
Postback action
Message action
URI action
Datetime picker action
Camera action
Camera roll action
Location action
Rich menu switch action
Clipboard action
```

Mark SDK support based on actual classes under `Line.Messaging/Messages/Action/`.

- [ ] **Step 4: Add a summary section**

Append this exact summary section near the end of the matrix:

```markdown
## 6. 問題摘要

| 優先級 | 數量 | 主要問題 |
| --- | ---: | --- |
| `P0` | 0 | 完成矩陣後更新實際數量。 |
| `P1` | 0 | 完成矩陣後更新實際數量。 |
| `P2` | 0 | 完成矩陣後更新實際數量。 |
| `P3` | 0 | 完成矩陣後更新實際數量。 |

## 7. 下一階段建議

矩陣完成後，下一階段應先修正所有 `P0`，再處理 `P1`。`P2` 與 `P3` 應依官方功能重要性與產品需求排程，不應混入 P0/P1 修正提交。
```

- [ ] **Step 5: Replace summary counts with actual counts**

Count rows by priority manually from the completed matrix and replace the four `0` values with actual counts.

Expected:

```text
The summary counts match the visible matrix rows.
```

- [ ] **Step 6: Commit object and webhook rows**

Run:

```powershell
git add -- "Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md"
git commit -m "docs: 補齊 LINE webhook 與物件對照矩陣"
```

Expected:

```text
Commit succeeds and only the matrix file is included.
```

---

### Task 6: Verify Matrix Quality And Scope

**Files:**
- Modify: `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`

- [ ] **Step 1: Check that only the matrix document changed since this plan began execution**

Run:

```powershell
git status --short --untracked-files=all
```

Expected:

```text
No source code files are modified.
Only planned documentation changes appear before each commit.
```

- [ ] **Step 2: Check for forbidden ambiguous tokens**

Run:

```powershell
$tokens = @("TB"+"D", "TO"+"DO", "待"+"補", "之後"+"再補", "隨後"+"補上", "不確定"+"先略", "未"+"整理")
Select-String -LiteralPath "Line.Messaging\文件\LINE_Messaging_API_官方對照矩陣.md" -Pattern ($tokens -join "|")
```

Expected:

```text
No matches.
```

- [ ] **Step 3: Check for rows without fixed status values**

Run:

```powershell
$allowed = @("Correct","WrongEndpoint","WrongHost","Missing","Partial","NotImplemented","Obsolete","Unsafe","NeedsOfficialVerification")
$rows = Get-Content -LiteralPath "Line.Messaging\文件\LINE_Messaging_API_官方對照矩陣.md" -Encoding UTF8 | Where-Object { $_ -match '^\| ' -and $_ -notmatch '^\| ---' -and $_ -notmatch '^\| 欄位' -and $_ -notmatch '^\| 狀態' -and $_ -notmatch '^\| 優先級' -and $_ -notmatch '^\| 官方分類' }
$badRows = foreach ($row in $rows) {
    $hasStatus = $false
    foreach ($status in $allowed) {
        if ($row -like "*``$status``*") { $hasStatus = $true }
    }
    if (-not $hasStatus -and $row -match '^\| (Client|Message|Content|User|Webhook|Rich|Audience|Insights|Coupon|OAuth|Action|Bot|Group|Room)') { $row }
}
$badRows
```

Expected:

```text
No output.
```

- [ ] **Step 4: Check for rows without fixed priority values**

Run:

```powershell
$priorities = @("P0","P1","P2","P3")
$rows = Get-Content -LiteralPath "Line.Messaging\文件\LINE_Messaging_API_官方對照矩陣.md" -Encoding UTF8 | Where-Object { $_ -match '^\| ' -and $_ -notmatch '^\| ---' -and $_ -notmatch '^\| 欄位' -and $_ -notmatch '^\| 狀態' -and $_ -notmatch '^\| 優先級' -and $_ -notmatch '^\| 官方分類' }
$badRows = foreach ($row in $rows) {
    $hasPriority = $false
    foreach ($priority in $priorities) {
        if ($row -like "*``$priority``*") { $hasPriority = $true }
    }
    if (-not $hasPriority -and $row -match '^\| (Client|Message|Content|User|Webhook|Rich|Audience|Insights|Coupon|OAuth|Action|Bot|Group|Room)') { $row }
}
$badRows
```

Expected:

```text
No output.
```

- [ ] **Step 5: Run Markdown whitespace check**

Run:

```powershell
git diff --check
```

Expected:

```text
No output.
```

- [ ] **Step 6: Run baseline build to verify documentation work did not hide source changes**

Run:

```powershell
dotnet build ChurchReport.sln --no-restore
```

Expected:

```text
Build succeeds with 0 errors. Existing warnings are acceptable if unchanged from baseline.
```

- [ ] **Step 7: Final commit**

If Task 6 made any documentation corrections, run:

```powershell
git add -- "Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md"
git commit -m "docs: 完成 LINE Messaging API 官方對照矩陣驗證"
```

Expected:

```text
Commit succeeds if corrections were needed. If no corrections were needed, there is no final commit.
```

---

### Task 7: Prepare The SDK Fix Plan Input

**Files:**
- Modify: `Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md`

- [ ] **Step 1: Add a fix-plan input section**

Append:

```markdown
## 8. SDK 修正計畫輸入

下一份 SDK 修正 plan 應從本矩陣取出以下項目：

1. 所有 `P0` 項目。
2. 所有 `P1` 項目。
3. `P2` 項目中與現有公開 SDK API 相衝突者。
4. `NeedsOfficialVerification` 項目中經官方文件確認為錯誤者。

SDK 修正 plan 不應直接處理 `P3`，除非使用者明確要求完整覆蓋該進階功能。
```

- [ ] **Step 2: Commit the fix-plan input section**

Run:

```powershell
git add -- "Line.Messaging/文件/LINE_Messaging_API_官方對照矩陣.md"
git commit -m "docs: 補充 LINE SDK 修正計畫輸入規則"
```

Expected:

```text
Commit succeeds and only the matrix file is included.
```

- [ ] **Step 3: Report completion**

Report:

```text
The official matrix is complete, verified, and ready to drive the SDK correction plan.
Include the final commit hash and matrix file path in the report.
```
