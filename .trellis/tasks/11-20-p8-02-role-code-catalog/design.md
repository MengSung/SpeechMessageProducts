# P8-02 技術設計

## 邊界

角色代碼、選項對照與顯示名稱歸屬 `SpeechMessageProducts.GoodShepherd.Core/Members`；固定文字仍由 `GoodShepherdText.resx` 與既有 I18nCheck 產生流程管理。ChurchReport 僅在名單 API 及兩個 lookup 的資料流接入角色目錄與 `GoodShepherdTextCatalog`。此產品 catalog 必須讀取 GoodShepherd 資源表；不可直接以 Tenancy 擴充註冊的通用 `TextCatalog` 取代，因其預設系統表是共用 SharedText，找不到 `Role.*` 鍵。

P8-02 的 Host 註冊使用一個明確的空 `IChurchTextOverrideSnapshotProvider` 與 `GoodShepherdTextCatalog`。P8-03a 啟用實際覆寫時，必須先移除這兩個 P8-02 空 provider／`ITextCatalog` 註冊，再呼叫 `AddChurchTextOverrides` 註冊唯一覆寫 owner，最後以該 owner 提供的 `IChurchTextOverrideSnapshotProvider` 建立並註冊 `GoodShepherdTextCatalog`。不得先把空 provider 當成真來源交給 `AddChurchTextOverrides`，也不得讓空 catalog 與實際 catalog 同時存在。覆寫開關關閉時保留空 provider 與產品 catalog。

## 資料流與信任邊界

1. 唯讀 metadata 讀取只允許 goodshepherdback 的 `contact.customertypecode` 選項中繼資料；黃金檔只包含整數選項值及語系 1028 標籤，不查詢會友列、不收錄組織識別以外的敏感設定、不保存認證。
2. `RoleCatalog` 以 metadata 標籤做精確、可重現的對照。角色代碼為產品穩定識別；選項值若無匹配則空白。未知選項值的解析結果沒有代碼及授權語意，使用來源標籤原文顯示。
3. 固定字典透過現有 `ITextCatalog`／`TextContext` 解析；ChurchReport 的空覆寫 provider 明確避免在 P8-02 提前引入教會狀態。P8-03a 再替換為實際覆寫註冊。
4. 名單 API 從既有伺服器端 metadata 清單建立 `data` 與 `items`。`value` 保持 D365 標籤作為既有表單的提交值；`text` 只供呈現；`code` 僅供新功能作穩定識別。唯一性及代碼對照依選項整數，不以標籤作權限或角色身份。
5. API 的教會識別只從當前已驗證登入 contact 的 `parentcustomerid` 取得，絕不接受 request 傳入的教會 ID。若無法取得非空 account ID，仍可回傳既有 `data`，`items.text` 使用每筆 D365 原始標籤，不建立空 GUID TextContext、不嘗試教會覆寫。
6. ViewModel 在頁面產生時以同一伺服器端角色目錄建立 lookup 錯誤備援並安全序列化為 JSON。客戶端不得自行建立角色選項或用顯示名稱推導權限。Ajax 有效回應優先使用 `items`；`items` 缺失時只以既有 `data` 作相容顯示。

## 相容性與錯誤處理

- `data` 回應欄位及 D365 標籤寫入流程保持相容。
- 既有兩個查詢失敗 fallback 仍可用，但來源改為伺服器角色目錄；空 metadata、未對應選項或字典缺鍵不得用猜測值授權或寫入。
- 找不到 11 個指定標籤時保留角色代碼並輸出無敏感資訊的單元報告；其選項值為空，任何角色授權判斷均不得把空值或未知整數提升為角色。
- 角色顯示名稱由 `GoodShepherdTextCatalog` 根據本次已授權 TextContext 決定；目錄本身不保存 request、使用者、教會、憑證或可變清單。API 以 OptionSetMetadataService 取得空對照時，必須將空集合視為查詢失敗並走伺服器 fallback；不可把缺 metadata 誤報成成功的空清單。fallback 標籤仍只是相容顯示，不是 metadata 證據；存檔仍由 D365 標籤反查並可 fail closed。

## 資源與隔離

Core catalog 為由 Host 管理的無狀態固定表；不持有連線、計時器、背景工作或 request cache。ChurchReport 借用 DI 註冊的文字 catalog，不擁有或 Dispose 其覆寫 provider。每次 API 請求以伺服器授權的組織與文字範圍建立回應，不把一位使用者的角色資料放入跨使用者快取。metadata 讀取工作只建立一次離線黃金證據，沒有會友個資。

## 驗收

按 08A 指定的 6 個 Core 雙目標測試、3 個 ChurchReport 測試先紅後綠；接著執行 Core Release、ChurchReport.MemberInfo Release、I18nCheck generate/check 與 verify、文件／編碼守門及 P8 完整 guard。`RoleCatalog_OptionValueTable_MatchesMetadataSnapshot` 必須對照真正取得的黃金 metadata；不可用空黃金檔或自我生成期望掩飾未取得的實機證據。若 metadata 缺失，此必要條件保持未完成，記錄後續補驗，不宣稱 P8-02 驗收。
