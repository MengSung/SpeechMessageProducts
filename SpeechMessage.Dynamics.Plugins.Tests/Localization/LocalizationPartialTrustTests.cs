/*
 * 檔案整體說明
 *
 * 目的與定位：將 P5-04 三個國際化外殼加入最小部分信任回歸，保護真外殼、純規則、
 * SHA-256、待辦序列化與 Shadow recorder 在無私有反射／網路／寫檔權限下可執行。
 * 主要功能與流程：父案例只授予 Execution 與測試閉包唯讀，SDK StrongName 為唯一平台
 * full trust 例外；子域自行建立合成 SDK、context 及無效 token envelope，分別驗證文字
 * Update、會友 alias Push 與教會待辦 Create 的完整 end、正式身分、零真寫入及敏感值零。
 * 使用邊界與維護：每案域最多執行一次，fixture 在方法返回前 using 排空並清除回呼，
 * 父 finally 無論成功或失敗均卸載域；跨域只回傳純結果字串，沒有 request、SDK 或秘密。
 * 不提升產品組件權限，不以 FullTrust 成功冒充 CAS，亦不把離線通過視為 D365 實機結果。
 */
using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using System.Security.Policy;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using Xunit;
namespace SpeechMessage.Dynamics.Plugins.Tests.Localization;
/// <summary>三個模組各自建立並卸載 CAS 域，沒有繼承父域完整信任的產品服務。</summary>
public sealed class LocalizationPartialTrustTests
{
    /// <summary>沿用固定做法同名測試，以實際模組案例補足國際化三條受限執行路徑。</summary>
    [Theory]
    [InlineData("LocalizedText")]
    [InlineData("MemberLanguage")]
    [InlineData("ChurchLocale")]
    public void PartialTrust_ShellShadowChain_Runs(string module)
    {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        var permissions = new PermissionSet(PermissionState.None);
        permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
        permissions.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read | FileIOPermissionAccess.PathDiscovery, root));
        Assert.Null(permissions.GetPermission(typeof(ReflectionPermission)));
        var sdk = typeof(IPlugin).Assembly.Evidence.GetHostEvidence<StrongName>();
        Assert.NotNull(sdk);
        var domain = AppDomain.CreateDomain("P5-Localization-" + Guid.NewGuid().ToString("N"), null,
            new AppDomainSetup { ApplicationBase = root, ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile },
            permissions, sdk);
        try
        {
            var probe = (LocalizationPartialTrustProbe)domain.CreateInstanceAndUnwrap(
                typeof(LocalizationPartialTrustProbe).Assembly.FullName, typeof(LocalizationPartialTrustProbe).FullName);
            Assert.Equal("PASS", probe.Run(module));
        }
        finally { AppDomain.Unload(domain); }
    }
}
/// <summary>測試專用跨域入口，子域內建立真外殼與所有資料；只存一次執行旗標直到父域卸載。</summary>
public sealed class LocalizationPartialTrustProbe : MarshalByRefObject
{
    /// <summary>限制一次執行，不跨測試或重新借用已結束 owner。</summary>
    private bool hasRun;
    /// <summary>在最小 CAS 權限下實際執行指定模組；任何不完整影子、真寫入或敏感內容都拒絕成功。</summary>
    /// <param name="module">測試固定的三種模組代號，不是產品輸入。</param>
    /// <returns>全部產品鏈與排空斷言成立才回 PASS，失敗回傳合成案例診斷。</returns>
    public string Run(string module)
    {
        try
        {
            Require(!hasRun && !AppDomain.CurrentDomain.IsFullyTrusted, "A restricted one-shot domain is required.");
            hasRun = true;
            Require(!GetType().Assembly.IsFullyTrusted && !typeof(LocalizedTextChangedPlugin).Assembly.IsFullyTrusted &&
                !typeof(MemberRichMenuService).Assembly.IsFullyTrusted, "Product assemblies cannot receive full trust.");
            using var fixture = new LocalizationFixture();
            switch (module)
            {
                case "LocalizedText":
                    var id = Guid.NewGuid();
                    var context = new RemoteExecutionContext
                    {
                        Depth = 2, Stage = 40, Mode = 1, MessageName = "Create", PrimaryEntityName = "new_localizedtext",
                        PrimaryEntityId = id, OrganizationId = Guid.NewGuid(), OrganizationName = "synthetic",
                        UserId = Guid.NewGuid(), CorrelationId = Guid.NewGuid()
                    };
                    context.InputParameters["Target"] = new Entity("new_localizedtext", id)
                    { ["new_accountid"] = new EntityReference("account", fixture.ChurchId) };
                    fixture.Execute(new LocalizedTextChangedPlugin("{\"mode\":\"Shadow\"}", ""), context);
                    Require((string?)fixture.Intents()[0]["operation"] == "Update" &&
                        (string?)fixture.Intents()[0]["id"] == fixture.ChurchId.ToString("D"), "The text event lost its church.");
                    break;
                case "MemberLanguage":
                    fixture.Execute(new MemberLanguageChangedPlugin("{\"mode\":\"Shadow\"}", ""), fixture.MemberContext(2));
                    Require((string?)fixture.Intents()[0]["operation"] == "Push" &&
                        (string?)fixture.Intents()[0]["contactId"] == fixture.ContactId.ToString("D") &&
                        (string?)fixture.Intents()[0]["contentSha256"] == "a39f9871dbf924231d791e767770c0fafed8498a0c741db408c2f525adba7da3",
                        "The member alias intent is incomplete.");
                    break;
                case "ChurchLocale":
                    fixture.Execute(new ChurchLocaleChangedPlugin("{\"mode\":\"Shadow\"}", ""), fixture.ChurchContext(depth: 2));
                    Require((string?)fixture.Intents()[0]["operation"] == "Create" &&
                        (string?)fixture.Intents()[0]["entity"] == "new_outboundtask" &&
                        (string?)fixture.Intents()[0]["fields"]?["new_accountid"]?["id"] == fixture.ChurchId.ToString("D"),
                        "The task intent is incomplete.");
                    break;
                default: throw new InvalidOperationException("Unknown synthetic module.");
            }
            var intents = fixture.Intents();
            Require(intents.Length == 2 && (bool?)intents[1]["end"] == true && (int?)intents[1]["count"] == 1 &&
                intents.All(intent => intent["overflow"] is null), "A complete single-intent trace is required.");
            Require(fixture.Service.WriteRecords.Count == 0 && fixture.TaskCount == 0, "Shadow wrote to the borrowed SDK.");
            var trace = string.Join("", fixture.Trace.Entries);
            Require(trace.IndexOf("invalid-envelope", StringComparison.Ordinal) < 0 &&
                trace.IndexOf("U" + new string('a', 32), StringComparison.Ordinal) < 0 &&
                fixture.Trace.Entries.Sum(line => line.Length + 2) <= 8000, "Trace exposed secrets or exceeded its budget.");
            fixture.Dispose();
            Require(fixture.Service.EntityCount == 0 && fixture.Service.WriteRecords.Count == 0 &&
                fixture.Service.RetrieveHandler is null, "The fixture did not drain its retained state.");
            return "PASS";
        }
        catch (Exception exception) { return exception.ToString(); }
    }
    /// <summary>受限域不依賴 xUnit 斷言反射，錯誤訊息只有固定合成契約。</summary>
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
