# P0-10 執行順序與檔案歸屬

由主會話依既有 inline 模式逐項執行，不提前 P0-11。

1. 新增 SpeechMessage.Dynamics.Plugins 與 .Tests 的嚴格 net48 專案、空殼、九個固定測試；取得功能紅燈。
2. 新增 tools/PluginPackaging 的中繼資料讀取空殼、PowerShell 入口及兩個樣本測試；先紅後綠。
3. 最小實作 DiagnosticsPingPlugin；固定輸出、深度守門、雙語資源、同步等待 Core、例外追蹤後拋出；追加隔離／生命週期邊界測試。
4. build/PluginMerge.targets：明確四個輸入、內部化、正式簽章、Debug／Release 未設金鑰守門、版本序號。
5. Localization 的 resx 增加診斷設定錯誤與推播範本文字，由既有工具生成，不手改生成檔。
6. 兩個專案加入方案；舊金鑰只移除追蹤；測試、打包檢查、覆蓋率與完整 §8 全綠後回報。
7. 更新 task.json、3.3 進度 DATA、wp-reports/P0-10.md、證據；封存本 CCG 工作任務並獨立提交。

停止條件與使用者核定沿用 00 §9；不連外部組織、不推送、不合併、不改寫歷史。
