# 前置A 追蹤恢復一頁操作紀錄

2026-11-17 12:53 Asia/Taipei (UTC+08:00)：使用者重啟服務後，Codex單次診斷確認追蹤恢復；Plugin自己回報FileVersion=1.0.0.17。
smoke與收尾verify皆0，診斷停用、追蹤回原值1；未部署DLL、未啟用舊步驟/流程、未推播LINE。
前置A完成；接續前置B，後續影子比對按07A執行，不採追蹤不可用退路。

| 需求/步驟 | 證據 | 狀態 |
| --- | --- | --- |
| 起點 | P7-00 verify=0、trace=1、D34有效；使用者回報重啟 | 通過 |
| 追蹤與診斷 | 約12:50～12:52 Asia/Taipei，trace設定2讀回、switch diagnostics enabled；唯一smoke p0-ping-trace | 通過 |
| 新版執行證據 | smoke輸出PluginFileVersion=1.0.0.17、Smoke scenario passed，exit0 | 追蹤可用 |
| finally回復 | switch off=0、trace-restore讀回1、verify=0 | 通過 |
| 資料/資源 | smoke task0、待辦0、D34仍通過；每個CLI租約結束釋放 | 通過 |
| 留存追蹤 | 本次診斷trace1筆，限定診斷型別及本輪時間，R9先備份再清理 | 已登錄，不先刪證據 |
| 權限/外部模型 | 服務由使用者重啟，未自行提升；使用者指定本階段ClaudeOnly | 已記錄 |

本次只驗證追蹤診斷，不是各業務模組影子/Live證據；使用P5已部署且雜湊匹配的基準DLL，未聲稱P7新打包已在沙箱執行。
原始紀錄位於ignored artifacts/p7；證據SHA-256如下：
- `pre-a-trace-on.log`：`909a5d524d465eb948da4288c5ff539b233fcf99f2ed3b2a0285d631f3cf3ede`。
- `pre-a-enable.log`：`f40bf8ae52839b9cfe03880f7a4951dc637a3f3b7a776a2ea97dc6b737d6f1c7`。
- `pre-a-smoke.log`：`a657b6e6d6190eca439a72525bdcf31293691c6dfcc805abf2d43230b9419f00`。
- `pre-a-off.log`：`f40bf8ae52839b9cfe03880f7a4951dc637a3f3b7a776a2ea97dc6b737d6f1c7`。
- `pre-a-trace-restore.log`：`bc46865a5ebf541d23975f1aa09f01544b955db0ceb3dcf7af48edfea65db9bd`。
- `pre-a-final-verify.log`：`ca0262dcc68601a726404ba0bc102910c4b70a1fdcf432a80b1a447fb2e3a4dd`。
- `pre-a-final-inventory.log`：`0327bb6554abbfad5e07ef8a81d2224c73a46f311986ac18aa96a5490c8cca8d`。
