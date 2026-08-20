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
        if (trace?.Enabled == true)
            trace.GatewayExecuteEnter(_depth);
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

    /// <summary>歸還尚未歸還的 scoped lease；不銷毀 manager 或其長命 pool。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _depth = 0;
        var lease = _lease;
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
