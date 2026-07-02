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
