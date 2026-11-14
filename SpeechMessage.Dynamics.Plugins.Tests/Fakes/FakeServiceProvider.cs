/*
 * 檔案整體說明
 *
 * 目的與定位：建立離線 Plugin Execute 所需的 IServiceProvider，依服務型別回傳
 * 案例明確註冊的 tracing、context、factory 與其他替身。它不保存跨案例狀態，
 * 不建立背景工作，也不接觸 D365、LINE、Session 或秘密。
 *
 * 主要功能與流程：測試以 Add 註冊 singleton-like 的案例替身，GetService 只查
 * 本案例字典；未註冊型別回傳 null，讓外殼能決定性地測試缺服務錯誤。案例結束
 * 時由呼叫端處置自己擁有的資源。
 *
 * 使用邊界與維護：註冊值必須是合成資料；不得把可變 provider 放入 static 或
 * 重用於不同組織／使用者案例。
 */
using System;
using System.Collections.Generic;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fakes;

/// <summary>型別鍵控的測試 service provider。</summary>
internal sealed class FakeServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> services = new();

    /// <summary>註冊本案例服務並回傳自己以便串接。</summary>
    public FakeServiceProvider Add<T>(T service) where T : class
    {
        services[typeof(T)] = service ?? throw new ArgumentNullException(nameof(service));
        return this;
    }

    /// <summary>依 service type 回傳案例服務，未註冊時回傳 null。</summary>
    public object? GetService(Type serviceType) =>
        serviceType is not null && services.TryGetValue(serviceType, out var service) ? service : null;
}
