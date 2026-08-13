# P7.4 未分組承諾 aggregate 讀取邊界實作計畫

1. 建立 fail-first tests：gate 順序、fixed request contract、A/B isolation、DTO validation、
   cancellation、no legacy aggregate fallback 與 checked-in false settings。
2. 在 `DonationDynamicsAccessBootstrap` 加入獨立 sub-gate，維持 existing base Package02 gate 與
   process-host resource ownership；不得建立 per-request provider/pool。
3. 新增 request-local `Package02UngroupedCommitmentReadService`，只依 typed DTO 建立 validated count map；
   不引入 CRM SDK 型別、cache 或 background work。
4. 將 `LoadUngroupedMembers` 和 `LoadUngroupedCommitmentTypePage` 非同步化，只替代
   `ORG-CALL-00024` 的 non-empty count selection。typed fault 或 cancellation 不回落 legacy count。
5. 兩份 appsettings 新增 `Package02UngroupedCommitmentReadEnabled=false`，連同完整繁中註解。
6. 執行 targeted test、relevant Dynamics/ChurchReport tests、完整 Release test/build、encoding/CRLF、
   diff/scope/Trellis check。以 CCG self-healing runner 限時 45 秒取得分析與 final review；未完成即記錄。
7. 更新 child check/parent P7.4 記錄，scope-only commit/archive；matrix 不改寫，P7.5/P8 不啟動。
