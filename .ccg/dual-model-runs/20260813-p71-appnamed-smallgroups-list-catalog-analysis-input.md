# ORG-CALL-00065 local capability architect analysis

請只做 repository-source analysis，不要寫檔、不要執行 CE、不要讀取或輸出 credential、endpoint、CRM ID、cookie、token、原始 response 或 raw exception。

目標：為 authority matrix 的 `ORG-CALL-00065`／`list.catalog.retrieve.appnamed.smallgroups` 設計一個獨立、fixed-template、zero-caller-parameter、bounded DTO-only Data8/ProductClient read capability。

已知事實：

- `ORG-CALL-00014` 剛完成為相鄰但不同 operation；不可共用 operation ID、template、response branch 或 DTO。
- legacy `ToolUtility/ListOperations/ListService.RetrieveSmallGroupLists()` 固定投影 list name/code/last-used/purpose/list ID 加兩個 leader lookup，固定 active/purpose/app-named filter，並排除退出名稱 pattern。
- ChurchReport `DownloadListManager`、`ListManagementDataManager`、`InMemoryDataContextSmallGroup` 保有 legacy ToolUtility/SDK graph 及共享可變 cache/state。本 child 不得遷移、reference 或修改這些 consumer。
- 歷史 P7.2 Slice C 已 immutable closed；不做 CE、fixture、feature enablement、traffic、P7.5 或 P8。

請提出：

1. fixed QueryExpression/filter/order/projection、leader lookup scalar contract、bounded paging/byte limits。
2. registry/wire/executor/ProductClient 層的最小閉合新增設計，特別是 response union 不可混 branch。
3. cross-user/profile isolation、resource lifecycle、cancellation/fault、cache avoidance、rollback 邊界。
4. 必要的 red-green tests 和 matrix evidence update。
5. Critical/Warning/Info；任何阻止此 local-only child 的真正 no-go。
