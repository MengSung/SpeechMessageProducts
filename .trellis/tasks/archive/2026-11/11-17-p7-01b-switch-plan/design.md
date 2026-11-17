# P7-01b 設計

唯一依據07A §4.3/4.4/§5 P7-01b。不得改計畫、guard或其他測試期望；不連任何組織、不啟舊程式、不新建csproj，使用者已核定順序inline執行。
SwitchTarget.Resolve以純函式接組織state、有效tier及tokenKey，產Enabled/Mode/SecretPlaceholder三純值，五列狀態唯一實作供plan/apply/verify。有效tier按型別override優先模組override，再預設；diagnostics不加入live切換。保留unsecure options，只有mode受控轉換，不在plan解析secret。
SwitchPlan為不可變有界DTO：組織名稱／WhoAmI正式ID／kind／目前fingerprint／target／manifest及catalog hash／本地及遠端兩DLL hash／ordered operations與sha256。SwitchSnapshot讀同命令SDK的完整有界metadata，step穩定ID及config安全投影／secure presence；workflow舊活動正式參照與XAML由switch-log本機owner保留，plan不輸出秘密。
切live先SetStepConfiguration（前提原step停用）→停舊step→流程改指／停用還原啟用→啟新step，同步先於非同步。切回先停新→流程／舊state回復→config；任何互斥衝突先拒絕。dormant不啟、不改指；有舊啟用列保留並列KeepLegacy／Manual供決定，不擅自啟用舊程式。production-live證據必須每個有效live項目pass，DLL不存在／hash異即拒絕。
WorkflowRepointer由ActivityProbe抽共用XML解析／精準改寫，不全文Replace；舊測試語意不變。先讀完整活動引用，混合改指／保留則整流程KeepLegacy；超1MiB列Manual。實際啟停用用SetState，原始XAML逐位元組保存在log以便回復，不以重序列化XML作rollback。
SwitchEngine執行前重讀serverId／fingerprint／source hashes及完整重算plan，另外核檔案hash；先export與legacy export、before log先flush再操作，每一步已嘗試／完成與before/after均持久記錄。SDK寫入可能送達後拋錯，失敗／取消對已嘗試者用CancellationToken.None反向回復，聚合原故障與cleanup故障；不把回復失敗當通過。
安全設定只在實際寫入時讀User Secrets，plan與log僅placeholder/presence不含key。回Shadow清secure lookup及無引用secure row；原Live安全設定恢復以授權secret source重新產生，不讀出舊明文。資源全部命令owner，SDK drain→lease Dispose→寫鎖關閉，無static mutable/cache/背景Task。
CLI新增plan --switch-to、switch --plan --plan-sha256、verify --plan；rollback判別switch-log與原export格式但保持嚴格JSON與路徑。ProductionGuard仍共同第一關；原module switch與export rollback行為不變。verify無plan使用catalog.switchState，含D7-7；plan與verify共用SwitchTarget，不能以手改manifest假裝狀態一致。

補充契約：新型別全部放Switching/，名稱沿07A：SwitchTarget、SwitchPlanner、SwitchExecutor、SwitchLog、WorkflowRepointer。引用未移植活動的流程整體KeepLegacy，包含JoinMainSmallGroupList.JoinMainSmallGroupListPlugIn，停用流程亦完全不動；測試SwitchPlan_WorkflowReferencingUnportedActivity_ListedAsKeepLegacy。前置A追蹤可用，P7-02a R4必做，不走D7-5。不得修改或提交.ccg/tasks/line-token-config-separation。

殘留目錄檢查：四個11-09-p3任務目錄實際含91/7/24/18個evidence檔案，未滿足刪除空目錄條件，保留原貌。
