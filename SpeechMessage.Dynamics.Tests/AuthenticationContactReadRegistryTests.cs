// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/AuthenticationContactReadRegistryTests.cs
// 用途：先以 TDD 鎖定 ORG-CALL-00055／00056 的認證聯絡人唯讀 capability。測試僅讀取
//       immutable registry 與目前 worktree 的受限來源文字，絕不建立 CE、Data8 connector、
//       HTTP、Session、credential、快取或背景工作。
//
// 隔離與生命週期契約：
// 1. account／LINE ID 僅是固定 operation 的 lookup 值；registry 不得接受 caller 指定
//    FetchXML、entity、欄位、profile、organization、endpoint 或 credential。
// 2. Data8 查詢在取得 router、pool 或 connector lease 前驗證輸入，且 LINE 查詢固定 active
//    statecode；zero／duplicate 都由上層 closed result 分類，不能以 TopCount 猜選。
// 3. 本檔讀取的 source snapshot 只活在單一測試呼叫堆疊；File.ReadAllText 完成即由 framework
//    關閉檔案 handle，不保留任何 contact、secret 或 response 到跨測試狀態。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證認證聯絡人 read boundary 的 server-owned registry 與 Data8 source 契約。
/// 這些是 fail-first 測試：當 ORG-CALL-00055／00056 尚未登錄時，失敗只表示缺少預定的
/// local-only typed boundary，不能被誤判為 CE、host 或 feature-gate 已啟用的證據。
/// </summary>
public sealed class AuthenticationContactReadRegistryTests
{
    private const string AccountOperationId = "auth.contact.retrieve.by.account";
    private const string LineIdOperationId = "auth.contact.retrieve.by.lineid";

    /// <summary>
    /// 保護兩個 operation 都必須由 deployment-owned registry 以固定 ID、read-only idempotency、
    /// bounded response 與唯一 lookup parameter 宣告。故障注入是尚未存在的 registry row；決定性斷言
    /// 是 caller 無法透過 parameters 攜帶 password、secret、profile 或任意 query，因此未來登入遷移不會
    /// 將 credential 或 routing authority 交給 ProductClient。
    /// </summary>
    [Fact]
    public void ORG_CALL_00055_and_00056_declare_only_the_fixed_lookup_inputs()
    {
        Package01OperationRegistry.TryGet(AccountOperationId, out var accountDefinition).Should().BeTrue(
            because: "ORG-CALL-00055 必須先登錄 server-owned account lookup capability");
        Package01OperationRegistry.TryGet(LineIdOperationId, out var lineIdDefinition).Should().BeTrue(
            because: "ORG-CALL-00056 必須先登錄 server-owned LINE ID lookup capability");

        accountDefinition.Should().NotBeNull();
        accountDefinition!.OperationKind.Should().Be("read");
        accountDefinition.TemplateId.Should().Be("auth.contact.by.account.v1");
        accountDefinition.ResponseKind.ToString().Should().Be("AuthenticationContactReadRecords");
        accountDefinition.IdempotencyClass.Should().Be("read-only");
        accountDefinition.MaximumResultItemCount.Should().Be(2,
            because: "帳號 contact lookup 的 registry 上限必須與固定 TopCount=2 和 wire envelope 完全一致");
        accountDefinition.Parameters.Select(parameter => new
        {
            parameter.Name,
            parameter.Type,
            parameter.Required,
            parameter.EncodingContext
        }).Should().BeEquivalentTo(
            [
                new
                {
                    Name = "accountLookupValue",
                    Type = "string",
                    Required = true,
                    EncodingContext = "fetchxml-attribute-value"
                }
            ],
            options => options.WithStrictOrdering());

        lineIdDefinition.Should().NotBeNull();
        lineIdDefinition!.OperationKind.Should().Be("read");
        lineIdDefinition.TemplateId.Should().Be("auth.contact.by.lineid.v1");
        lineIdDefinition.ResponseKind.ToString().Should().Be("AuthenticationContactReadRecords");
        lineIdDefinition.IdempotencyClass.Should().Be("read-only");
        lineIdDefinition.MaximumResultItemCount.Should().Be(2,
            because: "LINE contact lookup 的 registry 上限必須與固定 TopCount=2 和 wire envelope 完全一致");
        lineIdDefinition.Parameters.Select(parameter => new
        {
            parameter.Name,
            parameter.Type,
            parameter.Required,
            parameter.EncodingContext
        }).Should().BeEquivalentTo(
            [
                new
                {
                    Name = "lineIdLookupValue",
                    Type = "string",
                    Required = true,
                    EncodingContext = "fetchxml-attribute-value"
                }
            ],
            options => options.WithStrictOrdering());
    }

