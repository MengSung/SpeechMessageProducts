#nullable enable
using System;
using System.Threading;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Dataverse;

namespace ToolUtilityNameSpace.Localization;

/// <summary>
/// 將既有產品隔離的 Dataverse manager 轉為覆寫來源的短命 lease 工廠；只保存固定部署邊界與 Host singleton manager，
/// 不保存 request、使用者或 raw SDK。共用 cache 不得注入舊 request-scoped GatewayOrganizationService。
/// </summary>
/// <remarks>
/// 組合根須先驗 manager 實際 endpoint／產品與這裡的固定識別一致，並在每次呼叫前授權教會。
/// manager 原本以 product/environment/organization/effectiveIdentity 分池，本類不開第二個池、不改變身分、不以 context 建構代替授權。
/// Host 必須先 drain cache，再 Dispose manager；任何故障 lease 由來源 MarkFaulted 後歸還，由既有池確定淘汰。
/// </remarks>
public sealed class DataverseChurchTextOverrideServiceFactory : IChurchTextOverrideServiceFactory
{
    /// <summary>Host 擁有且已配置固定產品／endpoint／身分的 manager，不在本工廠 Dispose。</summary>
    private readonly IDataverseConnectionManager manager;
    /// <summary>固定組織識別，不能從任一 caller 路由覆寫。</summary>
    private readonly string organization;
    /// <summary>固定產品識別，不容許另一產品借用同一 manager。</summary>
    private readonly string product;

    /// <summary>建立純轉接器；不自行連線、讀取機密或建立服務容器。</summary>
    /// <param name="manager">組合根驗證後的現有 manager。</param>
    /// <param name="organization">對應 manager endpoint 的已驗證部署組織。</param>
    /// <param name="product">對應 manager 固定產品的部署識別。</param>
    public DataverseChurchTextOverrideServiceFactory(IDataverseConnectionManager manager, string organization, string product)
    {
        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        this.organization = RequireBoundary(organization, nameof(organization));
        this.product = RequireBoundary(product, nameof(product));
    }

    /// <summary>先驗固定組織／產品及 caller 取消，再取得唯一操作 lease；來源讀完所有頁才歸還，不逐頁換 client。</summary>
    /// <param name="context">Host 已授權的教會範圍，文字文化不改變部署邊界。</param>
    /// <param name="cancellationToken">操作 owner token，也控制既有 manager 的 pool admission。</param>
    /// <returns>本次操作獨佔 wrapper，不向 cache 發布任何 SDK。</returns>
    public IChurchTextOverrideServiceLease Open(TextContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(context.Organization, organization, StringComparison.Ordinal)
            || !string.Equals(context.Product, product, StringComparison.Ordinal))
            throw new InvalidOperationException("Override scope does not belong to this deployment.");
        return new Lease(context, manager.Acquire(cancellationToken));
    }

    /// <summary>固定部署值必須有界且不含控制字元；保留原大小寫，不自行修剪／改寫身分。</summary>
    private static string RequireBoundary(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            throw new ArgumentException("A bounded deployment identity is required.", parameter);
        foreach (var character in value)
            if (char.IsControl(character)) throw new ArgumentException("Invalid deployment identity.", parameter);
        return value;
    }

    /// <summary>一次借用的唯一 owner；只在來源方法內存活，Dispose 清除 context 與 inner 並恰一次歸還。</summary>
    private sealed class Lease(TextContext context, IClientLease inner) : IChurchTextOverrideServiceLease
    {
        /// <summary>在來源使用期間保持完整 scope，釋放時移除純值引用。</summary>
        private TextContext? scope = context;
        /// <summary>現有池的一次性租約，不代表擁有 raw client。</summary>
        private IClientLease? current = inner ?? throw new InvalidOperationException("Dataverse manager returned no lease.");
        /// <summary>只有尚未歸還時可取 scope，避免使用已失效操作。</summary>
        public TextContext Context => Volatile.Read(ref scope) ?? throw new ObjectDisposedException(nameof(Lease));
        /// <summary>只借用操作內 SDK；呼叫端不得保存或自行 Dispose。</summary>
        public IOrganizationService Service => GetLease().Service;
        /// <summary>不確定狀態必須讓既有池淘汰，不能歸還為健康 client。</summary>
        public void MarkFaulted() => GetLease().MarkFaulted();
        /// <summary>來源已排空後由 using 呼叫；重入恰一次歸還，不處置 manager，不能重用 wrapper。</summary>
        public void Dispose()
        {
            var lease = Interlocked.Exchange(ref current, null);
            Interlocked.Exchange(ref scope, null);
            lease?.Dispose();
        }
        /// <summary>釋放後立即拒絕，不容許再接觸已歸池的 SDK。</summary>
        private IClientLease GetLease() => Volatile.Read(ref current) ?? throw new ObjectDisposedException(nameof(Lease));
    }
}
