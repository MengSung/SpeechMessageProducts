# 更新回復前置需求
依 05A 修訂1執行更新回復前置第0至5步；使用者已授權階段5，不另重複詢問。
離線必須驗證無私有反射的 Shell Shadow 與 member 通知鏈、Plugin 自身 FileVersion 及 smoke 安全版本輸出。
實機只在 goodshepherdback 以 trace 診斷執行第一版→新版→第一版，AssemblyVersion、簽章及步驟ID不變，最後停用、追蹤還原且殘留0。
未完成實機前不得部署業務模組；依D5-1可續做P5-02/03，不冒稱已驗收。

## 指定驗收測試
- `PartialTrust_ShellShadowChain_Runs`：外殼＋影子記錄與既有member完整通知鏈，無私有反射權限，AppDomain必須卸載。
- `Trace_PluginFileVersion_ComesFromPluginAssembly`：值等於Plugin自身屬性、不同於FrameworkFileVersion且無秘密。
- smoke必須安全輸出有效版本，惡意／重複／缺少版本不得冒充三次版本證據，操作失敗／取消仍清理task及租約。