    /// <summary>
    /// 保護 Data8 executor 在 connector router／pool 解析之前 allowlist 兩個 operation，並要求兩個新 projection
    /// 都採單一 bounded RetrieveMultiple。LINE method 額外固定 new_lineid 與 active statecode；故障注入是
    /// 缺少 method、遺漏固定條件或使用單筆 Retrieve，決定性斷言避免零筆／重複資料被不安全地補查或猜選。
    /// </summary>
    [Fact]
    public void Data8_projection_allowlists_both_reads_and_keeps_line_lookup_active_and_bounded()
    {
        var executorSource = ReadRepositorySource(
            "SpeechMessage.Dynamics.Connectors.Data8",
            "Data8ProfileOperationExecutor.cs");
        executorSource.Should().Contain("OperationIds.AuthenticationContactRetrieveByAccount => true");
        executorSource.Should().Contain("OperationIds.AuthenticationContactRetrieveByLineId => true");
        executorSource.IndexOf("if (!TryCreateConnectorOperation(", StringComparison.Ordinal)
            .Should()
            .BeLessThan(
                executorSource.IndexOf("_connectorRouter.Resolve(profile)", StringComparison.Ordinal),
                because: "非法 lookup 不得在取得可重用 connector 資源後才被拒絕");

        var readsSource = ReadRepositorySource(
            "SpeechMessage.Dynamics.Connectors.Data8",
            "Package01Data8ReadOperations.cs");
        var accountMethod = ExtractMethodBody(readsSource, "ExecuteAuthenticationContactByAccount(");
        var lineIdMethod = ExtractMethodBody(readsSource, "ExecuteAuthenticationContactByLineId(");

        accountMethod.Should().Contain("RetrieveMultiple(");
        accountMethod.Should().NotContain(".Retrieve(");
        lineIdMethod.Should().Contain("RetrieveMultiple(");
        lineIdMethod.Should().NotContain(".Retrieve(");
        lineIdMethod.Should().Contain("new_lineid");
        lineIdMethod.Should().Contain("statecode");
        lineIdMethod.Should().Contain("statecode eq 0");
    }

    /// <summary>
    /// 保護 wire union 與本次新增的 authentication source 都沒有 credential projection。故障注入是 response
    /// branch 缺少 dedicated discriminator／factory，或新 source 讀入 new_app_pass；決定性斷言要求 wire 與 DTO
    /// source 完全不攜帶該 secret，防止明文密碼因 logging、JSON、cache 或另一 request 的 DTO 被保留。
    /// </summary>
    [Fact]
    public void Authentication_wire_and_DTO_sources_expose_a_dedicated_branch_without_the_legacy_secret_field()
    {
        var responseSource = ReadRepositorySource(
            "SpeechMessage.Dynamics.Abstractions",
            "Operations",
            "OperationResponseData.cs");
        responseSource.Should().Contain("AuthenticationContactReadRecords");
        responseSource.Should().Contain("ForAuthenticationContactReadRecords");
        responseSource.Should().Contain("authenticationContactReadRecords");

        var wireSource = ReadRepositorySource(
            "SpeechMessage.Dynamics.Abstractions",
            "Operations",
            "AuthenticationContactReadRecord.cs");
        var dtoSource = ReadRepositorySource(
            "SpeechMessage.Dynamics.ProductClient",
            "Authentication",
            "AuthenticationContactReadDto.cs");
        var clientSource = ReadRepositorySource(
            "SpeechMessage.Dynamics.ProductClient",
            "Authentication",
            "AuthenticationContactReadClient.cs");

        foreach (var source in new[] { wireSource, dtoSource, clientSource })
        {
            source.Should().NotContain(
                "new_app_pass",
                because: "新增認證 read wire／DTO／client 不得投影、記錄或保留 legacy secret");
        }
    }

    /// <summary>
    /// 以 solution root 加上 compile-time allowlisted 片段讀取 source。此 helper 不接受 browser、environment、
    /// profile 或測試資料提供的路徑；讀取完成立即釋放檔案 handle，沒有 cache、watcher 或跨案例保留狀態。
    /// </summary>
    /// <param name="relativeSegments">solution root 下的固定來源相對路徑。</param>
    /// <returns>目前 test 呼叫專屬的短生命週期來源文字快照。</returns>
    private static string ReadRepositorySource(params string[] relativeSegments)
    {
        var path = relativeSegments.Aggregate(FindRepositoryRoot(), Path.Combine);
        File.Exists(path).Should().BeTrue(because: $"受限來源檔案必須存在：{path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 從測試輸出目錄往上尋找唯一 solution root。每次查詢都重新計算而不建立 static cache，避免平行測試
    /// 重用另一 worktree 的路徑；找不到根目錄即 fail closed，不能悄悄掃描使用者機器上的其他 checkout。
    /// </summary>
    /// <returns>包含 SpeechMessageProducts.sln 的目前 worktree 根路徑。</returns>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("找不到 SpeechMessageProducts.sln，無法驗證認證 read source contract。");
    }

    /// <summary>
    /// 擷取指定方法的平衡大括號本文，避免其他 Data8 operation 的字串意外使本測試通過。methodName 只來自
    /// 本檔 compile-time 常數；缺少 projection method 是預期 RED，且 helper 不保存 source 或外部資源。
    /// </summary>
    /// <param name="source">單一測試範圍內讀取的 source snapshot。</param>
    /// <param name="methodName">預期存在的固定 Data8 projection 方法標記。</param>
    /// <returns>從標記至相符結束大括號的 method source。</returns>
    private static string ExtractMethodBody(string source, string methodName)
    {
        var methodIndex = source.IndexOf(methodName, StringComparison.Ordinal);
        methodIndex.Should().BeGreaterOrEqualTo(0, because: "固定 authentication Data8 projection method 必須存在");

        var openingBrace = source.IndexOf('{', methodIndex);
        openingBrace.Should().BeGreaterOrEqualTo(0, because: "projection method 必須有本文");
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

        throw new InvalidOperationException("authentication Data8 projection method 的大括號不平衡。");
    }
}
