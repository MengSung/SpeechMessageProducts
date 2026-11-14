# 實作計畫
1. 已更正進度P5-02 active/P5-03 todo，保留有效更新回復證據；CCG analyzer透過自癒入口。
2. 主代理擁有runtime/Program/Models/GoldenWriter/OutputSecretScan、csproj與PluginHarnessTests；Kuhn獨占SourceReader/Classification/LegacyProjectCompiler/TransportRewriter；Pasteur獨占架構及duplicate修復；Beauvoir獨占Core差異契約及scenario輸入。
3. 先同名合成行為測試紅燈，再實作獨立Domain執行、錄製傳輸、有限CRUD、活動輸出；補故障/隔離/清理測試。
4. 接合compiler，真實PresentRecord至少三情境，產生legacy/expected並掃描；29入口只編譯矩陣，查明37與36範圍。
5. CI/slnf/coverage/JSON companions同步。新工具coverage>=80%，局部建置與測試、文件及bytes。
6. 雙模型review/獨立審查修Critical High，提交固定快照後完整八守門；通過才驗收推送核SHA及封存，接P5-03a。未達不得標done。
