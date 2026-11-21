# P8-L1 設計

## 邊界

L1 僅改部署工具診斷輸出與對應測試，不變更 plugin 行為、既有數字原因碼、註冊清冊分級或組織資料。Live preparation 必須逐模組保存結果：活動拒絕不能短路同模組步驟的檢查。

## 診斷契約

- safety failure 只回傳固定子檢查 ID，禁止回傳被檢查設定的實際值。
- Dataverse fault 摘要包含穩定 fault code、操作階段及實體邏輯名稱；不包含組織 URL、record 欄位值或錯誤訊息本文。
- church-defaults apply fault 只加目標列 `statecode` 與四個目標欄位各自的 `IsValidForUpdate`，不讀出或輸出其值。
- relay precheck 使用既有固定原因碼，不以自由文字取代。
- trace exception 以 exception type 與 Plugin type 分組計數，不序列化 message、stack、trace detail 或個資。

## 驗證策略

每項診斷先加同名合成測試並確認舊行為失敗，再實作最小欄位投影使測試通過。部署測試採離線 fake／fixture；Live 調查使用專案已存在的只讀 `observe`、`plan` 或 `live-check` 子檢查輸出，絕不新增臨時 probe。

## 唯讀調查

以既有 P7 證據作調查起點，每項最多 30 分鐘。R7 依新分組資訊判斷兩筆例外是否可分類；R9 查現存 trace retention／清理記錄，沒有可驗證去向則結案為無法確認；church-defaults 以修正後唯讀診斷確認錯誤欄位、statecode 與 `IsValidForUpdate`，到達資訊邊界即停止。所有連線設定只由 User Secrets／環境變數提供，不列印、不複製到檔案或命令列。
