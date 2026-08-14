# CCG final reviewer：P7.4 ORG-CALL-00027 MemberInfo 上課紀錄授權邊界

請只審查目前 task-only diff。禁止修改檔案、執行 CE、改 feature gate/traffic、重播 Slice C、開始 P7.5/P8。

已由完整 source trace 確認：

- `LoadContactStorLessons` 在 typed composition 前呼叫 `EnsureCorrectUserData`、`CanViewContact`。
- `GetAccess` 讀取/寫入 Session `_MemberInfoAccess`，並使用 shared `InMemoryContext` login model/ListManager。
- Shepherd path 在 target allowlist 前經 `GetShepherdContactIds` -> `EnsureShepherdListsLoaded`，必要時以保存帳密 `SetupListManager`。
- `BaseChurchController.EnsureCorrectUserData` 也以 Session password 和 static validation cache 協調 mutable `ListManager`。

審查現行 task artifacts 的 local-design-no-go 是否正確，並確認：

1. 沒有把後段 `CanViewContact` 結果誤當成 immutable Gateway authorization boundary。
2. 禁止 runtime/sub-gate/partial Church workaround/SDK bridge/fallback/retry 是否足夠。
3. 恢復條件是否要求 authenticated-principal-derived immutable MemberInfo scope 先於 Session、InMemoryContext、cache、ListManager、profile/client composition 與 CRM I/O。
4. 沒有誤宣稱 CE、consumer cutover、P7.5 或 P8 evidence。

輸出繁體中文 Critical / Warning / Info；若沒有問題，寫明 no findings。超過 45 秒不等候。
