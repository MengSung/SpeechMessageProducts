# P7 MemberInfo small-group snapshot data plane

此高風險跨層 task 僅處理 ORG-CALL-00031／00032。新 Data8 composed read 只能消費既有 immutable `MemberInfoTargetAuthorizationScope`，回傳 bounded descriptor/membership DTO snapshot，且 membership IDs 必須由同一次 descriptor result 導出。

不得修改 `MemberInfoController`、Session、`InMemoryContext`、`ListManager`、ToolUtility、feature gate、traffic、CE fixture、週報、P7.5、P8 或 ORG-CALL-00033。失敗、timeout、取消、paging、schema/bound fault 一律 fail closed，沒有 retry/partial/fallback。

