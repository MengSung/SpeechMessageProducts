# 實作計畫

1. 依使用者最新指示，P5-02a 只含三項安全措施與 PresentRecord 實跑至少三情境黃金；b 段為 36 專案／29 入口矩陣及假服務查詢補強，排除零來源方案殼。
2. 工作包內自行分析、判斷，不再呼叫 Start-CcgDualModelRun；外部審查僅於階段末一次執行。
3. 撤回六個逐情境合成小檔及伴隨說明；固定 scenarios.json、工具產生且不得手改的 legacy.json、含 declaredDifferences 的 expected.json。
4. 局部綠燈小段立即以 P5-02 開頭提交；僅 git add 明列檔案；不改寫已推送歷史。完整包未驗收或固定 guard 未通過時不推送。
5. PresentRecord 正式 generate 仍 CompileFailed 且沒有產物；不為反射擴充 harness，已提前停下交使用者決定是否整包採 D5-3，詳 research/p5-02a-checkpoint.md；原定17:25最晚判定點尚未到期，不宣稱耗盡。
6. 取得使用者決定後續行對應方案；成功黃金、差異契約、覆蓋率 >=80% 及固定八項守門仍是未完成條件；驗收後才推送並用 git ls-remote 核對，再接 P5-03a。實機前仍須依 D5-18 及 User Secrets 確認真實測試 ID，不使用名稱猜測。
