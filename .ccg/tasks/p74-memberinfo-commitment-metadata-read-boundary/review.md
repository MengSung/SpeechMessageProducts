# P7.4 MemberInfo 承諾類型 metadata 讀取邊界審查

## 本機審查結論

- **Critical：已修正。** 三個 typed metadata consumer 原本在取得 request-local snapshot 後，仍以
  `GetRequiredClosedCustomerTypeValue(service)` 查詢 legacy OptionSet service。這會讓同一 response
  的 `結案` 值跨回 legacy metadata cache，可能混用不同 profile/generation 的 metadata。已先新增
  RED regression contract，確認失敗後才改為 `GetRequiredClosedCustomerTypeValue(service, typedCommitmentOptions)`；
  typed branch 用 `Single` 對 snapshot 的 `結案` 精確標籤取值，缺少或重複皆 fail closed，沒有 legacy lookup。
- **Warning：不在本 child scope。** 通用 Package02 contact-profile factory 沒有在 composition 前驗證
  ProfileAlias；本 child 實際使用的專用 Package02 ungrouped-commitment factory 已在 host resolution 前
  驗證，故不混入這個 Package03 metadata child。後續只有在通用 Package02 factory 的 owner child 才處理。
- **Info：** checked-in Package03 base/sub gate 都維持 `false`；沒有 CE mutation、feature enablement、
  traffic cutover、P7.5 或 P8 操作。沒有把 skipped live/CE test 當作證據。

## 驗證證據

- RED：`Commitment_metadata_typed_snapshot_resolves_closed_status_without_a_legacy_lookup` 初始失敗，因
  三個 action 均只呼叫 legacy closed-status resolver。
- GREEN／focused：42 passed，0 failed。
- `ChurchReport.MemberInfo.Tests`：606 passed，14 controlled live/CE skips，0 failed。
- solution Release tests：0 failed；`SpeechMessage.Dynamics.Tests` 739 passed／7 live SQL skips；
  `ChurchReport.MemberInfo.Tests` 600 passed／14 controlled live/CE skips。
- `dotnet build SpeechMessageProducts.sln --configuration Release --no-restore`：0 warnings、0 errors。
- task-scoped UTF-8 無 BOM、CRLF-only、final CRLF：passed；`git diff --check`：passed。

## 外部雙模型審查

- 透過 `Start-CcgDualModelRun.ps1` 啟動 final reviewer，等待上限 45 秒。
- Gemini wrapper 以 status `4294967295` 異常結束；Claude 在時限內沒有輸出；health check 本身正常。
- 狀態為 **雙模型未完成**，沒有 usable single-model fallback，不能宣稱完整雙模型審查；依使用者授權改採
  上列本機 tests、Release build、source/diff/encoding 檢查。
