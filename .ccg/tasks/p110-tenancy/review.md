# P1-10 主會話審查

審查者：本次實作主會話。依使用者最新覆寫，沒有啟動 Gemini／Claude，
也沒有獨立第二人審查；不能將本記錄說成雙模型或獨立審查完成。

## 範圍及結論

正式 02 P1-10、主 P1 Task 8 與永久安全／生命週期／編碼要求逐項核對。
本輪只新增 net10 Tenancy 地基及既有解析入口遷移，沒有更改正式計畫，
沒有恢復雙模型、移除測試或增添 NoWarn。正式 Changed 最終 1047 檔零問題，文件與狀態同步後重驗通過。

## 已修正發現

| 發現 | 原因與修正 | 決定性證據 |
| --- | --- | --- |
| environment 別名誤讀環境 ID | 不能用字串 sentinel 代表來源；獨立 usesEnvironment 判定 | boundary-red 兩案失敗，修後同來源 token／ID 通過 |
| 明確空時區被發布 | 缺值後備與明確空白須分開；空白直接拒絕 | boundary-red 兩案失敗，修後可拒絕並修正重試 |
| DI 入口忽略注入 resolver | 產品 helper 實際委派借入介面，不回靜態來源 | injected-owner-red 失敗，整合後通過 |
| Lazy 設定求值超出 catch | 移除舊 catch 後參數先求值，原設定根失敗直接逃逸 | 首輪全方案兩案失敗；factory-red 真紅；修後主機與 consumer 通過 |
| 進度摘要測試寫死舊計數 | 改成固定合成四完成／六 todo／兩人工操作，維持精確斷言 | 先 13／14，修後 14／14 |
| Debug 命令選錯專案而零案 | 正式 TRX 守門拒絕零執行；改回 workflow 指定專案 | debug-zero-rejection、debug-final executed=7 passed=7 |

## 正確性、安全與資源

- 十五個實際消費者全部委派唯一來源選擇器，保留四種舊政策與預設 null
  合併差異；Development validator 是安全驗證而非來源解析，保持原樣。
- TenantId 只驗格式，不賦予權限；固定產品 owner 明確拒絕外產品。
  設定鍵完整含 organization／product／church，沒有任意 request 路由入口。
- 沒有增加結果快取、global user state、provider、watcher、queue、timer 或 Task。
  靜態 Warning delegate 及 fallback logger 無租戶狀態；所有候選只活本次呼叫。
- Core credential 不複製型別；token 不在 ToString／預設 JSON，標頭注入、
  長度及錯誤來源拒絕；不輸出配置值或 Exception.Message。
- DI 使用外部 IConfiguration instance，不接管 owner；真實容器測試證明
  容器停止仍可用、原 owner 最終恰釋放一次。
- resolver 與 settings 各 A／B 千次並行且排空；百次結果／scope／credential／locale
  弱參照回收通過。真正來源故障保留原例外身份並可重試。
- 靜態產品 failure boundary 交共用例外入口；真實日誌證明原例外去重與
  敏感本文排除。告警 sender 不再通知自身錯誤，原 HttpClient drain／Dispose 不變。
- LineUtility client 的完整替換、舊 owner 釋放與外部注入不處置契約仍有實測。

## 驗證與界線

完整方案 17 TRX、4060／4060、0 失敗、0 略過；Tenancy 43、
獨立 coverage 98.71%、strict Rebuild 0 警告／0 錯誤。
I18n 108、PE 27、CI 閉包 6、進度頁 14、Debug 7、
Plugin metadata 10／build 6，套件／重複守門通過。
首輪全方案建置為既有測試警告 23、0 錯誤；修後方案增量建置為 0／0，
不將增量結果解讀為原警告已修。

ChannelId 只驗設定結果，既有缺值如實 null，未取得 LINE 實機頻道證據。
P1-06 的時區映射／DST、P1-07 金額白名單、P1-08 覆寫快取及 D365 連線
仍由各包驗收。未跑 hosted CI、未連 VM／D365／LINE、未 push 或部署。
無已知 P1-10 阻斷性缺陷；主 P1 尚未完成。
