using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel;
using System.Threading;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// Scoped Gateway 的 reentrant 實作。
/// 它只持有 scoped lease 參考，不持有 raw client；lease 由 manager pool 決定底層 client 的最終釋放。
/// </summary>
public sealed class DataverseGateway : IDataverseGateway, IDisposable
{
    private readonly IDataverseConnectionManager _manager;
    private IClientLease _lease;
    private int _depth;
    private int _disposed;

    /// <summary>
    /// 目前同時進行中的 <see cref="Execute{T}"/> 呼叫數，僅供觀測，不參與任何控制流決策。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>這個計數守的是什麼</b>：本型別是 Scoped（一個 request 一個實例），但 <c>_depth</c> 與
    /// <c>_lease</c> 是無同步保護的一般欄位。產品程式碼中有十餘處 <c>Task.Run</c> /
    /// <c>Task.WhenAll</c> 會讓多條執行緒共用同一個實例，此時兩種競態都會破壞連線隔離：
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// 兩條執行緒同時讀到 <c>_depth == 0</c>，各自呼叫 <c>Acquire</c>，而 <c>_lease</c> 只保留後寫入的
    /// 那一條 —— 另一條 lease 永遠不會歸還，連線池永久少一格。
    /// </description></item>
    /// <item><description>
    /// 先完成的執行緒把 <c>_depth</c> 遞減為零並歸還 client，但另一條執行緒仍在使用它 ——
    /// 該 client 回到池中後可能立刻被別的 request 租走，這正是整套架構要防止的跨 request 共用。
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>為什麼只觀測不修正</b>：修正需要把 <c>_depth</c> 與 <c>_lease</c> 改為 AsyncLocal，讓每條
    /// 平行分支各自持有租約，那是語意層級的變更，會改變租約數量與生命週期，必須單獨評估。
    /// 目前的單元測試只涵蓋單執行緒巢狀呼叫，實測 trace 中深度也恆為 1，代表平行路徑尚未被觸發；
    /// 但在本計數加入之前，一旦觸發也不會留下任何痕跡。先讓它可見，是修正的前置條件。
    /// </para>
    /// <para>
    /// 以 <see cref="Interlocked"/> 更新，因此計數本身在競態下仍然正確 —— 它不能修好被觀測的競態，
    /// 但至少不會因為同一個競態而漏報。
    /// </para>
    /// </remarks>
    private int _activeCalls;

    /// <summary>
    /// 目前這條非同步流程自身的巢狀深度，用來把「巢狀」與「平行」區分開。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>為什麼單看 <see cref="_activeCalls"/> 不夠</b>：單執行緒的三層巢狀呼叫會讓該計數依序
    /// 變成 1、2、3（外層尚未離開就進入內層），若直接以「大於 1」判定平行，每一次正常的
    /// reentrant 呼叫都會產生假警報。一個總是在叫的警報等於沒有警報。
    /// </para>
    /// <para>
    /// <b>為什麼 AsyncLocal 能區分</b>：ExecutionContext 採寫入時複製，因此 <c>Task.Run</c> 分支出去的
    /// 每條流程都會拿到自己的副本，兄弟分支之間互不可見。於是同一條流程的巢狀會讓
    /// <c>_activeCalls</c> 與本值同步增加（兩者相等），而另一條流程平行進入時只會推高
    /// <c>_activeCalls</c>，本值仍為 1 —— 差值即為真正的平行度。
    /// </para>
    /// <para>
    /// 判定式因此是 <c>_activeCalls &gt; flowDepth</c>：巢狀恆等、平行必然大於。
    /// </para>
    /// </remarks>
    private readonly AsyncLocal<int> _flowDepth = new();

