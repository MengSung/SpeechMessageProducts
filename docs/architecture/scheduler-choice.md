# F4 排程器選型與產品介接

依據：共通層實作計畫 1.3 的 01 F4；更新日期：2026-11-26。

採用 **Hangfire.Core、Hangfire.SqlServer、Hangfire.AspNetCore 1.8.25**，以平台 SQL 持久化工作。相比自建 timer／queue／排程表，Hangfire 已提供工作狀態、持久化及 worker 基礎設施，減少重啟、重試與停止的自行維護成本；產品隔離、相同工作鍵覆寫、重驗與業務冪等仍由本方案負責，不能從採用套件推論恰好執行一次。此頁記錄核定設計與產品介接；核心 SQL、重啟及關機保證須以共用排程測試的實際結果為準。

| 固定契約 | 責任與界線 |
| --- | --- |
| 每產品獨立 schema | `scheduler_goodshepherd2`、`scheduler_goodbutler`、`scheduler_goodguild`；工作仍驗完整產品／組織／租戶，schema 分開不能取代授權。 |
| WorkerCount＝4；ShutdownTimeout＝30 秒 | 共用核心是 SQL storage 與 server 唯一 owner；停止先阻止新工作、排空 server，再釋放 storage。30 秒不是任意外部 SDK 可強制中斷的保證。 |
| 自動重試最多 3 次 | 核心須明確設定，不能沿用 Hangfire 預設 10 次。最終失敗保留 Failed 與同一真實 Exception，交共用安全告警；正常 caller 取消不告警。 |
| 週期與速率 | 使用明確 IANA 時區；停機錯過的週期只補一次。宮格建立／刪除各每產品、每 Channel、每 UTC 小時 100 次；無額度由核心延到下小時，不假稱已執行。 |

產品組合根只呼叫 `AddGoodShepherd2Scheduling(WebApplicationBuilder)`。產品固定為 `GoodShepherd2`，啟用鍵是 `Scheduling:OrganizationId`（非空 D 格式 GUID），SQL 沿用 F6 使用的 `ConnectionStrings:Platform`；不新增第二份連線來源，不在此文件保存連線內容。連線須符合核心安全驗證（專用非系統資料庫、TLS、禁止信任未驗憑證、`Enlist=false`），SQL初始化與連線池仍由核心負責。完全沒有 `Scheduling` 區段時，保留明確拒絕排程／取消及拒絕 scope 的插槽，不開 SQL 或 CRM；共用 Platform 連線單獨存在仍屬未啟用。存在區段但組織缺漏、無效、空 GUID、未知鍵或缺 Platform 時拒絕啟動。

F2 既有 URL 白名單不證明實際組織 ID。產品使用同一 `IDataverseConnectionManager` 的 lease，在 Host `StartingAsync` 以 `WhoAmI` 驗證完整身分及組織；所有 lifecycle 的 Starting 完成後，核心才開始 Start。每次工作另借獨立短期 lease 再驗 `WhoAmI`，租戶工作即時讀權威 account ID、`new_istenant=true`、`statecode=0` 及生命週期。null 租戶只表示同組織的明確組織工作；沒有註冊任何產品工作、每日健康工作或 dashboard，F3 每日健康註冊留給 F11。

**生命週期值的依據與拒絕策略：** 1.3 計畫只定義七種名稱，當前可讀 repo schema 沒有 `new_lifecyclestatus` 的正式數值。因此執行前讀 CRM 已發布的該欄位 metadata，以 LCID 1028 的七種唯一 label（申請中、待啟用、試用中、進行中、待配發名額、保留中、已結束）映射權威數值；不依選項順序或猜測常數。保留中、已結束、停用、查無租戶、缺欄、未知／重複 metadata 或非核定值均拒絕並只記固定原因。欄位尚未部署時的 SDK 真正 fault 保留給核心告警，不當成成功或快取成永久拒絕。此策略不快取角色、token、授權結果或 schema，不增加一般 HTTP 查詢；每個租戶工作增加一次 WhoAmI、一次單列查詢及至多一次 metadata 查詢。同步 SDK 進入後完整等待，取消／故障淘汰 lease 再歸還；歸還失敗不得發布允許結果。

失敗、網路逾時或程序中斷可能造成外部副作用已完成、工作狀態未完成的「結果不明」情形。重試仍可能再次到達業務 handler；相同 `JobKey` 的待執行覆寫不等於業務副作用恰好一次。執行前查核只是當時狀態，沒有跨 SQL／D365 的原子交易。handler 必須以權威業務事件的 `BusinessKey` 及完整 scope 建立資料庫／Dataverse 原子唯一鍵或條件更新，副作用前重驗業務來源，且禁止在 payload 保存憑證、LINE userId、角色、Session、HttpContext 或可變 SDK 物件。副作用結果不明須核對權威資料或送審計補償，不可盲目重送宣稱成功。

**LGPL-3.0 使用方式：** 使用上游 NuGet 的獨立 `Hangfire.*.dll` 動態連結，不修改、內嵌、ILMerge 或搬入 vendored 原始碼。散布時須保留上游著作權與授權告知、提供 LGPL／GPL 授權文字及對應版本程式庫來源取得方式，並允許使用者以相容介面版本替換程式庫及為修改程式庫除錯而逆向工程；不得以應用程式條款限制這些權利。發行封裝需另核對實際 DLL、授權文字與來源交付，不以本頁冒稱已完成封裝。若未來修改上游，須另外履行修改版程式庫來源及通知義務。

官方依據：[1.8.25 發布](https://github.com/HangfireIO/Hangfire/releases/tag/v1.8.25)、[該版本 LGPL 原文](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/COPYING.LESSER)、[上游授權說明](https://www.hangfire.io/pricing/)、[LGPLv3 §4](https://www.gnu.org/licenses/lgpl-3.0.html#section4)、[Hangfire 例外與預設重試](https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html)。離線產品測試使用假 SDK，保護組織失配、狀態變更、未知 metadata、重複列、取消排空及清理故障；不代表真實 D365、SQL、LINE 或整包 F4 實機驗收。
