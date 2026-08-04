// ============================================================================
// 檔案：ChurchReport/Services/DonationDynamicsAccessBootstrap.cs
// 目的：只以部署設定中的非秘密 Gateway 欄位建立 Package01 fee-read 路徑。
//
// 安全與生命週期契約：
// 1. Package01FeeReadsEnabled=false 時保留既有 ToolUtility/Data8 業務路徑，且不建立 executor、provider 或 HTTP 資源。
            // 2. 啟用時僅接受 DedicatedGateway/CentralGateway；Embedded 與未知 mode 在 process host、ServiceProvider、HttpClient 或 secret 解析前 fail closed。
// 3. ProfileAlias 與 Gateway endpoint 只能由 DynamicsAccess 取得；不得由 CrmConnection、CRM URL 或 credential 推導。
// 4. 唯一 process host 擁有 Gateway ServiceProvider generation，host stop/DI disposal 以同一 terminal cleanup path 釋放資源。
// 5. static facade 只保存受主 DI lifecycle 管理的 host 參考，絕不保存 provider、session、credential 或 token。
// ============================================================================

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Gateway;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// DynamicsAccess 的舊呼叫點相容 facade 與純設定綁定入口。
    /// 此 static 類別不擁有 ServiceProvider、HttpClient、handler、timer 或 executor；真正的 process generation
    /// 由 ChurchReport 主 DI 註冊的 <see cref="IDonationDynamicsAccessProcessHost"/> singleton 唯一持有。
    /// hosted lifecycle 只在網站啟動期間發佈該 singleton 的非 owner 參考，讓尚未完成 DI 遷移的 manager／service
    /// 保持編譯相容；網站停止時先撤銷 facade，再由 singleton 完成確定性 Dispose，避免跨 host 世代洩漏。
    /// </summary>
    public static class DonationDynamicsAccessBootstrap
    {
        // 這個欄位只是舊 static callsite 的過渡路由，不是 provider／executor owner；它只在 hosted lifecycle
        // Start 與 Stop 間存在。使用 Interlocked 發佈可避免多 host／多測試競爭覆蓋彼此的 process generation。
        private static IDonationDynamicsAccessProcessHost? _compatibilityProcessHost;

        /// <summary>
        /// 建立奉獻收費表單服務；若啟用 Package 1，會依 JSON 走 Gateway-only。
        /// </summary>
        public static DonationDedicationFeeFormService CreateFeeFormService(
            ToolUtilityClass utility,
            IConfiguration configuration,
            IPackage01FeeReadClient? injectedFeeReadClient = null,
            IOptions<ProductDynamicsOptions>? injectedOptions = null)
        {
            ArgumentNullException.ThrowIfNull(utility);
            ArgumentNullException.ThrowIfNull(configuration);

            var enabled = IsPackage01Enabled(configuration);
            if (!enabled)
            {
                return new DonationDedicationFeeFormService(utility);
            }

            // 已由 DI 注入完成時（測試/正式 DI 路徑），直接使用。
            if (injectedFeeReadClient is not null)
            {
                var options = injectedOptions ?? Options.Create(BindOptions(configuration));
                return new DonationDedicationFeeFormService(
                    utility,
                    injectedFeeReadClient,
                    options,
                    package01FeeReadsEnabled: true);
            }

            var productOptions = BindOptions(configuration);
            EnsureGatewayOnly(productOptions);
            var processHost = GetStartedProcessHost();
            var executor = CreateGatewayExecutor(productOptions, processHost);

            IPackage01FeeReadClient feeReadClient = new Package01FeeReadClient(
                executor,
                NullLogger<Package01FeeReadClient>.Instance);

            return new DonationDedicationFeeFormService(
                utility,
                feeReadClient,
                Options.Create(productOptions),
                package01FeeReadsEnabled: true);
        }

        /// <summary>
        /// 嘗試建立 Package 1 client（Gateway-only）。
        /// 若未啟用或設定不完整，回傳 null，呼叫端應走舊路徑。
        /// </summary>
        public static IPackage01FeeReadClient? TryCreatePackage01Client(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (!IsPackage01Enabled(configuration))
            {
                return null;
            }

            var productOptions = BindOptions(configuration);
            EnsureGatewayOnly(productOptions);
            var processHost = GetStartedProcessHost();
            var executor = CreateGatewayExecutor(productOptions, processHost);

            return new Package01FeeReadClient(
                executor,
                NullLogger<Package01FeeReadClient>.Instance);
        }

        public static bool IsPackage01Enabled(IConfiguration configuration)
        {
            var raw = configuration["DynamicsAccess:Package01FeeReadsEnabled"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 只讀取 Gateway 所需的非秘密欄位並建立短生命週期 options。Embedded、CrmConnection、CRM endpoint、
        /// credential、token 與 secret-reference 均不會被繫結、複製或快取；ProfileAlias 缺漏交由既有
        /// Gateway validation 在任何 outbound request 前 fail closed。
        /// </summary>
        public static ProductDynamicsOptions BindOptions(IConfiguration configuration)
        {
            var options = new ProductDynamicsOptions
            {
                Gateway = new GatewayEndpointOptions()
            };

            // 字串欄位再保險，避免 section bind 失敗時整段空白。
            options.ProfileAlias = FirstNonEmpty(
                options.ProfileAlias,
                configuration["DynamicsAccess:ProfileAlias"]) ?? string.Empty;

            var modeText = configuration["DynamicsAccess:ConnectionMode"];
            if (!string.IsNullOrWhiteSpace(modeText) &&
                !Enum.TryParse<ConnectionMode>(modeText, ignoreCase: true, out _))
            {
                throw new InvalidOperationException(
                    $"Unsupported DynamicsAccess:ConnectionMode '{modeText}'. Expected a {nameof(ConnectionMode)} value.");
            }

            if (!string.IsNullOrWhiteSpace(modeText) &&
                Enum.TryParse<ConnectionMode>(modeText, ignoreCase: true, out var mode))
            {
                options.ConnectionMode = mode;
            }

            options.Gateway ??= new GatewayEndpointOptions();
            options.Gateway.Endpoint = FirstNonEmpty(
                options.Gateway.Endpoint,
                configuration["DynamicsAccess:Gateway:Endpoint"]) ?? string.Empty;
            options.Gateway.ApiPrefix = FirstNonEmpty(
                options.Gateway.ApiPrefix,
                configuration["DynamicsAccess:Gateway:ApiPrefix"],
                "/v1") ?? "/v1";

            EnsureGatewayOnly(options);

            return options;
        }

        /// <summary>
        /// Package01 啟用後 ChurchReport 僅允許 Gateway execution mode。此檢查位於 process host、ServiceProvider、
        /// HttpClient、handler 與任何 secret 解析之前，避免 Embedded 設定形成本機 transport 或既有 ToolUtility
        /// 路徑的 request fallback；legacy 業務仍由未啟用的功能旗標維持原有行為。
        /// </summary>
        /// <param name="productOptions">只含 Gateway 非秘密欄位的產品設定。</param>
        private static void EnsureGatewayOnly(ProductDynamicsOptions productOptions)
        {
            ArgumentNullException.ThrowIfNull(productOptions);
            if (productOptions.ConnectionMode is not (
                    ConnectionMode.DedicatedGateway or ConnectionMode.CentralGateway))
            {
                throw new InvalidOperationException(
                    "Package01FeeReadsEnabled requires DynamicsAccess:ConnectionMode=DedicatedGateway or CentralGateway.");
            }
        }
        private static IDynamicsOperationExecutor CreateGatewayExecutor(
            ProductDynamicsOptions productOptions,
            IDonationDynamicsAccessProcessHost processHost)
        {
            if (productOptions.Gateway is null ||
                string.IsNullOrWhiteSpace(productOptions.Gateway.Endpoint) ||
                string.IsNullOrWhiteSpace(productOptions.ProfileAlias))
            {
                throw new InvalidOperationException(
                    "DynamicsAccess Gateway mode requires ProfileAlias and Gateway:Endpoint when Package01FeeReadsEnabled=true.");
            }

            // 重要：舊 facade 只能借用主 DI singleton 的 process-level executor；不得在 static 類別另建 provider。
            // 這是產品 -> Gateway 的連線池，不是 per-user CRM session pool，亦不得保存 caller/session 身份。
            return processHost.GetOrCreateGatewayExecutor(productOptions);
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// 在 Generic Host 啟動時發佈主 DI singleton 給尚未完成建構式注入的舊呼叫點。
        /// 發佈只允許空值或同一物件重入；若另一個 host 尚未停止，立即 fail-closed，避免兩個 provider generation
        /// 透過 static facade 混用 endpoint、credential source、handler 或 token cache。
        /// </summary>
        /// <param name="processHost">由 ChurchReport 主 DI 建立並擁有的 process host。</param>
        internal static void AttachProcessHost(IDonationDynamicsAccessProcessHost processHost)
        {
            ArgumentNullException.ThrowIfNull(processHost);
            var current = Interlocked.CompareExchange(
                ref _compatibilityProcessHost,
                processHost,
                comparand: null);
            if (current is not null && !ReferenceEquals(current, processHost))
            {
                throw new InvalidOperationException(
                    "A different DynamicsAccess host is already started. Stop it before starting another host.");
            }
        }

        /// <summary>
        /// 在關機時只撤銷與指定 singleton 完全相同的 facade 參考；不 Dispose 資源，因為 cleanup 仍由
        /// <see cref="DonationDynamicsAccessBootstrapLifetime"/> 所持有的主 DI singleton 執行。
        /// 精確物件比較避免舊 host 的遲到 Stop 將新 host 的相容路由清空。
        /// </summary>
        /// <param name="processHost">目前正在停止、且預期已被發佈的主 DI singleton。</param>
        internal static void DetachProcessHost(IDonationDynamicsAccessProcessHost processHost)
        {
            ArgumentNullException.ThrowIfNull(processHost);
            Interlocked.CompareExchange(
                ref _compatibilityProcessHost,
                value: null,
                comparand: processHost);
        }

        /// <summary>
        /// 取得已由 hosted lifecycle 發佈的 process host；未啟動或關機已開始時立即 fail-closed，
        /// 不自行建立 fallback provider，避免舊 static 呼叫在 DI ownership 邊界之外產生第二個 HTTP pool。
        /// </summary>
        /// <returns>由 ChurchReport 主 DI 唯一擁有的 process host。</returns>
        /// <exception cref="InvalidOperationException">Generic Host 尚未 Start 或已開始 Stop。</exception>
        private static IDonationDynamicsAccessProcessHost GetStartedProcessHost()
        {
            return Volatile.Read(ref _compatibilityProcessHost)
                   ?? throw new InvalidOperationException(
                       "DynamicsAccess host has not started or is already stopping.");
        }
    }

    /// <summary>
    /// ChurchReport 行程內 Dynamics executor generation 的唯一可注入擁有權邊界。
    /// 實作必須把 Gateway／Embedded provider、HttpClient handler、timer、socket pool 與 token cache 的最長
    /// 存活範圍限制在 Generic Host lifetime，並以單一不可變 generation 防止不同 endpoint、profile、
    /// credential source 或 CE 版本共用 mutable transport state。此介面不接受 session／user identity，
    /// 因此不能被誤用為跨要求的身份或租戶 cache。
    /// </summary>
    public interface IDonationDynamicsAccessProcessHost : IAsyncDisposable
    {
        /// <summary>
        /// 在 host StartAsync 階段，以與 Dispose 共用的 lifecycle gate 發佈舊 static facade。若 disposal 已開始
        /// 則 fail-closed，不允許已 terminal owner 被重新發佈；此方法只發佈非 owner 參考，不建立 executor。
        /// </summary>
        void PublishCompatibilityFacade();

        /// <summary>
        /// 在 host StopAsync 階段撤銷舊 static facade。實作必須只清除精確相同的 owner，避免遲到的舊 host
        /// Stop 影響新的 host；此方法不 Dispose provider，cleanup 仍由 <see cref="IAsyncDisposable.DisposeAsync"/> 負責。
        /// </summary>
        void UnpublishCompatibilityFacade();

        /// <summary>
        /// 取得或建立目前唯一的 Gateway executor generation；相同設定重用同一 executor，設定變更則
        /// fail-closed 並要求 host restart／Dispose，避免在舊 handler 尚未 drain 時建立第二個連線池。
        /// </summary>
        /// <param name="options">已綁定、但仍會由正式 ProductClient options validator 驗證的產品設定。</param>
        /// <returns>由本 process host 擁有、不得由呼叫者 Dispose 的正式 operation executor。</returns>
        IDynamicsOperationExecutor GetOrCreateGatewayExecutor(ProductDynamicsOptions options);


    }

    /// <summary>
    /// ChurchReport 主 DI 擁有的 Dynamics process host singleton。
    /// 內部只允許一個 provider／executor generation，使用短生命週期 monitor 序列化第一次建立、設定衝突與關機；
    /// 這項短而低頻的同步成本只發生在啟動或舊 facade 第一次解析，換取不會重複建立 handler、socket pool、
    /// timer、token cache 的記憶體與生命週期安全。DisposeAsync 與 GetOrCreate 共用同一 gate，確保關機時
    /// 沒有半建立或半釋放 generation，且多個 shutdown caller 會觀察到冪等、確定完成的 cleanup。
    /// </summary>
    public sealed class DonationDynamicsAccessProcessHost : IDonationDynamicsAccessProcessHost
    {
        private readonly object _lifecycleGate = new();
        private ServiceProvider? _provider;
        private IDynamicsOperationExecutor? _executor;
        private string? _generationKey;
        private Task? _disposeTask;
        private bool _disposeStarted;

        /// <summary>
        /// 建立尚未擁有 provider generation 的 process host。建構式不解析設定、不建立 ServiceProvider、
        /// HttpClient、handler、timer、socket 或 token cache；只有 feature flag 啟用後的正式 executor 解析
        /// 才會開始資源 ownership，之後由本物件的 <see cref="DisposeAsync"/> 唯一且 terminal 地釋放。
        /// </summary>
        public DonationDynamicsAccessProcessHost()
        {
        }

        /// <summary>
        /// 在 process lifecycle monitor 內發佈非 owner static facade。與 Dispose 使用同一把 lock 可封閉
        /// Start／shutdown 競爭：一旦 terminal flag 設定，任何遲到 Start 都只能收到 ObjectDisposedException，
        /// 不會把已釋放的 provider owner 再掛回 static 路由。
        /// </summary>
        public void PublishCompatibilityFacade()
        {
            lock (_lifecycleGate)
            {
                ObjectDisposedException.ThrowIf(_disposeStarted, this);
                DonationDynamicsAccessBootstrap.AttachProcessHost(this);
            }
        }

        /// <summary>
        /// 在 process lifecycle monitor 內撤銷精確相同的 static facade；此動作是冪等 no-op，且不等待
        /// provider cleanup，因此正常 Stop 可先封閉新 static 呼叫，再由 DisposeAsync 完成 transport 回收。
        /// </summary>
        public void UnpublishCompatibilityFacade()
        {
            lock (_lifecycleGate)
            {
                DonationDynamicsAccessBootstrap.DetachProcessHost(this);
            }
        }

        /// <summary>
        /// 取得或建立 Gateway executor。正式 ProductClient DI 擴充會建立唯一 HttpClientFactory-owned handler；
        /// 本方法不增加 caller identity header，也不直接送 HTTP。options 驗證在 executor 解析時執行，
        /// 因而無效 HTTPS endpoint、alias 或 API prefix 會在 host StartAsync preflight 階段 fail-closed。
        /// </summary>
        public IDynamicsOperationExecutor GetOrCreateGatewayExecutor(ProductDynamicsOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var key = ComputeGenerationKey(
                "gateway",
                options.ProfileAlias,
                options.Gateway?.Endpoint,
                options.Gateway?.ApiPrefix,
                options.Gateway?.MaxResponseBytes.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

            return GetOrCreate(key, services =>
            {
                services.AddSpeechMessageDynamicsGatewayProductClient(configured =>
                {
                    configured.ConnectionMode = options.ConnectionMode;
                    configured.ProfileAlias = options.ProfileAlias;
                    configured.Gateway = options.Gateway is null
                        ? null
                        : new GatewayEndpointOptions
                        {
                            Endpoint = options.Gateway.Endpoint,
                            ApiPrefix = options.Gateway.ApiPrefix,
                            MaxResponseBytes = options.Gateway.MaxResponseBytes
                        };
                });
            });
        }

        /// <summary>
        /// 確定性釋放目前 provider generation。多個 StopAsync／DI DisposeAsync caller 會取得同一個 cleanup task，
        /// 因此都觀察相同完成或失敗結果，且 provider 只 Dispose 一次。第一個 caller 在 monitor 內把 host 標成
        /// terminal、取走 owner 欄位並發佈 cleanup task；之後的 GetOrCreate 一律丟出 ObjectDisposedException，
        /// 避免 shutdown 開始後由遲到要求重建 handler、timer、socket 或 token cache。
        /// </summary>
        public ValueTask DisposeAsync()
        {
            lock (_lifecycleGate)
            {
                if (_disposeTask is null)
                {
                    _disposeStarted = true;
                    // Generic Host 啟動失敗時可能直接 Dispose DI singleton，而不先呼叫 hosted StopAsync；
                    // 同一 gate 內撤銷 facade，才能避免 concurrent Start 把已 terminal owner 重新發佈。
                    DonationDynamicsAccessBootstrap.DetachProcessHost(this);
                    var provider = _provider;
                    _provider = null;
                    _executor = null;
                    _generationKey = null;
                    _disposeTask = provider is null
                        ? Task.CompletedTask
                        : DisposeProviderAsync(provider);
                }

                return new ValueTask(_disposeTask);
            }
        }

        /// <summary>
        /// 在 lifecycle gate 內建立或重用唯一 generation。provider 只在全部服務註冊完成後才解析 executor；
        /// 解析失敗時先釋放尚未發佈的 provider，再把原始錯誤交回啟動流程，避免部分 handler graph 被遺留。
        /// </summary>
        private IDynamicsOperationExecutor GetOrCreate(
            string generationKey,
            Action<IServiceCollection> configureServices)
        {
            lock (_lifecycleGate)
            {
                ObjectDisposedException.ThrowIf(_disposeStarted, this);

                if (_provider is not null)
                {
                    if (!string.Equals(_generationKey, generationKey, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "DynamicsAccess process configuration changed. Restart the host to replace and drain the active generation.");
                    }

                    return _executor
                           ?? throw new InvalidOperationException(
                               "DynamicsAccess process generation has no executor.");
                }

                var services = new ServiceCollection();
                services.AddLogging();
                configureServices(services);

                var provider = services.BuildServiceProvider(validateScopes: true);
                try
                {
                    var executor = provider.GetRequiredService<IDynamicsOperationExecutor>();
                    _provider = provider;
                    _executor = executor;
                    _generationKey = generationKey;
                    return executor;
                }
                catch (Exception originalFailure)
                {
                    try
                    {
                        provider.Dispose();
                    }
                    catch (Exception cleanupFailure)
                    {
                        throw new AggregateException(
                            "DynamicsAccess provider initialization and rollback both failed.",
                            originalFailure,
                            cleanupFailure);
                    }

                    throw;
                }
            }
        }

        /// <summary>
        /// 等待 ServiceProvider 完成非同步 cleanup。此 helper 會把 provider DisposeAsync 在同步前段發生的例外
        /// 也封裝進共享 Task，確保所有併行 Dispose caller 觀察相同失敗，而不是只有第一個 owner 看見例外。
        /// </summary>
        private static async Task DisposeProviderAsync(ServiceProvider provider)
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 將 generation 欄位以長度前綴的 SHA-256 digest 正規化，避免簡單串接碰撞造成不同 profile／endpoint
        /// 誤判為同一世代。每個欄位的暫存 UTF-8 bytes 在加入 hash 後立即清零，降低 local secret 在額外
        /// managed buffer 中的停留時間；digest 只用於同 process 相等比較，不會記錄或跨程序持久化。
        /// </summary>
        private static string ComputeGenerationKey(params string?[] fields)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> length = stackalloc byte[4];

            foreach (var field in fields)
            {
                var bytes = Encoding.UTF8.GetBytes(field ?? string.Empty);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
                hash.AppendData(length);
                hash.AppendData(bytes);
                CryptographicOperations.ZeroMemory(bytes);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }

    /// <summary>
    /// 將主 DI singleton 發佈給舊 static facade，並在 host shutdown 時成為明確的 process generation
    /// cleanup 協調者。StartAsync 不解析 executor／provider／HttpClient，因此 feature flag=false 的網站啟動
    /// 仍是嚴格零資源；StopAsync 先撤銷新 static 呼叫，再等待 singleton Dispose，避免關機競爭建立新世代。
    /// DI container 之後可能再次呼叫 DisposeAsync，所以 process host 必須提供併行冪等保證。
    /// </summary>
    public sealed class DonationDynamicsAccessBootstrapLifetime : IHostedService
    {
        private readonly IDonationDynamicsAccessProcessHost _processHost;
        private int _started;

        /// <summary>
        /// 建立只持有主 DI singleton 參考的 hosted lifecycle；建構式不建立任何外部資源。
        /// </summary>
        /// <param name="processHost">ChurchReport 主 DI 唯一擁有的 Dynamics process host。</param>
        public DonationDynamicsAccessBootstrapLifetime(
            IDonationDynamicsAccessProcessHost processHost)
        {
            _processHost = processHost ?? throw new ArgumentNullException(nameof(processHost));
        }

        /// <summary>
        /// 發佈 legacy facade 路由。若 caller 已取消則在發佈前停止；重複 Start 為冪等 no-op，
        /// 不解析 executor 或啟動 HTTP，讓真正的 preflight 仍由獨立 hosted service 控制。
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _started, 1) == 0)
            {
                try
                {
                    _processHost.PublishCompatibilityFacade();
                }
                catch
                {
                    Volatile.Write(ref _started, 0);
                    throw;
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 先阻止舊 static 呼叫取得 owner，再等待 provider cleanup 完成。此方法刻意不把 host shutdown token
        /// 傳入 provider disposal，因為中途取消 cleanup 會遺留 handler、timer、socket 或 token cache；
        /// Stop 重入為 no-op，而 cleanup 例外會傳回 Generic Host，不能靜默視為成功。
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _started, 0) == 0)
            {
                return;
            }

            _processHost.UnpublishCompatibilityFacade();
            await _processHost.DisposeAsync().ConfigureAwait(false);
        }
    }
}
