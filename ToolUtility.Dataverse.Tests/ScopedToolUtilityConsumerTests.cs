// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Dataverse.Tests/ScopedToolUtilityConsumerTests.cs
// 檔案責任：驗證 ChurchReport A 類工具只接受目前 request 的 ToolUtility，
//           並且短命付款處理器不會釋放由 DI scope 擁有的工具。
// 生命週期契約：測試替身的 IOrganizationService 由測試擁有；
//               ToolUtilityClass 的 scoped 服務所有權仍由外層 scope 管理。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using ChurchReport.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ToolUtility.Dataverse.Tests
{
    /// <summary>
    /// 驗證 A 類 consumer 的 request-scoped ToolUtility 注入與釋放邊界。
    /// </summary>
    public sealed class ScopedToolUtilityConsumerTests
    {
        /// <summary>
        /// 保護 QR 工具不再於欄位初始化時呼叫 legacy Factory。
        /// 故障注入為未設定 Factory 的程序狀態；若工具仍呼叫 Factory，建構即會失敗。
        /// 決定性斷言為建構後欄位持有呼叫端提供的同一個 scoped 實例。
        /// </summary>
        [Fact]
        public void QrCodeUtility_UsesInjectedRequestToolUtility()
        {
            var toolUtility = (ToolUtilityClass)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(ToolUtilityClass));

            var utility = new QrCodeUtility(toolUtility);

            var field = typeof(QrCodeUtility).GetField(
                "m_ToolUtilityClass",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(field);
            Assert.Same(toolUtility, field!.GetValue(utility));
        }

        /// <summary>
        /// 保護短命付款處理器不得釋放 request scope 擁有的 ToolUtility。
        /// 故障注入為可驗證 Dispose 呼叫的 CRM 替身；決定性斷言是 Dispose 後
        /// 組織服務仍未收到 Dispose，證明 consumer 沒有越權釋放 scoped 依賴。
        /// </summary>
        [Fact]
        public void DonationFeePaymentProcessor_DisposeDoesNotDisposeInjectedToolUtility()
        {
            var organizationService = new Mock<IOrganizationService>(MockBehavior.Loose);
            var disposableService = organizationService.As<IDisposable>();
            var tracer = new Mock<IToolUtilityTracer>(MockBehavior.Loose);
            var configuration = new ConfigurationBuilder().Build();
            var toolUtility = new ToolUtilityClass(
                organizationService.Object,
                tracer.Object,
                configuration);
            var provider = new StubToolUtilityProvider(toolUtility);

            using (var processor = new DonationFeePaymentProcessor(provider))
            {
            }

            disposableService.Verify(
                service => service.Dispose(),
                Times.Never);
        }

        /// <summary>
        /// 僅回傳已由外層建立的 scoped ToolUtility；此替身不擁有也不釋放該實例。
        /// </summary>
        private sealed class StubToolUtilityProvider : IToolUtilityProvider
        {
            private readonly ToolUtilityClass _toolUtility;

            public StubToolUtilityProvider(ToolUtilityClass toolUtility)
            {
                _toolUtility = toolUtility ?? throw new ArgumentNullException(nameof(toolUtility));
            }

            public ToolUtilityClass GetToolUtility() => _toolUtility;
        }
    }
}
