# ChurchReport Package01 drain-first non-overlap runbook

本 runbook 是 deployment owner 的離線操作順序。它不包含任何上游識別、路由、
認證資料或可重播的請求內容；每一步只使用部署擁有者可驗證的 bounded read-back。
未完成的證據必須保持 no-go，不能以猜測或局部狀態放行。

## 1. Stop legacy intake

由 ChurchReport lifecycle owner 停止新的 legacy intake。停止動作必須是
deployment-owned、可回復且只作用於目前 canonical Organization plan。記錄固定的
\`legacy-intake-stopped\` 分類，不記錄請求、使用者或上游欄位。

## 2. Drain

等待既有 legacy work 完成，使用 bounded deadline。lease loss、取消、dispose
不確定性、同步呼叫超時或任何 sync overrun 都是 no-go，分類固定為
\`sync-overrun\`；不得以延長無界等待掩蓋未知工作。

## 3. Read-back

由 deployment owner 讀回 durable coordinator 的 Organization-level 狀態，確認
canonical binding、generation/epoch 與 durable topology 均一致。controller 的
\`stopped-and-drained\` 狀態只是 process-local observation，絕不能當作
Organization-level proof。讀回失敗、未知 legacy coverage、non-durable topology
或 binding 不一致時，固定分類為 \`legacy-coverage-unproven\`、
\`non-durable-topology\` 或 \`canonical-binding-unproven\`，維持 no-go。

## 4. Gateway readiness

只有在 read-back 已證明同一 canonical binding 與 durable coordinator 後，才由
Gateway owner 執行 readiness 檢查。Gateway readiness 必須是獨立 owner 的證據；
controller 狀態、旗標文字或本機記憶體計數器都不能替代它。未知或失敗固定分類為
\`gateway-not-ready\`。

## 5. One smoke

Gateway owner 在 readiness 通過後只執行 one smoke，使用合成且不含敏感資料的工作
標記，確認單一路徑已就緒。不得並行恢復 legacy intake，不得執行額外同步呼叫；
smoke 失敗立即 no-go。

## 6. Rollback

rollback owner 必須在 smoke 前已被明確指派，並保有停止 Gateway、維持 legacy
intake 停止、重新 drain 與 read-back 的固定順序。任何未指派 rollback owner
固定分類為 \`rollback-owner-unassigned\`。rollback 不能以 controller 狀態宣稱
Organization-level 安全，也不能在未知 legacy coverage 下開啟任何 gate。

## Fixed validator contract

使用 \`docs/scripts/Test-ChurchReportLegacyGatewayNonOverlap.ps1\` 時，只能提供下列
六個 bounded switch：\`-Durable\`、\`-CanonicalBinding\`、\`-LegacyDrained\`、
\`-LegacyCoverageProven\`、\`-GatewayReady\`、\`-RollbackOwner\`。六個 switch 全部提供
才會輸出 \`go: all-required-evidence-proven\` 並以 0 結束；任何缺漏或額外輸入都
輸出固定去識別化 no-go 分類並以非 0 結束。腳本不連線、不寫檔、不接觸 CRM/SQL，
也不改變 feature flag。
