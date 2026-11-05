# P1-04 抽取前風險分析報告

根據任務指示與已核定邊界，本報告針對 **P1-04 抽取前風險分析** 進行結構化評估。本次分析基於去識別化摘要資訊，聚焦於純資料核心（ToolUtility.Core）架構、相容性邊界、測試覆蓋與故障防護。

---

## 一、 核心風險與分類評估 (Critical / Warning / Info)

### 【Critical】高風險 / 關鍵阻礙
1. **FetchXML 直接字串插入與安全性漏洞**
   - **現況**：FeeService 的 FetchXML 查詢使用直接字串拼接（包含 `< & " '` 等特殊字元），且部分舊方法直接插入輸入值。
   - **風險**：若在搬移至 `ToolUtility.Core` 時未對 GUID、訂單編號等參數進行嚴格防護或改用結構化篩選，將導致 XML 注入或查詢崩潰。
   - **對策**：搬移前必須針對特製字元（`< & " '`）撰寫特性測試（Characterization Tests），並在重構時改以參數化或安全的 XML 組合方式處理，拒絕無條件照抄舊有不安全實作。

2. **跨教會 Static 狀態與服務生命週期污染**
   - **現況**：多個消費集合可能並存於同一 host 中。
   - **風險**：借入之資料服務（`IOrganizationService` 等）嚴禁被 `Dispose` 或建立跨教會共用的 `static` 狀態，否則會導致記憶體流失或資料交叉汙染。
   - **對策**：實施 DI 多載隔離連線鍵，並在測試中明確驗證「借入服務絕不被釋放（Not Disposed）」及「A/B 並行無狀態汙染」。

3. **空實作與業務契約驗證**
   - **現況**：部分 Public 方法目前為空實作，若直接當成完成的 Core 能力會引入未定義行為。
   - **風險**：未查明同能力的有效舊來源及消費者前，盲目補上空實作或猜測業務，將破壞既有查詢契約。
   - **對策**：先查明有效舊來源，證明既有查詢契約後再修正。

---

### 【Warning】中風險 / 警告
1. **相依性汙染與 Logger / Cache 殘留**
   - **現況**：`Operations` 多數接 object logger，部分帶有反射、`LoggerInvoker` 或 host cache 介面；`ContactService` 使用 host cache；`ComplexQueryService` 使用 `ICrmClient`；`QueryService` 使用系統時間與 Trace 原值。
   - **風險**：若直接複製 using 或將 host 特有依賴（如 LINE、檔案、具體 cache 實作）帶入 Core，將違反「Core 不帶 LINE／檔案依賴」與「不分區 cache」的核定邊界。
   - **對策**：嚴格過濾相依性，Core 僅保留純資料操作；錯誤必須傳遞真實 Exception，由 host 既有共用 notifier 負責通知，絕不吞例外假成功。

2. **重複穩定 ID 與去重邏輯**
   - **現況**：每個消費集合需保留不同 ID 的同名資料。
   - **風險**：若錯誤地以「名稱」去重，會導致不同集合的同名資料遺失。
   - **對策**：強制以「重複穩定 ID 拒絕發布」為唯一去重與驗證標準。

---

### 【Info】資訊 / 提示
1. **多標靶專案結構與 SDK 設定**
   - `ToolUtility` 為 net10 host；新增 `ToolUtility.Core` 支援 `net462;net10.0`，原命名空間不變。
   - Framework SDK 使用 `CoreAssemblies`，net10 SDK 使用 `Dataverse.Client`，均設為 `PrivateAssets=all`。新專案採用 strict 模式且不加 `NoWarn`。
2. **DI 多載與向後相容**
   - 新增 product 多載用於連線鍵隔離，舊多載標記 `[Obsolete]` 並委派至 `ChurchReport`。
   - Host 相容 adapter 只能轉接純介面，不能反向引用 host。

---

## 二、 測試與抽取順序規劃

為確保搬移過程萬無一失，應嚴格執行以下順序：

```
[階段 1：鎖定現狀與特性測試]
  │
  ├─> 1. 鎖定目前有實作 Fee 查詢之黃金資料 (Golden Data)、回傳與失敗傳遞
  ├─> 2. 針對三個新 Fee 方法（GetPaidFee / GetNotYetPaidFee / RetrieveFeeByFetchXmlOrderNumber）各補至少 3 例測試
  └─> 3. 涵蓋訂單特殊字元 (< & " ') 與 GUID 格式明確驗證測試
  
[階段 2：防禦與並行測試驗證]
  │
  ├─> 1. 撰寫「相同名稱不同 ID」與「重複 ID 拒絕發布」測試
  ├─> 2. 撰寫 A/B 並行執行測試
  └─> 3. 撰寫「借入服務不被釋放 (Not Disposed)」驗證測試
  
[階段 3：在原服務上執行綠特性測試]
  │
  └─> 確保修改前所有行為均有綠燈測試支撐
  
[階段 4：安全抽取與 Git 搬移]
  │
  ├─> 1. 建立 ToolUtility.Core (net462;net10.0)，套用 strict 規則
  ├─> 2. 執行 `git mv` 搬移純資料操作（隔離 ServiceClient、Extensions、HTTP、cache、logger、連線）
  └─> 3. 調整 DI 多載，舊多載標記 Obsolete 委派 ChurchReport
```

---

## 三、 關鍵決策與 Trade-offs

1. **拒絕照抄壞行為 vs. 維持相容性**
   - *Trade-off*：舊有直接字串插入 FetchXML 的實作雖然能動，但存在安全隱患。我們選擇**不直接照抄**，而是在特性測試鎖定其商業輸出契約的同時，在 Core 內部改用安全建構方式，確保行為相容但安全性升級。
2. **純資料核心的隔離性 vs. 靈活性**
   - *Trade-off*：捨棄 host 的 cache、logger 與具體通知實作，改由 Core 拋出真實 Exception、由 Host Notifier 統一處理。這會增加少許配接代價，但能確保 Core 成為高度可重用、無副作用的純資料核心。
3. **穩定 ID 識別 vs. 名稱去重**
   - *Trade-off*：放棄直覺的名稱去重，改以穩定 ID 進行嚴格校驗。雖然可能需要多寫幾行識別碼比對邏輯，但能完美支援「不同消費集合可存在同名但不同 ID 資料」的業務需求。

---

## 四、 結論與建議方案

- **首選方案**：嚴格遵循「**先測後搬、嚴格隔離、拒絕壞行為傳遞**」的策略。在 `ToolUtility.Core` 建立前，先補齊帶有特殊字元與 GUID 驗證的特性測試；搬移時排除所有非純資料相依性（Logger、Cache、HTTP、Dispose 呼叫），並確保錯誤如實向上傳遞。
- **需修正式計畫之情況**：若在撰寫特性測試時發現某個舊 Public 實作（例如 FeeService 的字串插入查詢）高度依賴 host 端的隱式環境變數或不可抽離的 cache，則不能當作一般實作細節處理，必須提早將其重構為介面化參數輸入，再行 `git mv`。
