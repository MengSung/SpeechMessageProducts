# P4-06 APIService 補驗回報

**補驗時間：** 2026-11-13 19:59 Asia/Taipei (UTC+08:00)。

**三句話：** 將 Sinopac QPay 外層 `APIService` 改為明確映射規格服務名稱，並以契約測試鎖定 `OrderCreate`、`OrderPayQuery`、`AllotQuery`。provider 測試 20/20、AllotQuery 測試 11/11、固定 guard §8.1 八項通過；修正後唯一一次 Sandbox `Live_AllotQuery_SandboxRoundTrip` 通過 1/1，驗簽 provider status=`S`。本次沒有撥款明細證據、沒有真實付款；P4-06 已驗收、P4-PROP-008 解除，P4-PROP-009 待核定，階段 5 未啟動。

| 需求 | 證據 | 狀態 |
|---|---|---|
| 外層服務名稱契約 | `Envelope_ApiService_UsesDocumentedServiceName`、`Envelope_ApiService_NeverNumeric`、`Envelope_ApiService_RejectsUnknownEnumValue`；provider 20/20 | 通過 |
| AllotQuery envelope | `SinopacAllotQueryTests` 11/11；Sign、Message 與內層 JSON 固定向量未改 | 通過 |
| 完整固定守門 | 提交 `d23d3a47`；§8.1 八項 exit 0；Release build 0 errors、全方案測試 0 failed、Debug tracer 7/7；encoding 145 files/0 issues、duplicate new=0、I18n、file documentation 1,468/0 failures、progress 28/28 | 通過 |
| Sandbox 補驗 | `Live_AllotQuery_SandboxRoundTrip-20261113.trx` 1/1 Passed；SHA-256 `B05B3A689D82699F2D08ACE521D8FBF60EFE7C8BBF117BC2501F1A1B51428517`。已驗簽 provider status=`S`；Fee snapshot 未變，TRX 沒有記錄撥款明細筆數 | 通過；不代表發生付款或取得撥款明細 |
| 清理 | TRX 輸出確認測試 Contact、Fee、Outbound Task 各 0 | 通過 |

**守門證據：** `guard-d23d3a47-full.log` SHA-256 `DA326CD57AAD45F1123A3987D5F518FFE6C541E3FDE4F081D0B85A5B622E8EF6`；`guard-d23d3a47-prepare.log` SHA-256 `FD95389EE18ED2202BAC397C6855105577682106CD438957530DB97F54AEEFCE`。操作紀錄位於 P4-06 子任務本機 evidence；未保存任何設定值、Nonce、Sign、回應本文或個資。

**提案：** P4-PROP-008 已因 Sandbox 狀態 `S` 補驗而解除。P4-PROP-009 建議後續版本維持三種 QPay `APIService` wire name 的明確契約測試，防止 enum 數字序列化回歸，待使用者核定。
