# 階段 5 前置缺項清單（2026-11-14 08:10 Asia/Taipei (UTC+08:00)）

| 項目 | 給哪一包 | 狀態 | 需要誰做什麼 |
|---|---|---|---|
| D365-9.1 MSCRMSandboxService、MSCRMAsyncService 重啟與 iisreset | 更新回復前置 | 不可用／未在本機找到服務；目前執行者非系統管理員 | 依 D5-1 先做 P5-02、P5-03；更新回復前置需要實機時，再由具備 D365 主機管理權限的環境執行，不在此工作樹猜測或繞過權限。 |
| SPEECHMESSAGE_PLUGIN_KEYFILE | 更新回復前置、所有打包 | 已有；環境變數存在且檔案存在 | 不需使用者處理。 |
| verify --org goodshepherdback | 所有實機 | 未通過／本機執行檔或連線前置回報 DirectoryNotFoundException，未宣稱 verify=0 | 在部署工具建置與連線前置可用後重試；目前不部署模組。 |
| export-metadata --org goodshepherdback | 所有模組盤點 | 可用；唯讀結構快照已建立於 d365\snapshots\goodshepherdback\ | 不需使用者處理。 |
| Deployment:Organizations:goodshepherdback:TokenKey、TestChannelToken、TestChannelSecret、TestLineUserId | P5-04 起 | 已存在；只核對鍵名，不輸出值 | 不需使用者處理。 |
| ShadowTestChurchId、ShadowTestContactId | 所有影子情境 | 缺少 | 若離線工作完成後仍需實機影子情境，需在 Deployment User Secrets 補上測試教會與會友 GUID；目前先不問，繼續不依賴實機的工作。 |
| D34 清理與測試教會／會友資料 | 所有實機 | 未核對；因測試 GUID 尚未取得，不讀取或猜測業務資料 | 取得測試 GUID 後依既有 P2-04 前置唯讀核對。 |
| 外掛追蹤設定 | 更新回復前置、shadow | 未在本機 D365 服務環境核對 | 實機前由部署工具／D365 主機唯讀核對；不得把本機設定當沙箱證據。 |
| 教會代碼 alias 規則 | P5-04 | 尚未核對 | P5-04 盤點時只輸出符合／不符合筆數與不符合教會 ID，不輸出秘密或個資。 |
| 舊方案 Dynamics365Line | P5-02 | 可讀；位於工作樹外的 .其他解決方案\Dynamics365Line | 僅讀第一層 36 個專案，不讀其 .worktrees、backup_*、bin、obj、packages 原始檔。 |
| C:\smv | 守門 | 可用；目前只有 guard | 不需使用者處理。 |

- 缺項：4 項（D365 重啟權限、verify、兩個影子測試 GUID、D34 清理核對）。
- 等待期間 Codex 先做的離線工作：P4 H-1 修補、P5-01、P5-02、P5-03。
- 本清單只記鍵名、主機／路徑與狀態，不記任何秘密值。

## 2026-11-14 12:54 Asia/Taipei (UTC+08:00) 更新回復前置後狀態
上表08:10是歷史盤點。現況verify已0，D34測試綁定1／其他0，trace還原1、自訂步驟19全停用、active自訂流程定義0；FileVersion3→4→3不需重啟，原主機權限缺項未阻擋本輪。兩個Shadow GUID仍未設定，P5-03實機前由唯一測試綁定及權威parentcustomerid核對後處理，不猜GUID。
