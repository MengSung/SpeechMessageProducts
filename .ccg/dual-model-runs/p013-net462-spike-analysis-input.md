請針對已核定的 P0-13 net462 離線 spike 分析以下方案，並檢查其風險。只使用附文；不要另讀repo、機密、appsettings、User Secrets，不呼叫外部服務，不修改檔案。
使用者核定：先離線試做4.6.2，建置／測試／打包，不部署；D21產品預設仍net48。
既有故障：D365 pluginassembly Create Stage30，TargetFrameworkVersionValidator.ValidateInternal 中FormatException；元件檔案9.1.49.21825；伺服器mscorlib.dll檔案4.8.4795.0；未測得框架上限、沙箱載入版本、net462註冊結果。
預計方法：
1. 在 artifacts/p013-net462-spike/ 的唯一隔離副本，只複製明確白名單的三個產品專案、三個相關測試、build規則與中央套件表；不複製配置、金鑰、授權、憑證或bin/obj。
2. 只在副本將net48與條件式參考擴充／切換為net462，Core中的#if NET48改為#if NETFRAMEWORK以保留Framework分支，polyfill包含net462。正式來源完全不動。套件版本完全不動，不抑制警告／稽核。
3. 跑原Plugin／Core／Localization測試；Core測試保留既有net48範圍的舊測試排除，不新增對net10舊SDK的引用。不更改測試方法或assert。
4. Plugin用原四輸入合併，正式key僅由現有環境變數交既有ILRepack，不讀／複製／輸出key。產物寫副本artifacts目錄；比對原net48部署DLL前後hash不變。
5. 原PluginPackaging檢查器保持原樣，預期以targetFramework=false拒絕net462；副本檢查器僅把精確預期v4.8改成v4.6.2，其他檢查不動，並跑副本原檢查器測試（fixture預期框架改462）。另確認該檢查器拒絕原net48，避免多目標任意通過。
6. 這是throwaway相容候選。測試在本機現有4.8 runtime執行，不能聲稱在純4.6.2 runtime／D365沙箱驗證。確認編譯引用確為4.6.2 reference assemblies、合併輸入均對應正確框架，不只改attribute。
請列Critical/High/Medium/Info與最小必要修正；避免建議不在核定範圍的部署、降警告或伺服器變更。繁中600字內。

## SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj
```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
    <!-- 嚴格開關與 XML 文件開關必須同在專案本體，避免 SDK 載入時點造成假綠燈。 -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <!-- 建置端以 -p:SpeechMessageBuildNumber 提供單調序號；本機首版預設一，不改 AssemblyVersion。 -->
    <SpeechMessageBuildNumber Condition="'$(SpeechMessageBuildNumber)' == ''">1</SpeechMessageBuildNumber>
    <FileVersion>1.0.0.$(SpeechMessageBuildNumber)</FileVersion>
    <InformationalVersion>1.0.0-p0.$(SpeechMessageBuildNumber)</InformationalVersion>
    <!-- 使用自訂目標隔離打包與原始測試組件；套件預設會覆寫 bin 並併入整個輸出資料夾。 -->
    <ILRepackTargetsFile>$(MSBuildThisFileDirectory)../build/PluginMerge.targets</ILRepackTargetsFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" PrivateAssets="all" />
    <PackageReference Include="System.Text.Json" PrivateAssets="all" />
    <PackageReference Include="ILRepack.Lib.MSBuild.Task" PrivateAssets="all" />
    <ProjectReference Include="../SpeechMessage.Localization/SpeechMessage.Localization.csproj" />
    <ProjectReference Include="../LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj" />
    <InternalsVisibleTo Include="SpeechMessage.Dynamics.Plugins.Tests" />
  </ItemGroup>
</Project>

```

## LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj
```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net48;net10.0</TargetFrameworks>
    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
    <!-- SDK 在 targets 之前決定 XML 文件輸出；嚴格開關與文件開關必須同在專案本體。 -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" />
    <InternalsVisibleTo Include="LineMessagingProcessor.Tests" />
    <InternalsVisibleTo Include="SpeechMessage.Dynamics.Plugins.Tests" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <Reference Include="System.Net.Http" />
  </ItemGroup>
</Project>

```

## SpeechMessage.Localization/SpeechMessage.Localization.csproj
```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net48;net10.0</TargetFrameworks>
    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
    <!-- SDK 在匯入 targets 前決定文件產出，必須於本體啟用以通過 SMQ0001／CS1591 守門。 -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <!-- resx 是唯一來源，由 I18nCheck 產生程式碼；不重複嵌入資源，也不建立衛星組件。 -->
    <EmbeddedResource Remove="Resources/**/*.resx" />
    <InternalsVisibleTo Include="SpeechMessage.Localization.Tests" />
  </ItemGroup>
</Project>

```

## LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj
```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net48;net10.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
    <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Newtonsoft.Json" />
    <PackageReference Include="coverlet.collector" PrivateAssets="all" />
  </ItemGroup>

  <!-- 舊 SDK 與處理器在階段一才支援 net48；保留既有四類共 33 案例在 net10 執行，
       不讓 net48 參考 net10 專案，也不修改、刪除或重命名任何既有測試。 -->
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />
    <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj" />
    <None Update="Golden/push-text.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <Reference Include="System.Net.Http" />
    <Compile Remove="LineMessagingProcessorSendMessageTests.cs;LineMessagingProcessorReliableNotificationTests.cs;LineMessagingProcessorIdentityProfileTests.cs;LineMessagingProcessorGroupRoomProfileTests.cs" />
  </ItemGroup>
</Project>

```

## Directory.Build.targets
```
<Project>
  <!-- 嚴格規則只套用到明確啟用的新專案；舊專案同時開啟會引入大量既有警告。
       SDK 在 csproj 本體之前讀取 Directory.Build.props，因此必須在 targets 才判斷
       SpeechMessageStrictCode，不能靠命令列覆寫讓測試假通過。 -->
  <PropertyGroup Condition="'$(SpeechMessageStrictCode)' == 'true'">
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisMode>Recommended</AnalysisMode>
    <!-- net48 預設只稽核直接相依；嚴格專案須在還原時稽核全部相依，與 net10 保持一致。 -->
    <NuGetAuditMode>all</NuGetAuditMode>
    <!-- SDK 只為 .NETCoreApp 自動選分析層級；net48 空值會讓 Recommended 規則集不載入。
         使用者 2026-11-03 核定：嚴格模式只在空值時明確採 latest，使雙目標確實同規則；
         net10 原有層級不變，舊專案不啟用嚴格模式也不受影響。 -->
    <AnalysisLevel Condition="'$(AnalysisLevel)' == ''">latest</AnalysisLevel>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <WarningsAsErrors>$(WarningsAsErrors);CA1307;CA1309;CA1310;RS0030</WarningsAsErrors>
  </PropertyGroup>

  <!-- 使用者 2026-11-03 核定的永久規則，適用本計畫所有階段：
       只有嚴格模式、明確測試標記、專案名稱以 .Tests 結尾三者同時成立，
       才允許計畫要求的底線測試命名；僅排除 CA1707，其他分析器與警告視為錯誤不變。
       必須集中在此設定，不得在個別測試專案重複加入其他 NoWarn。 -->
  <PropertyGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And '$(IsTestProject)' == 'true' And $([System.String]::Copy('$(MSBuildProjectName)').EndsWith('.Tests', System.StringComparison.Ordinal))">
    <NoWarn>$(NoWarn);CA1707</NoWarn>
  </PropertyGroup>

  <!-- SDK 在匯入本檔之前已依文件開關決定 DocumentationFile 與輸出複製行為。
       因此 GenerateDocumentationFile 必須在 csproj 本體與嚴格模式一同啟用，
       不能在此補設開關或改寫 SDK 私有屬性；編譯前拒絕空的實際文件路徑，避免假綠燈。 -->
  <Target Name="ValidateSpeechMessageDocumentation" BeforeTargets="CoreCompile" Condition="'$(SpeechMessageStrictCode)' == 'true'">
    <Error Condition="'$(DocumentationFile)' == ''" Code="SMQ0001" Text="嚴格專案必須在 csproj 本體同時設定 SpeechMessageStrictCode=true 與 GenerateDocumentationFile=true；目前 DocumentationFile 為空，無法驗證 XML 文件。" />
  </Target>

  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true'">
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" PrivateAssets="all" />
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
    <!-- WarningsAsErrors 不會自行啟用預設停用的規則，另以專用全域設定確保分析器真的執行。 -->
    <GlobalAnalyzerConfigFiles Include="$(MSBuildThisFileDirectory)build/StrictCode.globalconfig" />
  </ItemGroup>

  <!-- Plugin 與 Workflow 都在 D365 Sandbox 執行，額外拒絕 GDI；一般主機不受此限制。
       分析器依 BannedSymbols.txt 檔名辨識清單，故 Plugin 清單保留同名並置於獨立目錄。 -->
  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And ('$(MSBuildProjectName)' == 'SpeechMessage.Dynamics.Plugins' Or '$(MSBuildProjectName)' == 'SpeechMessage.Dynamics.Workflows')">
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)build/Plugin/BannedSymbols.txt" />
  </ItemGroup>

  <!-- 補丁只補 net48 缺少的編譯器中繼資料，不在 net10 重複宣告框架型別。
       使用連結檔確保所有雙目標程式庫共用一份定義，不建立額外狀態或組件相依。 -->
  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And '$(TargetFramework)' == 'net48'">
    <Compile Include="$(MSBuildThisFileDirectory)src-shared/Polyfills/IsExternalInit.cs" Link="Polyfills/IsExternalInit.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)src-shared/Polyfills/NotNullWhenAttribute.cs" Link="Polyfills/NotNullWhenAttribute.cs" />
  </ItemGroup>
</Project>

```

