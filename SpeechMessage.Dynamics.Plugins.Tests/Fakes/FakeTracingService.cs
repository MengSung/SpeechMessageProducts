/*
 * 檔案整體說明
 *
 * 目的與定位：收集單次 Plugin Execute 的追蹤訊息，供離線測試檢查錯誤型別、
 * Shadow 寫入與分段上限。訊息只保存在案例自己的清單，不寫檔、不連外、不跨
 * 使用者或組織共享。
 *
 * 主要功能與流程：Trace 將格式化後的文字追加至本案例清單；測試結束由 fixture
 * 丟棄清單。此替身不吞例外，也不模擬 Dataverse plugintracelog 的完整容量，
 * 實機一次執行長度仍須依 05A §8 查證。
 *
 * 使用邊界與維護：測試輸出不得含 token、cookie、完整訊息或未驗證個資；只用
 * 合成事件 ID 與固定型別字串。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fakes;

/// <summary>記錄格式化追蹤內容的替身。</summary>
internal sealed class FakeTracingService : ITracingService
{
    /// <summary>本案例追蹤文字。</summary>
    public List<string> Entries { get; } = new();

    /// <summary>加入不持有外部資源的追蹤文字。</summary>
    public void Trace(string format, params object[] args) =>
        Entries.Add(string.Format(CultureInfo.InvariantCulture, format, args));
}
