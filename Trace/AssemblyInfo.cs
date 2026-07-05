// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Trace/AssemblyInfo.cs
// 所屬區塊：追蹤與診斷相關工具程式。
// 檔案責任：此檔案提供 AssemblyInfo 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：此檔案沒有明確型別宣告，可能是組件屬性、頂層設定、partial 補充檔或工具支援檔。
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System.Reflection、System.Runtime.CompilerServices
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：若此檔案由工具或專案系統產生，後續重新產生時可能覆蓋註解；修改前應先確認來源工具與產生流程。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Reflection;
using System.Runtime.CompilerServices;

//
// 組件的一般資訊是由下列的屬性集所控制。
// 變更這些屬性的值即可修改組件的相關資訊。
//
[assembly: AssemblyTitle("Trace")]
[assembly: AssemblyDescription("Enhanced tracing and debugging utilities for .NET 10 applications")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("SpeechMessage")]
[assembly: AssemblyProduct("ChurchReport Trace Library")]
[assembly: AssemblyCopyright("Copyright ? 1997-2025 John Robbins & SpeechMessage")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

//
// 組件的版本資訊由下列四個值所組成:
//
//      主要版本
//      次要版本
//      組建編號
//      修訂
//
// 您可以自行定義所有的值，也可以照以下的方式使用 '*' 將修訂和組建編號設定為預設值:
// ? 修正：移除萬用字元 '*'，改為固定版本號 2.0.0
// 原因：.NET 10 的確定性編譯 (Deterministic Build) 不允許萬用字元版本號

[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

//
// 若要簽署組件，您必須指定要使用的金鑰。
// 如需組件簽署的詳細資訊，請參閱 Microsoft .NET Framework 說明。
//
// 使用下列屬性來控制要使用哪把金鑰來簽署。
//
// 注意:
//   (*) 如果沒有指定金鑰，組件就不會被簽署。
//   (*) KeyName 是指在電腦上密碼編譯服務提供者 (CSP) 中所安裝的金鑰。
//       KeyFile 是指含有金鑰的檔案。
//   (*) 如果同時指定了 KeyFile 和 KeyName 的值，就會發生下列的處理:
//       (1) 如果在 CSP 內可以找到 KeyName，就會使用這一個金鑰。
//       (2) 如果 KeyName 不存在但 KeyFile 存在，就會將 KeyFile 裡的金鑰安裝至 CSP 中
//           並使用它。
//   (*) 若要建立 KeyFile，您可以使用 sn.exe (強式名稱) 公用程式。
//       在指定 KeyFile 時，KeyFile 的位置應該相對於專案輸出目錄，即為
//        %Project Directory%\obj\<configuration>。
//       舉例，如果您的 KeyFile 是在專案目錄中，您就應該將 AssemblyKeyFile 屬性指定為
//        [assembly: AssemblyKeyFile("..\\..\\mykey.snk")]
//   (*) 延遲簽署是一個進階選項 - 如需詳細資訊，請參閱 Microsoft .NET Framework 說明。
//
// ? 金鑰檔案在專案檔案 (Trace.csproj) 中已指定
//[assembly: AssemblyDelaySign(true)]
//[assembly: AssemblyKeyFile("..\\..\\SpeechMessageCrmKey.snk")]
[assembly: AssemblyKeyName("")]
