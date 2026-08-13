// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/AppNamedListCatalogReadRegistryTests.cs
// 用途：在不建立 CE、Data8 connector、連線池或任何外部資源前，守護 ORG-CALL-00014 的封閉名單目錄契約。
//
// 本測試只讀取 process-static registry、反射型別與 repository source。固定 GUID、operation ID 與模板名稱皆為
// 合約測試資料，沒有使用者、Profile、credential、session、Entity 或可重用 connection；測試結束後不保留
// cancellation registration、timer、stream、handle 或跨案例 mutable state。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 ORG-CALL-00014「App-named 名單目錄」必須先形成 server-owned、zero-parameter、bounded 的資料邊界。
/// 此類別不派送 operation；紅燈只能表示編譯後 allowlist／封閉 response contract 尚未建立，而不是外部 CE
/// 或 deployment 環境問題。未來實作仍須由另一個受權 consumer child 負責 authorization、feature gate 與 rollout。
/// </summary>
public sealed class AppNamedListCatalogReadRegistryTests
{
    private const string OperationId = "list.catalog.retrieve.app.named";
    private const string TemplateId = "list.catalog.appnamed.v1";
    private const string ResponseKindName = "AppNamedListCatalogRecords";

    /// <summary>
    /// 保護 registry 僅接受零 caller parameter 的固定名單目錄 operation。故障注入是目前尚未登錄的 ID；
    /// 決定性斷言要求固定模板、readonly discriminator 和既有四頁／位元組／項目上限，避免 caller 把篩選、
    /// 排序、Entity、Profile、endpoint 或 credential 注入 connector、pool 或另一個 request 的隔離範圍。
    /// </summary>
    [Fact]
    public void ORG_CALL_00014_registry_declares_the_exact_zero_parameter_bounded_catalog_contract()
    {
        Package01OperationRegistry.TryGet(OperationId, out var definition).Should().BeTrue(
            because: "ORG-CALL-00014 必須有固定 operation，而不能退回 generic CRM list query");

        definition.Should().NotBeNull();
        definition!.OperationKind.Should().Be("read");
        definition.TemplateKind.Should().Be("fetchxml");
        definition.TemplateId.Should().Be(TemplateId);
        definition.ResponseKind.ToString().Should().Be(ResponseKindName);
        definition.IdempotencyClass.Should().Be("read-only");
        definition.Parameters.Should().BeEmpty("目錄篩選與排序完全由伺服器固定");
        definition.MaximumPageCount.Should().Be(4);
        definition.MaximumPageBytes.Should().Be(64 * 1024);
        definition.MaximumCumulativeResponseBytes.Should().Be(4 * 64 * 1024);
        definition.MaximumResultItemCount.Should().Be(4096);
    }

    /// <summary>
    /// 保護成功 envelope 必須有 catalog 專屬 discriminator、collection constructor parameter 與 factory，並由
    /// union validator 要求恰好一個相符 branch。故障注入是尚未存在的型別/成員；決定性斷言防止 fee、lesson、
    /// authentication 或 raw CRM response 被誤當成 catalog rows，從而避免跨 capability／跨請求資料混合。
    /// </summary>
    [Fact]
    public void Catalog_response_branch_is_exposed_and_must_be_the_only_matching_branch()
    {
        Enum.GetNames<OperationResponseKind>().Should().Contain(ResponseKindName);

        var constructor = typeof(OperationResponseData).GetConstructors().Should().ContainSingle().Subject;
        constructor.GetParameters().Select(parameter => parameter.Name).Should().Contain("appNamedListCatalogRecords");
        typeof(OperationResponseData).GetMethods()
            .Select(method => method.Name)
            .Should()
            .Contain("ForAppNamedListCatalogRecords");

        var responseSource = ReadRepositorySource(
            "SpeechMessage.Dynamics.Abstractions",
            "Operations",
            "OperationResponseData.cs");
        responseSource.Should().Contain(
            "OperationResponseKind.AppNamedListCatalogRecords => branchCount == 1 && appNamedListCatalogRecords is not null");
    }

    /// <summary>
    /// 保護 catalog wire row 必須是只含五個允許 pure scalar 的不可變投影。故障注入是尚未提供的 record 型別；
    /// 決定性斷言要求 list ID、可空名稱、created-from option、UTC last-used 與 purpose 的精確 public 型別，避免
    /// connector 日後把 CRM <c>Entity</c>、formatted-value 字典、profile、cookie 或可變集合留在 response 中。
    /// 本測試只反射 Abstractions assembly 的 metadata，不建立 CE 服務、connector lease、stream、cache、session 或
    /// background work；反射結果於方法結束即失去參考，不會成為跨案例或跨使用者的資料 owner。
    /// </summary>
    [Fact]
    public void Catalog_wire_record_has_only_the_allowlisted_immutable_scalar_contract()
    {
        var recordType = typeof(OperationResponseData).Assembly.GetType(
            "SpeechMessage.Dynamics.Abstractions.Operations.AppNamedListCatalogRecord");

        recordType.Should().NotBeNull(
            because: "catalog row 必須有封閉的 immutable wire record，不能將 CRM Entity 或通用 object 穿越邊界");

        var properties = recordType!
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new { property.Name, property.PropertyType })
            .ToArray();

        properties.Should().Equal(
            new { Name = "CreatedFromCodeOption", PropertyType = typeof(int?) },
            new { Name = "LastUsedOn", PropertyType = typeof(DateTimeOffset?) },
            new { Name = "ListId", PropertyType = typeof(Guid) },
            new { Name = "ListName", PropertyType = typeof(string) },
            new { Name = "Purpose", PropertyType = typeof(string) });
    }

    /// <summary>
    /// 從 test output 向上定位 repository 並在 using scope 內讀取來源。檔案 bytes 不會進入產品 cache、session 或
    /// background work；這個 helper 沒有持久 handle，故不會把測試路徑或內容保留到另一個 profile/request。
    /// </summary>
    /// <param name="segments">由 solution root 起算的安全相對路徑片段。</param>
    /// <returns>本次 assertion 專屬的 UTF-8 source text。</returns>
    private static string ReadRepositorySource(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return File.ReadAllText(Path.Combine([directory.FullName, .. segments]));
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到含有 SpeechMessageProducts.sln 的 repository root。");
    }
}
