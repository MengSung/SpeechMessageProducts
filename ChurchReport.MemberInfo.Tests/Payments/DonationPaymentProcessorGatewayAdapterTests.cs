// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorGatewayAdapterTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DonationPaymentProcessorGatewayAdapterTests、class RecordingPaymentGateway、class SingleServiceProvider、class TestSession、class ThrowingToolUtilityProvider
// 主要成員：ChurchReport_controllers_do_not_accept_legacy_ipayment_in_constructors、Donation_payment_processor_does_not_store_legacy_ipayment_provider、Donation_payment_processor_constructors_require_gateway_create_adapter、Qpay_manager_and_context_constructors_accept_gateway_create_adapter、ContextDictionary_passes_gateway_create_adapter_from_request_services、CreateDonationPaymentOrder_uses_gateway_adapter_when_available、CreateOrderATM_uses_gateway_adapter_when_available、Qpay_manager_order_maintenance_fails_closed_without_legacy_toolkit、ChurchReport_assembly_does_not_expose_legacy_payment_toolkits、Donation_product_workflow_processors_do_not_accept_legacy_order_pay_models
// 引用命名空間：System.Reflection、System.Runtime.CompilerServices、ChurchReport.Models、ChurchReport.Payments、ChurchReport.Tools、ChurchReport.WebServiceConnector、FluentAssertions、Microsoft.AspNetCore.Http
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Reflection;
using System.Runtime.CompilerServices;
using ChurchReport.Models;
using ChurchReport.Payments;
using ChurchReport.Tools;
using ChurchReport.WebServiceConnector;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Models;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.DependencyInjection;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證 ChurchReport 奉獻付款流程已透過產品層 adapter 接到共用金流核心，
/// 並保留舊 QPay 命名入口的相容性。
/// </summary>
public sealed class DonationPaymentProcessorGatewayAdapterTests
{
    [Fact]
    public void ChurchReport_controllers_do_not_accept_legacy_ipayment_in_constructors()
    {
        const string legacyPaymentTypeName = "ChurchReport.Tools.IPayment";
        var offenders = typeof(DonationPaymentManager).Assembly
            .GetTypes()
            .Where(type => type.Namespace != null && type.Namespace.StartsWith("ChurchReport.Controllers"))
            .Where(type => type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .SelectMany(type => type
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(constructor => new
                {
                    Type = type,
                    Constructor = constructor
                }))
            .Where(item => item.Constructor
                .GetParameters()
                .Any(parameter => parameter.ParameterType.FullName == legacyPaymentTypeName))
            .Select(item => item.Type.FullName)
            .Distinct()
            .OrderBy(name => name)
            .ToArray();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void Donation_payment_processor_does_not_store_legacy_ipayment_provider()
    {
        const string legacyPaymentTypeName = "ChurchReport.Tools.IPayment";
        var processorType = typeof(DonationPaymentProcessor);

        var fieldNames = processorType
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType.FullName == legacyPaymentTypeName)
            .Select(field => field.Name)
            .OrderBy(name => name)
            .ToArray();
        var propertyNames = processorType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => property.PropertyType.FullName == legacyPaymentTypeName)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();
        var methodNames = processorType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.ReturnType.FullName == legacyPaymentTypeName)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        fieldNames.Should().BeEmpty();
        propertyNames.Should().BeEmpty();
        methodNames.Should().BeEmpty();
    }

    [Fact]
    public void Donation_payment_processor_constructors_require_gateway_create_adapter()
    {
        var adapterType = typeof(DonationPaymentCreateGatewayAdapter);
        var adapterParameters = typeof(DonationPaymentProcessor)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => parameter.ParameterType == adapterType)
            .ToArray();

        adapterParameters.Should().NotBeEmpty();
        adapterParameters.Should().OnlyContain(parameter => !parameter.HasDefaultValue);
    }

    [Fact]
    public void Qpay_manager_and_context_constructors_accept_gateway_create_adapter()
    {
        ConstructorHasAdapter(typeof(DonationPaymentManager)).Should().BeTrue();
        ConstructorHasAdapter(typeof(InMemoryDataContextSmallGroup)).Should().BeTrue();
    }

    [Fact]
    public void ContextDictionary_passes_gateway_create_adapter_from_request_services()
    {
        const string sessionId = "payment-adapter-session";
        ContextDictionary.Remove(sessionId);
        var adapter = CreateAdapter(new RecordingPaymentGateway(new PaymentCreateResult()));
        var httpContext = new DefaultHttpContext
        {
            Session = new TestSession(sessionId),
            RequestServices = new SingleServiceProvider(adapter)
        };
        var accessor = new HttpContextAccessor
        {
            HttpContext = httpContext
        };
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var context = ContextDictionary.GetInMemoryDataContextSmallGroup(
            accessor,
            memoryCache,
            new ThrowingToolUtilityProvider());

        try
        {
            var field = typeof(InMemoryDataContextSmallGroup).GetField(
                "m_DonationPaymentCreateGatewayAdapter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            field!.GetValue(context).Should().BeSameAs(adapter);
        }
        finally
        {
            ContextDictionary.Remove(sessionId);
        }
    }

    [Fact]
    public async Task CreateDonationPaymentOrder_uses_gateway_adapter_when_available()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCreateResult
        {
            Status = PaymentStatus.Pending,
            ProductOrderId = "C20260626112233",
            ProviderOrderRef = "TS123456789",
            PaymentPageUrl = "https://pay.example.test/card",
            ProviderData = new Dictionary<string, string>
            {
                ["shop_no"] = "NA0149_001"
            }
        });
        var processor = CreateProcessor(CreateAdapter(gateway));

        var order = await InvokeCreateDonationPaymentOrder(processor);

        gateway.CreatePaymentCallCount.Should().Be(1);
        gateway.LastCreateRequest.Should().NotBeNull();
        gateway.LastCreateRequest!.ProviderHint.Should().BeNull();
        gateway.LastCreateRequest.ProductOrderId.Should().Be("C20260626112233");
        gateway.LastCreateRequest.Metadata["Param1"].Should().Be("fee-id");
        gateway.LastCreateRequest.Metadata["Param2"].Should().Be("Jesus");
        gateway.LastCreateRequest.Metadata["Param3"].Should().Be("fee");
        gateway.LastCreateRequest.Metadata["UserId"].Should().Be("Grace");
        gateway.LastCreateRequest.Customer.Name.Should().Be("Grace");
        order.Status.Should().Be("S");
        order.OrderNo.Should().Be("C20260626112233");
        order.TSNo.Should().Be("TS123456789");
        order.CardParam.CardPayURL.Should().Be("https://pay.example.test/card");
    }

    [Fact]
    public async Task CreateOrderATM_uses_gateway_adapter_when_available()
    {
        var gateway = new RecordingPaymentGateway(new PaymentCreateResult
        {
            Status = PaymentStatus.Pending,
            ProductOrderId = "A20260626112233",
            ProviderOrderRef = "TSATM123456",
            PaymentPageUrl = "https://pay.example.test/atm",
            ProviderData = new Dictionary<string, string>
            {
                ["shop_no"] = "NA0149_001",
                ["atm_pay_no"] = "12345678901234"
            }
        });
        var processor = CreateProcessor(CreateAdapter(gateway));

        var order = await processor.CreateOrderATM(
            800,
            "ATM payment",
            "20260626112233",
            "fee-id");

        gateway.CreatePaymentCallCount.Should().Be(1);
        gateway.LastCreateRequest.Should().NotBeNull();
        gateway.LastCreateRequest!.ProductOrderId.Should().Be("A20260626112233");
        gateway.LastCreateRequest.PaymentMethod.Should().Be("A");
        gateway.LastCreateRequest.Metadata["Param1"].Should().Be("fee-id");
        gateway.LastCreateRequest.Metadata["Param2"].Should().Be("Jesus");
        gateway.LastCreateRequest.Metadata["Param3"].Should().Be("fee");
        order.Status.Should().Be("S");
        order.OrderNo.Should().Be("A20260626112233");
        order.TSNo.Should().Be("TSATM123456");
        order.ATMParam.Should().NotBeNull();
        order.ATMParam.AtmPayNo.Should().Be("12345678901234");
        order.ATMParam.WebAtmURL.Should().Be("https://pay.example.test/atm");
    }

    [Fact]
    public async Task Qpay_manager_order_maintenance_fails_closed_without_legacy_toolkit()
    {
        var manager = (DonationPaymentManager)RuntimeHelpers.GetUninitializedObject(typeof(DonationPaymentManager));

        var result = await manager.OrderMaintain("C20260626112233", "E");

        result.Status.Should().Be("F");
        result.OrderNo.Should().Be("C20260626112233");
        result.Command.Should().Be("E");
        result.Description.Should().Contain("not part of the reusable payment core");
    }

    [Fact]
    public void ChurchReport_assembly_does_not_expose_legacy_payment_toolkits()
    {
        var legacyTypeNames = new[]
        {
            "ChurchReport.Tools.IPayment",
            "ChurchReport.Tools.IQPayToolkit",
            "ChurchReport.Tools.QPayToolkit",
            "ChurchReport.Tools.QPayToolkitWrapper",
            "ChurchReport.Tools.MyPayToolkit",
            "ChurchReport.Tools.MyPayToolkitWrapper",
            "ChurchReport.Tools.TspgToolkit",
            "ChurchReport.Tools.TspgToolkitWrapper",
            "ChurchReport.Tools.TSPGWebhookHandler"
        };

        var assembly = typeof(DonationPaymentManager).Assembly;
        var presentLegacyTypes = legacyTypeNames
            .Where(typeName => assembly.GetType(typeName, throwOnError: false) != null)
            .OrderBy(typeName => typeName)
            .ToArray();

        presentLegacyTypes.Should().BeEmpty();
    }

    [Fact]
    public void Donation_product_workflow_processors_do_not_accept_legacy_order_pay_models()
    {
        var processorTypes = new[]
        {
            typeof(DonationFeePaymentProcessor),
            typeof(RecurringDonationPaymentProcessor)
        };

        var offendingMethods = processorTypes
            .SelectMany(type => type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Select(method => new
                {
                    Type = type,
                    Method = method
                }))
            .Where(item => item.Method
                .GetParameters()
                .Any(parameter => parameter.ParameterType.FullName == "QPay.Domain.QryOrderPay"))
            .Select(item => item.Type.FullName + "." + item.Method.Name)
            .OrderBy(name => name)
            .ToArray();

        offendingMethods.Should().BeEmpty();
    }

    [Fact]
    public void ChurchReport_assembly_does_not_define_qpay_domain_namespace()
    {
        var qpayDomainTypes = typeof(DonationPaymentManager).Assembly
            .GetTypes()
            .Where(type => string.Equals(type.Namespace, "QPay.Domain", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .OrderBy(name => name)
            .ToArray();

        qpayDomainTypes.Should().BeEmpty();
    }

    [Fact]
    public void ChurchReport_assembly_does_not_expose_legacy_qpay_query_models()
    {
        var legacyQueryTypeNames = new[]
        {
            "ChurchReport.Payments.QryOrder",
            "ChurchReport.Payments.OrderInfo",
            "ChurchReport.Payments.OrderInfoATMParamRes",
            "ChurchReport.Payments.OrderInfoCardParamRes",
            "ChurchReport.Payments.QryOrderPay",
            "ChurchReport.Payments.TSResult"
        };

        var assembly = typeof(DonationPaymentManager).Assembly;
        var presentLegacyTypes = legacyQueryTypeNames
            .Where(typeName => assembly.GetType(typeName, throwOnError: false) != null)
            .OrderBy(typeName => typeName)
            .ToArray();

        presentLegacyTypes.Should().BeEmpty();
    }

    private static DonationPaymentProcessor CreateProcessor(IDonationPaymentCreateGatewayAdapter adapter)
    {
        var processor = (DonationPaymentProcessor)RuntimeHelpers.GetUninitializedObject(typeof(DonationPaymentProcessor));
        SetField(processor, "RETURN_URL", "https://church.example.test/qpay-return");
        SetField(processor, "BACKEND_URL", "https://church.example.test/qpay-backend");
        SetField(processor, "PAYMENT_ORGANIZATION", "Jesus");
        SetField(processor, "m_ShopNo", "NA0149_001");
        SetField(processor, "m_DonationPaymentCreateGatewayAdapter", adapter);
        return processor;
    }

    private static async Task<CreOrder> InvokeCreateDonationPaymentOrder(DonationPaymentProcessor processor)
    {
        var method = typeof(DonationPaymentProcessor).GetMethod(
            "CreateDonationPaymentOrder",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var task = method!.Invoke(
            processor,
            new object?[]
            {
                1200,
                "Fee payment",
                "20260626112233",
                "fee-id",
                "C",
                "ONE",
                string.Empty,
                0,
                "M",
                1,
                "fee",
                "Grace",
                "cc-token"
            }) as Task<CreOrder>;

        task.Should().NotBeNull();
        return await task!;
    }

    private static bool ConstructorHasAdapter(Type type)
    {
        return type
            .GetConstructors()
            .Any(constructor => constructor
                .GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(IDonationPaymentCreateGatewayAdapter)
                    || parameter.ParameterType == typeof(DonationPaymentCreateGatewayAdapter)
                    || parameter.ParameterType == typeof(DonationPaymentCreateGatewayAdapter)));
    }

    private static void SetField<T>(DonationPaymentProcessor processor, string fieldName, T value)
    {
        var field = typeof(DonationPaymentProcessor).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(processor, value);
    }

    private static DonationPaymentCreateGatewayAdapter CreateAdapter(IPaymentGateway gateway)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payment:DefaultProfile"] = "JesusTest"
            })
            .Build();

        return new DonationPaymentCreateGatewayAdapter(
            gateway,
            new PaymentCreateRequestFactory(),
            new ChurchReportPaymentProfileResolver(configuration));
    }

    private sealed class RecordingPaymentGateway : IPaymentGateway
    {
        private readonly PaymentCreateResult _createResult;

        public RecordingPaymentGateway(PaymentCreateResult createResult)
        {
            _createResult = createResult;
        }

        public int CreatePaymentCallCount { get; private set; }
        public PaymentCreateRequest? LastCreateRequest { get; private set; }

        public Task<PaymentCreateResult> CreatePaymentAsync(
            PaymentCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            CreatePaymentCallCount++;
            LastCreateRequest = request;
            return Task.FromResult(_createResult);
        }

        public Task<PaymentStatusResult> QueryPaymentAsync(
            PaymentQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentCallbackResult> ParseCallbackAsync(
            PaymentCallbackRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class SingleServiceProvider : IServiceProvider
    {
        private readonly IDonationPaymentCreateGatewayAdapter _adapter;

        public SingleServiceProvider(IDonationPaymentCreateGatewayAdapter adapter)
        {
            _adapter = adapter;
        }

        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IDonationPaymentCreateGatewayAdapter)
                ? _adapter
                : null;
        }
    }

    private sealed class TestSession : ISession
    {
        public TestSession(string id)
        {
            Id = id;
        }

        public bool IsAvailable => true;
        public string Id { get; }
        public IEnumerable<string> Keys => Array.Empty<string>();

        public void Clear()
        {
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
        }

        public void Set(string key, byte[] value)
        {
        }

        public bool TryGetValue(string key, out byte[] value)
        {
            value = Array.Empty<byte>();
            return false;
        }
    }

    private sealed class ThrowingToolUtilityProvider : IToolUtilityProvider
    {
        public ToolUtilityClass GetToolUtility()
        {
            throw new InvalidOperationException("ToolUtility should not be used by this test.");
        }
    }
}
