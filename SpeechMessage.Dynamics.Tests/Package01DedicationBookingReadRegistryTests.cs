// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/Package01DedicationBookingReadRegistryTests.cs
// 用途：在尚未接觸 CE、網路、連線池或實際憑證前，鎖定 ORG-CALL-00041 的封閉讀取契約。
//       本檔只讀取已編譯的 registry 與工作區來源；所有資料均為固定字串或測試 GUID，
//       不建立 Data8 client、不取得 admission permit，也不保留跨測試、跨使用者或跨 Profile 的狀態。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 ORG-CALL-00041「依聯絡人讀取認獻單」的最小紅燈契約。
/// 此類別刻意以既有公開型別、列舉名稱與受限來源文字進行驗證，使尚未建立的 wire record
/// 不會造成測試專案編譯失敗。測試不會啟動 connector、建立 pool、呼叫 CE 或使用任何網路資源；
/// 因此失敗只代表缺少預定 capability／封閉 response branch，而不是外部環境不穩定。
/// </summary>
public sealed class Package01DedicationBookingReadRegistryTests
{
    private const string OperationId = "payments.dedication.retrieve.by.contact";
    private const string TemplateId = "payments.dedication.by.contact.v1";
    private const string ResponseKindName = "Package01DedicationBookingRecords";
    private const int ConservativeMaximumPageCount = 4;
    private const int ConservativeMaximumPageBytes = 64 * 1024;
    private const int ConservativeMaximumResultItemCount = 4096;

    /// <summary>
    /// 保護 deployment-owned registry 的封閉 operation schema：呼叫端只能傳入必要的 contactId GUID
    /// 與可選的 legacy 顯示名稱，不能注入 FetchXML、Entity、Profile、端點或憑證。有限的頁數、
    /// 位元組與項目上限避免大型認獻資料在單一 request、回應緩衝區或 connector lease 中無界累積。
    /// 本測試不派送任何 operation；它只讀取 process-static immutable registry snapshot。
    /// </summary>
    [Fact]
    public void ORG_CALL_00041_registry_declares_the_exact_bounded_dedication_booking_read_contract()
    {
        Package01OperationRegistry.TryGet(OperationId, out var definition).Should().BeTrue(
            because: "ORG-CALL-00041 必須以固定 capability ID 宣告，而不是由呼叫端提供查詢語意");

        definition.Should().NotBeNull();
        definition!.OperationKind.Should().Be("read");
        definition.TemplateId.Should().Be(TemplateId);
        definition.ResponseKind.ToString().Should().Be(ResponseKindName);
        definition.IdempotencyClass.Should().Be("read-only");
        definition.Parameters.Select(parameter => new
        {
            parameter.Name,
            parameter.Type,
            parameter.Required,
            parameter.EncodingContext
        })
            .Should()
            .BeEquivalentTo(
                [
                    new
                    {
                        Name = "contactId",
                        Type = "guid",
                        Required = true,
                        EncodingContext = "fetchxml-attribute-value"
                    },
                    new
                    {
                        Name = "contactName",
                        Type = "string",
                        Required = false,
                        EncodingContext = "fetchxml-attribute-value"
                    }
                ],
                options => options.WithStrictOrdering());
        definition.MaximumPageCount.Should().Be(ConservativeMaximumPageCount);
        definition.MaximumPageBytes.Should().Be(ConservativeMaximumPageBytes);
        definition.MaximumCumulativeResponseBytes.Should().Be(
            ConservativeMaximumPageCount * ConservativeMaximumPageBytes);
        definition.MaximumResultItemCount.Should().Be(ConservativeMaximumResultItemCount);
    }

    /// <summary>
    /// 保護 Gateway／Embedded response union 的 fail-closed 邊界。認獻單必須有專屬 discriminator、
    /// 專屬 collection constructor 參數與 factory，且驗證器必須要求「恰好一個」相符分支；這可阻止
    /// fee、stor lesson 或其他 response 分支混入同一 envelope 而將不同使用者或不同 operation 的資料誤送。
    /// 反射與來源檢查均為純本機讀取，沒有 response stream、cache 或外部資源需要清理。
    /// </summary>
    [Fact]
    public void Dedicated_booking_response_branch_is_exposed_and_is_required_to_be_the_only_matching_branch()
    {
        Enum.GetNames<OperationResponseKind>().Should().Contain(ResponseKindName);

        var constructor = typeof(OperationResponseData).GetConstructors().Should().ContainSingle().Subject;
        constructor.GetParameters().Select(parameter => parameter.Name).Should().Contain("dedicationBookingRecords");
        typeof(OperationResponseData).GetMethods()
            .Select(method => method.Name)
            .Should()
            .Contain("ForPackage01DedicationBookingRecords");

        var responseSource = ReadRepositorySource(
            "SpeechMessage.Dynamics.Abstractions",
            "Operations",
            "OperationResponseData.cs");
        responseSource.Should().Contain(
            "OperationResponseKind.Package01DedicationBookingRecords => branchCount == 1 && dedicationBookingRecords is not null");
    }