## tools/PluginPackaging/Program.cs
```
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace SpeechMessage.Tools.PluginPackaging;

/// <summary>
/// 由 PowerShell 啟動的離線中繼資料檢查器；只讀 PE，不載入或執行受檢 DLL。
/// 每次程序只處理指定的檔案，串流與讀取器由同一操作確定性釋放，不保留到 static。
/// </summary>
internal static class Program
{
    /// <summary>輸出固定 JSON 檢查結果；未完成檢查時不得以結束碼零假稱通過。</summary>
    /// <param name="args">受檢 DLL 的本機路徑，不含憑證或連線資訊。</param>
    /// <returns>零表示所有契約通過，一表示驗證拒絕。</returns>
    public static int Main(string[] args)
        => Run(args, Console.Out);

    /// <summary>由呼叫者擁有輸出 writer，讓並行離線驗證不必改全域 Console 或共享診斷緩衝。</summary>
    /// <param name="args">單一本機 DLL 路徑。</param>
    /// <param name="output">僅存活於此次操作的輸出目的地，本工具不負責關閉。</param>
    /// <returns>所有中繼資料契約通過才回傳零。</returns>
    internal static int Run(string[] args, TextWriter output)
    {
        if (args.Length != 1) return Reject("InvalidArguments", output);
        try
        {
            // 每個受檢檔案最多十六 MiB；先以檔案長度拒絕超界，再建立唯讀 PE reader。
            using var stream = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length >= 16 * 1024 * 1024) return Reject("SizeLimit", output);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return Reject("InvalidImage", output);
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) return Reject("InvalidImage", output);
            var assembly = metadata.GetAssemblyDefinition();
            var publicKey = metadata.GetBlobBytes(assembly.PublicKey);
            var identity = new AssemblyName("InspectionOnly");
            identity.SetPublicKey(publicKey);
            var strongName = publicKey.Length > 0 && identity.GetPublicKeyToken() is { Length: > 0 } &&
                (pe.PEHeaders.CorHeader!.Flags & CorFlags.StrongNameSigned) != 0;
            var targetFramework = ReadFramework(metadata, assembly);
            var references = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray();
            var allowed = references.All(name => name is "mscorlib" or "System" or "Microsoft.Xrm.Sdk" or "Microsoft.Crm.Sdk.Proxy" ||
                name.StartsWith("System.", StringComparison.Ordinal));
            var noDrawing = references.All(name => !string.Equals(name, "System.Drawing", StringComparison.Ordinal) &&
                !name.StartsWith("System.Drawing.", StringComparison.Ordinal));
            var plugin = metadata.TypeDefinitions.Any(handle =>
            {
                var type = metadata.GetTypeDefinition(handle);
                return (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public &&
                    string.Equals(metadata.GetString(type.Namespace), "SpeechMessage.Dynamics.Plugins.Diagnostics", StringComparison.Ordinal) &&
                    string.Equals(metadata.GetString(type.Name), "DiagnosticsPingPlugin", StringComparison.Ordinal);
            });
            var ok = strongName && targetFramework && allowed && noDrawing && plugin;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                ok,
                checks = new { readable = true, strongName, targetFramework, allowedReferences = allowed, noDrawing, diagnosticsPlugin = plugin, size = true },
                assemblyVersion = assembly.Version.ToString(),
                references,
                sizeBytes = stream.Length
            }));
            return ok ? 0 : 1;
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 損壞影像、不可讀檔與不合法中繼資料都是預期輸入拒絕；不輸出原始路徑或例外訊息。
            return Reject("InvalidImage", output);
        }
    }

    /// <summary>只解析 assembly 的框架屬性 blob，不反射載入待測型別或執行屬性建構式。</summary>
    /// <param name="metadata">當次 PE reader 擁有的唯讀中繼資料。</param>
    /// <param name="assembly">已確認為組件的定義。</param>
    /// <returns>只有明確 net48 TargetFrameworkAttribute 才通過。</returns>
    private static bool ReadFramework(MetadataReader metadata, AssemblyDefinition assembly)
    {
        foreach (var handle in assembly.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (!string.Equals(metadata.GetString(type.Namespace), "System.Runtime.Versioning", StringComparison.Ordinal) ||
                !string.Equals(metadata.GetString(type.Name), "TargetFrameworkAttribute", StringComparison.Ordinal)) continue;
            var blob = metadata.GetBlobReader(attribute.Value);
            return blob.ReadUInt16() == 1 && string.Equals(blob.ReadSerializedString(), ".NETFramework,Version=v4.8", StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>驗證尚未完整成功時一律失敗；固定代碼與布林欄位不包含檔案原文或簽章金鑰資訊。</summary>
    /// <param name="reason">本工具擁有的固定原因代碼。</param>
    /// <param name="output">呼叫端擁有的操作專屬輸出。</param>
    /// <returns>供 PowerShell／CI 判定的拒絕結束碼。</returns>
    private static int Reject(string reason, TextWriter output)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ok = false,
            reason,
            checks = new { readable = false, strongName = false, targetFramework = false, allowedReferences = false, noDrawing = false, diagnosticsPlugin = false, size = false }
        }));
        return 1;
    }
}

```
