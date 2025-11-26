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
