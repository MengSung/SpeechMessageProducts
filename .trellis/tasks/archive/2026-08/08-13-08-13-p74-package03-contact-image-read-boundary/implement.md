# P7.4 Package03 聯絡人圖片唯讀邊界實作計畫

## 前置條件

- 已讀取 AGENTS.md、parent P7.4 task、Package03 inventory、immutable matrix 與 cross-user isolation spec。
- 維持所有 feature gate 為 false，不做 CE、流量、P7.5 或 P8 操作。
- 以 `Start-CcgDualModelRun.ps1` 進行一次最多 45 秒的架構分析；沒有可用雙模型結果時，在 task 紀錄標示「雙模型未完成」。

## TDD 實作順序

1. 新增 service contract tests，先驗證固定 profile/workload、content type、defensive copy、取消與 A/B isolation；執行並確認 RED。
2. 新增最小 `Package03ContactImageReadService` 與 immutable result，令 service tests GREEN。
3. 新增 controller/source contract tests：false-gate 在 parse/authorization/service 前停止；true-gate 的 server scope 授權在 parse 前、精確目標授權在 parse 後且兩者都在 dispatch 前；既有 route 不變；無 cache/legacy/fallback；取消不進 generic catch。執行並確認 RED。
4. 新增 controller route、組態 gate 及專屬 service 解析，令 contract tests GREEN；不修改 `GetContactImage`。
5. 針對 fault/no-image 映射補足最小測試與實作，不加入未驗證的 LINE/avatar parity 或 UI consumer。

## 驗證與完成

6. 執行 targeted tests、ChurchReport Release test project、solution Release build、完整 solution tests。
7. Byte-check 本 child 所有 `.cs` 為 UTF-8 無 BOM、CRLF-only、final CRLF；執行 `git diff --check`、scope check、forbidden symbol scan。
8. 透過 self-healing runner 執行 reviewer，最多等候 45 秒，修正 Critical finding 或準確記錄降級。
9. 更新 child/parent task records；不改 immutable matrix、不宣稱 CE/cutover，僅 stage/commit/archive 本 child 擁有的檔案。

## 回復點

- 在任何實作或測試不符合 DTO-only/no-fallback/隔離條件時，回到前一個 RED/GREEN checkpoint。
- 不會變更既有 route 或資料；刪除新 route 或保持 gate=false 可確定回復本 child 的可見行為。

## 2026-08-13 實作與本機品質結果

- [x] 新增獨立 `/MemberInfo/Package03ContactImage` route；gate=false 在 session、GUID、target authorization、
      typed client 與 I/O 前回傳 404，既有 `GetContactImage` 未修改。
- [x] 新增 request-local `Package03ContactImageReadService`／defensive-copy result；固定 deployment profile 與
      `church-report-member-info-image-read` workload，僅發布 PNG/JPEG bytes，沒有 cache、legacy fallback 或 retry。
- [x] 新增 service、controller source-contract 與 bootstrap composition tests；先確認 production types 不存在的 RED，
      再完成 GREEN。A/B interleaving、取消、空 payload、未知 media kind、empty profile 與 disabled gate 都有 regression。
- [x] targeted 22 tests、ChurchReport 577 passed／14 explicit skips、solution Release tests、Release build、byte encoding、
      `git diff --check` 與 forbidden-symbol scan 通過。
- [x] CCG self-healing reviewer 在 45 秒上限內取得 Gemini 與 Claude 的可用輸出，兩者皆無 Critical／Warning；
      這是完成的雙模型審查，不是降級結果。
- [x] 保持 `Package03SpecialResourcesEnabled=false`；沒有 CE、traffic、P7.5、P8、push 或 PR 動作。
