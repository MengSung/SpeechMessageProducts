# P7.4 MemberInfo 小組樹授權來源稽核

## 範圍

只稽核 ORG-CALL-00031／00032 的既有 authorization source，判定能否安全進入 Gateway local
implementation。只修改 task/CCG 記錄，不修改 runtime、matrix、feature gate、CE、traffic、P7.5 或 P8。

## 關鍵安全需求

- Gateway capability 必須在 Session、InMemoryContext、cache、client composition 與 CRM I/O 前，取得
  server-derived、immutable、request-local authorization scope。
- Shepherd branch 不得以保存 account/password 載入 shared ListManager 作為 scope authority。
- 不得只遷移 Church branch 或傳遞 `Entity`／`EntityCollection`／query／credential 等 raw legacy state。
- 無法證明完整 Church/Shepherd boundary 時，必須 fail closed 並記錄恢復條件。
