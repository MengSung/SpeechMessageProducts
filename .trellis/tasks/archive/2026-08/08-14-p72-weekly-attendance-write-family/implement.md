# P7.2 週日出席與週報寫入能力家族：實施計畫

## Phase 1：規劃與稽核

1. [x] 讀取 goal objective、AGENTS.md、backend isolation/hosting specs、P7.2/P7.3 archive、authoritative
       matrix、P7.4 Package03 consumer inventory、相關 production QR utility 和 current git state。
2. [x] 建立此 child 的 PRD、design、implementation plan 與 context manifest，記錄 non-replay、scope、
       evidence hierarchy 和禁用的 read-new/write-legacy path。
3. [x] 完成 `PersonalQrCodeUtility`／`SundayQrCodeUtility` mutation graph source audit；把每個 legacy
       read/write/relationship/weekly-report/notification 副作用列入 research，並指定 first-slice candidate。
4. [x] 已透過規定 self-healing runner 完成一次最多 45 秒的 CCG architect analysis 嘗試；Gemini
       有可用概念性輸出，Claude 未完成。已記錄「雙模型未完成」，不重試等待，且不將轉碼輸出作為
       實作依據。
5. [x] 依 source audit 更新 PRD/design，curate JSONL；規劃決策與禁止事項另記於
       `planning-decision.md`，再進入 task activation review。

## Phase 2：本機 contract 與實作

6. [x] 重驗先前 P7.2 Slice H 的 TDD local-only contract（weekly cardinality、attendance upsert、semantic
       plan），結果 32 passed、0 failed。這些 contract 可作為新 writer 的必要基礎，但不能取代
       server authorization、ledger、fixture 或 CE evidence。
7. [x] 完成 production QR 入口與 legacy mutation graph 的 cross-layer audit，證明目前在
       authorization-before-I/O 前會寫入 shared `InMemoryContext`，且單一 QR 呼叫混合 present record、
       weekly report、meeting relation、weekly recomputation 與 notification side effects；不具備唯一
       selected mutation 的安全前提。詳細 no-go 見 `local-no-go.md`。
8. [x] 執行 focused local contract tests，確認 zero-active unlinked、exactly-one exact-link、
       duplicate/unavailable no-go、timeout/no-replay 和 A/B local isolation 均維持；沒有為 no-go 建立
       假 fixture、接線 Data8 executor 或 consumer。

## Phase 3：受控 CE evidence（僅在資格成立時）

9. [x] CE evidence 未開始：本機設計 no-go 使 read-only preflight、fresh fixture、controlled dispatch、
       read-back/reconcile 與 cleanup 都不適用；舊 Slice C 永不重試。
10. [x] 已依證據停止這個 QR attendance mutation family，不重試，也不將 no-go 推廣為其他獨立 P7
        local-only/read family 的停止條件；去識別化原因與恢復條件記於 `local-no-go.md`。

## Phase 4：品質、完成與封存

11. [x] 執行 targeted tests、required project/solution Release tests/build、encoding/CRLF、`git diff --check`、
        scope/isolation/lifecycle check。targeted attendance contract 為 32 passed、0 failed；Release build
        為 0 warnings、0 errors。完整 solution test 第一次在既有 Kestrel HTTP/1.1 body-limit case 出現
        暫時性 `ResponseEnded` transport failure；單獨及同類 45-case suite 均立即通過，最後再次序列化完整
        solution suite 亦通過。故記為非本 child 程式變更所致的測試主機傳輸波動，不以它掩蓋本機 gate 結果。
12. [x] 執行一次最多 45 秒的 CCG final review；Gemini 逾時且 Claude 沒有可用輸出，故記錄為
        「雙模型未完成」，只採本機 source inspection 與驗證，沒有把未完成輸出當作完整 review。
13. [x] 完成 spec update、scope-only commit、archive，並回寫 parent checkpoint；不改 matrix consumer state、
        feature gate、P7.5 或 P8。
