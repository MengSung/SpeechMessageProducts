# 更新回復前置整合計畫

既有授權為全部階段5；固定依05A，不碰正式組織、不開新Live步驟、不讓舊新同時啟用。
範圍因獨立複核與打包閉包新增回歸跨過5檔，升L+；分工已按檔案歸屬執行並關閉完成代理。

1. DiagnosticsPingPlugin自身FileVersion追蹤及同名紅綠測試（主代理）。
2. Deployment安全smoke版本發布、格式／來源／失敗清理測試（獨立worker，已完成）；共用correlation誤判以兩入口紅綠修正（主代理）。
3. 最小CAS Shadow＋既有member通知鏈（獨立worker）；SDK僅完整StrongName平台載入例外，產品與測試仍受限，不擴權。
4. 合併member既有驗證器保留唯一具體IPlugin入口，容許核定的抽象外殼（主代理，6/6通過）。
5. 生命週期診斷：獨立研究者隔離HMAC/AES/RNG與Framework卸載，禁止把斷言綠燈等同CSP清理通過。若需改加密產品，先列根因與相容向量／雙宿主／CAS回歸，再最小修復，不改協定或增加權限。
6. 外部分析／審查已走自癒入口但均無結果，明示未取得；獨立本地審查及複核實際變更，Critical/High修復。
7. 全部離線合格後提交指定訊息，唯一固定guard跑00§8.1；通過才做goodshepherdback trace更新／rollback。實機前置不足依D5-1記錄並續P5-02/03，禁止部署模組。

回復：程式未推送前保留使用者既有修改；實機使用更新apply的export回復第一版，finally停用診斷步驟、還原追蹤及工作樹清單，精確本次資料殘留0。
