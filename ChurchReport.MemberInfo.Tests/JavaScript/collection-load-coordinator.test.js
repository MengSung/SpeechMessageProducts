// ============================================================================
// 檔案路徑：ChurchReport.MemberInfo.Tests/JavaScript/collection-load-coordinator.test.js
// 測試責任：以 Node 內建 test runner 決定性模擬慢網路回應亂序、重複 refresh 與 view dispose，
//           驗證每個 Grid owner 只有最新 generation 能完成發布，且所有 timer／transport 參考可清除。
// 資源生命週期：每個案例都主動 dispose coordinator，測試不建立 watcher、伺服器、DOM、Session、
//               無界 queue 或常駐程序；受控 Promise 只在案例區域存活。
// 編碼要求：本檔案必須以 UTF-8 without BOM、CRLF、final CRLF 儲存。
// ============================================================================
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { createCollectionLoadCoordinator, mount, disposeOwner } = require(
    '../../SpeechMessageProducts.ChurchReport/wwwroot/js/collection-load-coordinator.js');

/**
 * 建立可由測試指定完成順序的 transport；abort 只記錄呼叫而不強迫 Promise 中止，
 * 用來證明 generation 才是正確性邊界，而不是假設瀏覽器一定能取消已送出的回應。
 */
function createControlledTransport() {
    let resolve;
    let reject;
    let abortCount = 0;
    const promise = new Promise((resolvePromise, rejectPromise) => {
        resolve = resolvePromise;
        reject = rejectPromise;
    });

    promise.abort = function abort() {
        abortCount += 1;
    };

    return {
        promise,
        resolve,
        reject,
        get abortCount() { return abortCount; }
    };
}

/**
 * 驗證第二世代先完成後，第一世代即使稍後成功也只能得到 stale rejection，
 * 不得把舊資料 resolve 給目前 Grid。
 */
test('晚到的舊成功回應不能覆蓋新世代', async () => {
    const coordinator = createCollectionLoadCoordinator();
    const first = createControlledTransport();
    const second = createControlledTransport();

    const firstLoad = coordinator.runLoad(() => first.promise);
    const secondLoad = coordinator.runLoad(() => second.promise);
    second.resolve(['new-generation']);
    assert.deepEqual(await secondLoad, ['new-generation']);

    first.resolve(['stale-generation']);
    await assert.rejects(firstLoad, error => error && error.isStaleGeneration === true);
    assert.equal(first.abortCount, 1);
    coordinator.dispose();
});

/**
 * 驗證舊世代的錯誤也不能改寫新世代 error UI；舊錯誤一律正規化為 stale cancellation，
 * 最新世代成功值仍完整交付。
 */
test('晚到的舊錯誤不能覆蓋新世代成功狀態', async () => {
    const coordinator = createCollectionLoadCoordinator();
    const first = createControlledTransport();
    const second = createControlledTransport();

    const firstLoad = coordinator.runLoad(() => first.promise);
    const secondLoad = coordinator.runLoad(() => second.promise);
    second.resolve(['current']);
    assert.deepEqual(await secondLoad, ['current']);

    first.reject(new Error('old network failure'));
    await assert.rejects(firstLoad, error => error && error.isStaleGeneration === true);
    coordinator.dispose();
});

/**
 * 驗證 active load 期間任意多次 refresh 只保留一個 pending intent；目前世代完成後
 * refresh action 只執行一次，不形成無界 timer、Promise 或 XHR queue。
 */
test('active load 期間重複 refresh 只合併成一次', async () => {
    const coordinator = createCollectionLoadCoordinator();
    const transport = createControlledTransport();
    let refreshCount = 0;
    const load = coordinator.runLoad(() => transport.promise);

    for (let index = 0; index < 100; index += 1) {
        coordinator.requestRefresh(() => { refreshCount += 1; });
    }

    assert.equal(coordinator.getDiagnostics().pendingRefreshCount, 1);
    transport.resolve([]);
    await load;
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(refreshCount, 1);
    assert.equal(coordinator.getDiagnostics().pendingRefreshCount, 0);
    coordinator.dispose();
});

/**
 * 驗證 dispose 先使 generation 失效、再 abort transport 並清空 pending callback；
 * 之後的晚到回應不得發布，診斷計數必須回到零且不可再次 refresh。
 */
test('dispose 後會清除 transport 與 pending refresh 並拒絕晚到回應', async () => {
    const coordinator = createCollectionLoadCoordinator();
    const transport = createControlledTransport();
    let refreshCount = 0;
    const load = coordinator.runLoad(() => transport.promise);
    coordinator.requestRefresh(() => { refreshCount += 1; });

    coordinator.dispose();
    transport.resolve(['late']);

    await assert.rejects(load, error => error && error.isStaleGeneration === true);
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(transport.abortCount, 1);
    assert.equal(refreshCount, 0);
    assert.deepEqual(coordinator.getDiagnostics(), {
        disposed: true,
        activeRequestCount: 0,
        pendingRefreshCount: 0
    });
});

/**
 * 驗證同一 DOM owner 被 partial view 重複 mount 時，舊 coordinator 與舊元件只釋放一次，
 * WeakMap 最終只指向最新 owner；測試結束明確 disposeOwner，避免測試物件留在 registry。
 */
test('同一 owner 重複 mount 會先完整釋放舊世代', () => {
    const owner = {};
    let firstDisposeCount = 0;
    let secondDisposeCount = 0;
    const first = mount(owner, () => { firstDisposeCount += 1; });
    const second = mount(owner, () => { secondDisposeCount += 1; });

    assert.equal(first.getDiagnostics().disposed, true);
    assert.equal(second.getDiagnostics().disposed, false);
    assert.equal(firstDisposeCount, 1);
    assert.equal(secondDisposeCount, 0);

    disposeOwner(owner);
    assert.equal(second.getDiagnostics().disposed, true);
    assert.equal(secondDisposeCount, 1);
});
