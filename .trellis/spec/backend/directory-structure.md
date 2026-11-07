# Directory Structure

> How backend code is organized in this project.

---

## Overview

<!--
Document your project's backend directory structure here.

Questions to answer:
- How are modules/packages organized?
- Where does business logic live?
- Where are API endpoints defined?
- How are utilities and helpers organized?
-->

(To be filled by the team)

---

## Directory Layout

```
<!-- Replace with your actual structure -->
src/
├── ...
└── ...
```

---

## Module Organization

<!-- How should new features/modules be organized? -->

(To be filled by the team)

---

## Naming Conventions

<!-- File and folder naming rules -->

(To be filled by the team)

---

## Examples

<!-- Link to well-organized modules as examples -->

(To be filled by the team)

---

## 新增專案與方案同步契約（2026-11-07 使用者核定）

### 1. 適用範圍

本工作樹未來新增的產品、共用程式庫、測試與工具 `.csproj`，建立骨架時即加入
`SpeechMessageProducts.sln`。不得等功能、測試或整合完成才登錄。特殊方案邊界
若與此要求衝突，先說明衝突，不能自行視為例外。

### 2. 操作入口

```powershell
dotnet sln .\SpeechMessageProducts.sln add .\SpeechMessage.Hosting\SpeechMessage.Hosting.csproj .\SpeechMessage.Hosting.Tests\SpeechMessage.Hosting.Tests.csproj
dotnet sln .\SpeechMessageProducts.sln list
```

### 3. 不變條件

每個新專案在正確工作樹的方案中恰好一筆；保留原專案身分與設定，補齊
Debug／Release 的 ActiveCfg 與 Build.0。不把「加入方案」當成建置、測試或
實機驗收通過；工作包完成狀態仍由實際驗收證據決定。

### 4. 失敗處理

檔案存在但 list 不含專案即為整合缺口；路徑不存在或 CLI 非零結束碼時修正
路徑／專案格式後重新加入。若出現重複項目、原專案遺失或設定映射缺漏，
停止該次整合並修復，不能只靠重開 Visual Studio 掩蓋磁碟上的問題。

### 5. 正反案例

正例：Hosting 與 Hosting.Tests 建立後一併加入方案，再繼續尚未完成的功能。
基本情境：重跑新增命令後仍各一筆，原有專案保持不變。
反例：只以專案路徑跑測試，方案卻維持舊清單，讓使用者誤以為專案不存在。

### 6. 驗證與證據

加入前後比對專案名稱、相對路徑與 GUID；斷言原集合完整保留、新項目各一筆，
並核對建置設定。分開保存相關專案建置與測試結果、UTF-8 無 BOM／CRLF
檢查及進度頁狀態，不能以方案可見支持整個工作包完成。

### 7. 錯誤與正確順序

錯誤：建立 csproj → 開發多個回合 → 最後才加入 sln。

正確：建立 csproj → 同步加入 sln 並核對清單／映射 → 開發與驗證。