    /// <summary>建立目前 request scope 專屬的 Gateway。</summary>
    public DataverseGateway(IDataverseConnectionManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <summary>
    /// 在目前 Gateway 範圍執行不回傳值的 Dataverse 操作。外層呼叫取得一次 lease，巢狀呼叫只增加深度；
    /// Trace 僅記錄進出深度，絕不攔截、吞沒或改寫底層例外與 lease 的 faulted 決策。
    /// </summary>
    public void Execute(Action<IOrganizationService> work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        Execute<object>(service => { work(service); return null; });
    }

    /// <summary>
    /// 在目前 scope 的唯一 lease 上執行 Dataverse 操作。lease 會在最外層 finally 中歸還；只有被
    /// <see cref="IsConnectionFault"/> 判定為傳輸層故障的例外才標記 faulted，商業層 fault 一律原樣
    /// 往上拋但保留連線。啟用 Trace 時只於這些既有邊界記錄 reentrant 深度，以驗證同一 request 不會
    /// 重複租借 client。
    /// </summary>
    public T Execute<T>(Func<IOrganizationService, T> work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        ThrowIfDisposed();

        if (_depth == 0)
            _lease = _manager.Acquire();
        _depth++;
        var trace = DataverseTrace.Current;
        var flowDepth = _flowDepth.Value + 1;
        _flowDepth.Value = flowDepth;
        var activeCalls = Interlocked.Increment(ref _activeCalls);
        if (trace?.Enabled == true)
        {
            trace.GatewayExecuteEnter(_depth);
            // 同一條流程的巢狀會讓兩個計數同步增加而相等；只有另一條流程平行進入時，
            // 全域計數才會超過本流程自身的深度。詳細理由見 _flowDepth 的說明。
            if (activeCalls > flowDepth)
                trace.GatewayConcurrent(activeCalls);
        }
        try
        {
            return work(_lease.Service);
        }
        catch (Exception ex)
        {
            // 例外一律原樣重擲；此處只決定「這條連線還能不能重用」，絕不改變呼叫端看到的錯誤。
            if (IsConnectionFault(ex))
                _lease.MarkFaulted();
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _activeCalls);
            _flowDepth.Value = flowDepth - 1;
            if (trace?.Enabled == true)
                trace.GatewayExecuteExit(_depth);
            _depth--;
            if (_depth == 0)
            {
                var lease = _lease;
                _lease = null;
                lease?.Dispose();
            }
        }
    }

    /// <summary>
    /// 歸還尚未歸還的 scoped lease；不銷毀 manager 或其長命 pool。
    /// </summary>
    /// <remarks>
    /// 正常流程中 lease 已於最外層 <see cref="Execute{T}"/> 的 finally 歸還，因此此處的
    /// <c>lease</c> 應為 null、<c>_depth</c> 應為 0。若不是，代表有一條 lease 是靠 DI 容器回收
    /// scope 才被救回來，而非靠正常的執行路徑 —— 這在有 DI 保護時不會出錯，但同樣的控制流出現在
    /// 沒有 scope 保護的呼叫點（例如背景工作）時就是真正的連線洩漏。因此在釋放前先記錄狀態。
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var lease = _lease;
        var trace = DataverseTrace.Current;
        if (trace?.Enabled == true)
            trace.GatewayScopeEnd(_depth, leaseStillHeld: lease != null);

        _depth = 0;
        _lease = null;
        lease?.Dispose();
    }

    /// <summary>
    /// 判定例外是否代表「這條實體連線已不可信」，用來決定歸還時要淘汰還是留在池內重用。
    /// </summary>
    /// <remarks>
    /// 分類依據是「Dataverse 有沒有成功收到請求並回覆」：
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="FaultException"/>（含 <c>FaultException&lt;OrganizationServiceFault&gt;</c>）代表伺服器
    /// 已完整處理並回傳 SOAP fault —— 欄位不存在、權限不足、驗證規則失敗都屬此類。通道、token 與
    /// 安全內容全部健康，淘汰它只會換來一次沒有必要的重新握手。
    /// </description></item>
    /// <item><description>
    /// 傳輸層例外（WCF 通道、原始 HTTP／Socket、逾時）代表請求可能根本沒送達或回應已損毀，
    /// 通道狀態不可信，必須淘汰。
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>順序不可調換</b>：<see cref="FaultException"/> 在 WCF 型別階層中是
    /// <see cref="CommunicationException"/> 的子類別，若先比對 <see cref="CommunicationException"/>，
    /// 所有商業 fault 都會被誤判為連線故障，本方法即等同無效。
    /// </para>
    /// <para>
    /// 逐層檢查 <see cref="Exception.InnerException"/>，因為上層組件可能把原始例外包成
    /// <see cref="InvalidOperationException"/> 之類的型別；每一層都先判 fault 再判傳輸，維持同一優先序。
    /// </para>
    /// <para>
    /// 未知例外採「保留連線」：應用程式自身的錯誤不代表通道損毀，而真正壞掉的連線仍有兩道後備防線
    /// —— 出借前的 WhoAmI 健康檢查，以及下一次操作必然再擲出的傳輸層例外。反向預設（未知即淘汰）
    /// 會讓任何應用程式 bug 都燒毀一條連線，正是本次修正要消除的行為。
    /// </para>
    /// </remarks>
    /// <param name="exception">操作委派擲出的例外；本方法只讀取型別，不讀取訊息內容或 CRM 資料。</param>
    /// <returns>true 表示連線必須淘汰；false 表示連線可安全歸還池內重用。</returns>
    private static bool IsConnectionFault(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            // 商業層 fault 必須先於 CommunicationException 判定，理由見 remarks。
            if (current is FaultException)
                return false;

            if (current is CommunicationException or TimeoutException or WebException or SocketException or IOException)
                return true;
        }

        return false;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(DataverseGateway));
    }
}
