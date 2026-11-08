/*
 * 檔案整體說明
 *
 * 目的與定位：P2-11實際簽章合併產物的隔離測試入口；由PowerShell驗證器建立獨立AppDomain，
 * 不在一般無金鑰方案測試中偷做打包，不以原未合併DLL代替部署輸出。
 * 主要功能與流程：對指定合併DLL重用六個真通知鏈案例，含正常、錯教會、未綁定、偽語系、
 * 錯task關聯及不捕捉同步內容。SDK與HTTP全為案例私有替身，任何斷言失敗即向runner傳播。
 * 使用邊界與維護：只傳路徑及固定計數跨Domain，不傳credential、SDK或HttpClient；各案例
 * using排空後由runner在finally卸載Domain，組件及Moq資源不永久留在主測試程序。
 */
using System;
using System.Threading;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>可跨Domain呼叫的無狀態測試入口；Domain是其唯一生命週期owner。</summary>
public sealed class PackagedMemberProbe : MarshalByRefObject
{
    private int entered;

    /// <summary>使用真正合併DLL的注入接縫執行相同決定性斷言；不作外部I/O或保存測試實例。</summary>
    /// <param name="assemblyPath">runner已限定的本工作樹部署DLL完整路徑。</param>
    /// <returns>全部完成才回傳六個案例，不把部分成功當驗收。</returns>
    public int Verify(string assemblyPath)
    {
        if (Interlocked.Exchange(ref entered, 1) != 0)
            throw new InvalidOperationException("Each probe domain may be used only once.");
        var tests = new DiagnosticsMemberTests(assemblyPath);
        tests.Execute_Member_Normal_AcceptedWithTaskRetryKey();
        tests.Execute_Member_ChurchMismatch_NoHttp();
        tests.Execute_Member_NotBound_NoHttp();
        tests.Execute_Member_Pseudo_UsesMemberCultureAndTemplate();
        tests.Execute_Member_WrongRegarding_FailsBeforeHttp();
        tests.Execute_Member_DoesNotCaptureSynchronizationContext();
        return 6;
    }
}
