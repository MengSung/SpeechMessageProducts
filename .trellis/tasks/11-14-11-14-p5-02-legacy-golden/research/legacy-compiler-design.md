# 舊專案編譯與安全改寫設計

2026-11-14 14:00 Asia/Taipei (UTC+08:00)：本文件先於 compiler/rewrite 實作建立。範圍僅 SourceReader、Classification、LegacyProjectCompiler、TransportRewriter，不載入或執行舊 DLL，不修改舊來源或 csproj。

## 輸入閉包與編碼

僅接受工作樹外 Dynamics365Line 第一層專案，依 csproj 明列 Compile Include 與 ProjectReference 遞迴建閉包；Properties、bin、obj、packages、backup 與 reparse point 不納來源。每專案最多 512 檔、每檔 1 MiB、總來源 16 MiB，參考閉包最多 64 專案。XML 禁止 DTD，不執行 Import、Target 或任何 MSBuild 工作；條件式來源、glob 或未支援的 Include 表達式 fail closed，不能猜測收進副本。AssemblyInfo.cs 為組件中繼資料，合併閉包時排除，避免相依組件重複 attributes。

讀取時依 BOM 嚴格 UTF-8/UTF-16；無 BOM 先 strict UTF-8，再 strict CP950 並要求位元組往返，保留原始字串與 mojibake，不改常值。來源只存單次編譯區域，輸出僅摘要與前 20 筆代碼及檔名，永不輸出診斷訊息、原文或秘密。來源含 token 不構成編譯拒絕條件，秘密掃描只管輸出。

## 編譯與安全界線

Public LegacyProjectCompiler.Compile(root, projectName) 及 CompileSources(sources) 回傳 IDisposable CompilationResult，Status 為 Generated／CompileFailed／BlockedByNetworkApi，Diagnostics 為固定代碼與檔名，成功有 AssemblyBytes 與可選 EntryType。Dispose 清除 owner bytes；呼叫端自行負責隔離 AppDomain 與執行結束卸載，compiler 不 Assembly.Load。

Roslyn 使用 net48 reference assemblies、harness Assembly.Location、已還原 CRM Core／Workflow／Newtonsoft，以及舊 packages 的 RestSharp、zxing／WebApi Formatting；不引用舊 bin、obj 或任意 DLL。原始與改寫後皆依 semantic symbol 驗證：HttpClient 建構改 RecordingHttpClient；RestClient 建構及型別改 RecordingRestClient；已知 File／Directory API 與 FileStream 開啟改 ScratchFiles。StreamWriter 只允許 stream 輸入，路徑輸入改 scratch。IOrganizationService 是注入的假 CRM 契約，不當作網路。一般 HttpRequestMessage、HttpContent、Uri、HttpStatusCode 是資料，不視為傳輸。

未攔截 WebRequest／WebClient／Socket／TcpClient／DNS／SMTP／WCF client proxy、原始 HTTP handler、反射／dynamic／PInvoke／程序／實體檔案 API 拒絕。即使 API 隱藏在未呼叫 helper 也拒絕，不以可達性猜測放行。PostAsJsonAsync 等基於 HttpClient 的擴充可保留；所有 HttpClient 建構必須受攔截，衍生自行傳輸、handler 置換或反射逃逸拒絕。此來源驗證是舊來源離線 harness 的限制契約，不宣稱可執行任意敵意程式碼。

## 與 runtime owner 的接合

runtime 需 public RecordingHttpClient : HttpClient（無參數；固定 RecordingHandler）、RecordingRestClient(url／無參數)，ScratchFiles.Open(path, FileMode, FileAccess, FileShare) 回 Stream，以及 ReadAllBytes、ReadAllText、WriteAllText、AppendAllText、WriteAllBytes、CreateDirectory、Exists。ScratchFiles 每次執行只能持有自身 root，所有輸入路徑（包含絕對磁碟路徑）必須映射至該 root 且不能逃逸；root 由 AppDomain/runner 最後清理。

舊 RestSharp 105.2.3 存在，但 extension Get 可能繞過 subclass；優先採 harness 內同名 RestSharp shim，compiler 偵測 shim 時不再引用舊 RestSharp DLL。若使用實際 DLL，runtime 必須保證所有 virtual/extension dispatch 都導向記錄層；不能證明則拒絕。PresentRecord 閉包包含 Line.Messaging、LineUtility、ToolUtility、Trace，現有單專案遞迴掃描不符合此契約。

## 驗證

建置 PluginHarness；合成來源驗 Generated／CompileFailed／BlockedByNetworkApi、alias／fully-qualified 呼叫、檔案改寫、無秘密原文診斷；真實 PresentRecord 只編譯不執行，記錄安全阻擋或前 20 個代碼／檔名；最後執行文件守門及指定四個 C# 的 byte-level UTF-8 no BOM／CRLF／final CRLF 檢查。執行測試與 AppDomain 清理由主代理整合驗證。