    /// <summary>
    /// 保護 executor 在 connector router、pool、admission permit 與 Data8 client 建立以前拒絕錯誤輸入。
    /// 測試鎖定 operation 必須進入 Data8 allowlist，並鎖定既有參數複製／驗證發生在 router resolve 之前；
    /// 因此遺漏或格式錯誤的 contactId 不得耗用另一 request、Profile 或租約的可重用資源。此為來源契約，
    /// 不會建立 connector，故不會產生 session、client 或 permit 泄漏風險。
    /// </summary>
    [Fact]
    public void Data8_executor_allowlists_the_operation_and_validates_parameters_before_pool_resolution()
    {
        var executorSource = ReadRepositorySource(
            "SpeechMessage.Dynamics.Connectors.Data8",
            "Data8ProfileOperationExecutor.cs");

        executorSource.Should().Contain("OperationIds.PaymentsDedicationRetrieveByContact => true");
        executorSource.Should().Contain("TryCopyValidatedParameters(");
        executorSource.IndexOf("TryCopyValidatedParameters(", StringComparison.Ordinal)
            .Should()
            .BeLessThan(
                executorSource.IndexOf("_connectorRouter.Resolve(profile)", StringComparison.Ordinal),
                because: "任何 contactId 缺漏或格式錯誤都必須在取得 pool 前 fail closed");
    }

    /// <summary>
    /// 保護 planned Data8 projection 不重新引入舊路徑的逐筆 Retrieve N+1。專屬 method 必須存在，
    /// 且其封閉方法本文只能以 RetrieveMultiple 讀取固定 projection；不得以單筆 Retrieve 補查資料。
    /// 來源檢查僅讀取 repository 中的 C# 文字，不會執行 CRM、網路、連線池或背景工作。
    /// </summary>
    [Fact]
    public void Dedicated_booking_Data8_read_uses_only_bounded_RetrieveMultiple_without_N_plus_one_Retrieve()
    {
        var operationsSource = ReadRepositorySource(
            "SpeechMessage.Dynamics.Connectors.Data8",
            "Package01Data8ReadOperations.cs");
        var methodBody = ExtractMethodBody(
            operationsSource,
            "private static OperationResponseData ExecuteDedicationBookingByContact(");

        methodBody.Should().Contain("RetrieveMultiple(");
        methodBody.Should().NotContain(".Retrieve(");
    }

    /// <summary>
    /// 由目前測試執行目錄向上尋找 solution root，再以固定相對路徑讀取指定來源。
    /// 路徑不是使用者輸入、Profile、端點或資料庫定位子；若工作區形狀不完整即立即失敗，避免測試
    /// 靜默掃描到其他 checkout。File.ReadAllText 的 stream 由 framework 在同步讀取結束時釋放。
    /// </summary>
    /// <param name="relativeSegments">solution root 下的固定、allowlisted 相對路徑片段。</param>
    /// <returns>以 UTF-8 預設偵測讀入的單一測試擁有來源快照。</returns>
    private static string ReadRepositorySource(params string[] relativeSegments)
    {
        var root = FindRepositoryRoot();
        var path = relativeSegments.Aggregate(root, Path.Combine);
        File.Exists(path).Should().BeTrue(because: $"來源契約檔案必須存在：{path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 從目前執行目錄往上尋找包含 solution 檔的唯一工作區根目錄。此 helper 不快取結果，確保平行測試
    /// 不共用可變路徑狀態；找不到時立即拋出固定例外，避免讀取到工作區外的同名原始碼。
    /// </summary>
    /// <returns>目前測試工作區的絕對根路徑。</returns>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("找不到 SpeechMessageProducts.sln，無法驗證受限來源契約。");
    }

    /// <summary>
    /// 擷取指定方法的平衡大括號本文，使 N+1 掃描不會被同一檔案其他無關 operation 影響。
    /// 此 helper 僅接受本測試常數提供的 method 名稱；若 planned method 尚未落地就立即失敗，正是本輪 RED
    /// 的預期訊號。它不保存來源快照，也不管理外部資源。
    /// </summary>
    /// <param name="source">已在本測試方法範圍讀入的單一來源快照。</param>
    /// <param name="methodName">預期為固定 server-owned projection 的方法名稱。</param>
    /// <returns>包含方法大括號的最小來源片段。</returns>
    private static string ExtractMethodBody(string source, string methodName)
    {
        var methodIndex = source.IndexOf(methodName, StringComparison.Ordinal);
        methodIndex.Should().BeGreaterOrEqualTo(0, because: "專屬 Data8 projection method 必須存在");

        var openingBrace = source.IndexOf('{', methodIndex);
        openingBrace.Should().BeGreaterOrEqualTo(0, because: "專屬 Data8 projection method 必須有本文");

        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[openingBrace..(index + 1)];
            }
        }

        throw new InvalidOperationException("專屬 Data8 projection method 的大括號不平衡。");
    }
}
