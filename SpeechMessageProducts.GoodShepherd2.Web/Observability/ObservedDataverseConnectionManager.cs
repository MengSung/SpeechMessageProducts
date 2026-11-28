/*
檔案整體說明
目的與定位：F11好牧人主機IDataverseConnectionManager的觀測轉接，讓所有經共用介面的產品consumer同樣產生dataverse.leases.active，不只健康端點。
主要功能與流程：成功Acquire後移交底層lease及單次Meter票據給短命ObservedLease，Service與MarkFaulted沿原契約委派。唯一Dispose先完成底層歸還或淘汰，再扣計數；重入不重複釋放。觀測建立失敗時標記、歸還並安全告警，保留原始主要例外。
使用邊界與維護：manager、Meter及diagnostics全部借用DI的既有owner；decorator停止只拒絕新借用，不關閉任何借入依賴，不保留使用者、組織值或憑證。租約只活至原操作finally，Host先排空consumer再Dispose；沒有新pool、Task、timer、static狀態或背景清理。每次租借僅一個固定種類票據，原始pool仍控制容量、隔離與取消，不改來源授權或連線重用政策。合成百次租借、清理故障及Meter已關閉測試保護清理對稱。
*/
using Microsoft.Xrm.Sdk;
using SpeechMessage.Hosting.Observability;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Observability;

/// <summary>Host-owned觀測殼，只借用原manager；所有消費端仍使用既有介面及原pool隔離規則。</summary>
internal sealed class ObservedDataverseConnectionManager(
    IDataverseConnectionManager manager, HostingMeters meters, ExceptionDiagnostics diagnostics) : IDataverseConnectionManager
{
    private int disposed;

    /// <summary>只有成功借入才遞增，觀測建立失敗完整淘汰歸還；caller取消仍由原manager處理。</summary>
    public IClientLease Acquire(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var lease = manager.Acquire(cancellationToken);
        try { return new ObservedLease(lease, meters.TrackDataverseLease()); }
        catch (Exception primary)
        {
            diagnostics.Report(primary, "GoodShepherd2.Observation.Acquire", cancellationToken);
            try { lease.MarkFaulted(); }
            catch (Exception failure) { diagnostics.Report(failure, "GoodShepherd2.Observation.Fault"); }
            try { lease.Dispose(); }
            catch (Exception failure) { diagnostics.Report(failure, "GoodShepherd2.Observation.Release"); }
            throw;
        }
    }

    /// <summary>既有pool的一致純值快照，不暴露client或改變原metrics權威來源。</summary>
    public DataversePoolMetrics GetMetrics()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return manager.GetMetrics();
    }

    /// <summary>只停止本殼新借用；原manager及Meter由DI各自唯一釋放，既有lease仍可完整歸還。</summary>
    public void Dispose() => Interlocked.Exchange(ref disposed, 1);

    /// <summary>當次操作唯一持有的短命lease；不直接關閉SDK，所有實際歸還由原pool負責。</summary>
    private sealed class ObservedLease(IClientLease lease, IDisposable observation) : IClientLease
    {
        private IClientLease? current = lease;
        /// <summary>只在尚未歸還時借用服務，不延長原lease生命或跨操作發布client。</summary>
        public IOrganizationService Service => GetLease().Service;
        /// <summary>不確定傳輸仍由原pool淘汰，觀測殼不改故障或重用決策。</summary>
        public void MarkFaulted() => GetLease().MarkFaulted();
        /// <summary>原子取得唯一清理權，底層歸還完成或拋錯後才扣回計數；原始清理例外不吞沒。</summary>
        public void Dispose()
        {
            var owned = Interlocked.Exchange(ref current, null);
            if (owned is null) return;
            try { owned.Dispose(); }
            finally { observation.Dispose(); }
        }
        /// <summary>已歸還票據不能再取得服務；不因重入重新租借其他連線。</summary>
        private IClientLease GetLease() => Volatile.Read(ref current) ?? throw new ObjectDisposedException(nameof(ObservedLease));
    }
}
