/*
 * 檔案整體說明
 * 目的與定位：本檔直接執行 GoodShepherd2 的 LIFF 路由入口及身分頁產品模組，
 * 以離線契約測試驗證設定、SDK 次序、Bearer 請求、文字呈現與開發隔離探測。
 * 主要功能與流程：建立每案獨立的 DOM、SDK Proxy、fetch 與可控初始化；注入
 * 設定缺失、401、403、伺服器失敗、惡意文字及離頁競賽，決定性核對輸出、
 * 導頁次數、請求選項與 AbortSignal。載入的皆為真實產品 JS，不內嵌產品替身。
 * 邊界與資源：測試只有合成 tenant、角色及 token，不接觸 LINE、瀏覽器或正式
 * 網路。頁面 owner 在 after hook 觸發 pagehide，釋放初始化並等待已啟動工作；
 * 追蹤計時器須歸零，事件與資料只活在單案。這不代表部署、真實 SDK 或手機通過。
 * 維護與驗證：node --test SpeechMessageProducts.GoodShepherd2.Web.Tests/js/whoami.test.cjs；
 * 任何提前讀參數、寫 innerHTML、寫 storage 或 token 洩露都使測試失敗。
 * 來源須 UTF-8 無 BOM、CRLF 及末尾 CRLF；新增行為必須增補具體故障斷言。
 */
'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const base = path.resolve(__dirname, '../../SpeechMessageProducts.GoodShepherd2.Web/wwwroot/liff');
const TENANT_A = '12345678-1234-1234-1234-123456789abc';
const TENANT_B = '22345678-1234-1234-1234-123456789abc';
const TOKEN = 'synthetic-id-token-never-render';
const options = { timeout: 2500 };

/** 單案可釋放初始化；拒絕會由產品 Promise 的 catch 消費，不建立外部工作。 */
function deferred() {
    let resolve, reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
}

/** 每案追蹤產品安裝的事件，離頁後以 listener 數量歸零驗證資源排空。 */
class TrackedTarget extends EventTarget {
    constructor() { super(); this.listeners = new Map(); }
    addEventListener(name, callback, value) {
        if (!this.listeners.has(name)) this.listeners.set(name, new Set());
        this.listeners.get(name).add(callback); super.addEventListener(name, callback, value);
    }
    removeEventListener(name, callback, value) {
        this.listeners.get(name)?.delete(callback); super.removeEventListener(name, callback, value);
    }
    get listenerCount() { return [...this.listeners.values()].reduce((sum, set) => sum + set.size, 0); }
}

/** DOM 探針只允許文字寫入；真實事件模型可驗證 click listener 的清理。 */
class Element extends TrackedTarget {
    constructor() { super(); this.textContent = ''; this.hidden = true; this.disabled = false; }
    set innerHTML(_value) { assert.fail('禁止 innerHTML'); }
}

/**
 * 建立受控頁面及 SDK；初始化未完成前讀取 location 或其他 SDK 屬性會留下違規。
 * after hook 先離頁再釋放 init 並排空工作，決定性斷言沒有遺留自建 timer。
 */
function harness(t, settings = {}) {
    const initialization = deferred();
    let initialized = false;
    const events = [], violations = [], requests = [], navigations = [], timers = new Set();
    const nodes = Object.fromEntries(['page-status', 'identity', 'tenant-id', 'tenant-name',
        'roles', 'login-button', 'probe-button', 'probe-result'].map(id => [id, new Element()]));
    const win = new TrackedTarget();
    win.document = { body: { dataset: { liffPage: settings.page || 'whoami' } },
        getElementById(id) { return nodes[id]; } };
    win.AbortController = AbortController;
    win.URLSearchParams = URLSearchParams;
    const timerCallbacks = new Map();
    win.setTimeout = (callback, delay) => {
        const id = setTimeout(() => { timers.delete(id); timerCallbacks.delete(id); callback(); }, delay);
        timers.add(id); timerCallbacks.set(id, callback); return id;
    };
    win.clearTimeout = id => { timers.delete(id); timerCallbacks.delete(id); clearTimeout(id); };
    win.expireTimers = () => {
        for (const [id, callback] of [...timerCallbacks]) { win.clearTimeout(id); callback(); }
    };
    Object.defineProperty(win, 'location', { get() {
        if (!initialized) violations.push('location before init');
        return { get search() { return settings.search || ''; },
            replace(target) { navigations.push(target); } };
    } });
    for (const key of ['localStorage', 'sessionStorage']) {
        Object.defineProperty(win, key, { get() { assert.fail('禁止 storage'); } });
    }
    win.console = new Proxy({}, { get() { assert.fail('禁止 console'); } });
    const config = { liffId: 'synthetic-liff-id', isDevelopment: false, ...settings.config };
    win.fetch = async (url, init) => {
        requests.push({ url, init }); events.push(`fetch:${url}`);
        if (settings.fetch) return settings.fetch(url, init, config);
        if (url === '/api/liff/config') return response(settings.configStatus || 200, config);
        if (url === '/api/liff/whoami') return response(settings.status || 200,
            settings.identity || { tenantId: TENANT_A, tenantName: '測試教會', roles: ['member'] });
        return response(settings.probeStatus || 200, { ok: true });
    };
    win.liff = new Proxy({
        init(value) {
            events.push('init'); assert.equal(value.liffId, 'synthetic-liff-id');
            assert.equal(Object.keys(value).join(','), 'liffId');
            return initialization.promise.then(() => { initialized = true; events.push('initialized'); });
        },
        getIDToken() {
            events.push('getIDToken');
            return settings.noToken ? null : typeof settings.token === 'function' ? settings.token() : settings.token || TOKEN;
        },
        login(...args) { assert.equal(args.length, 0); events.push('login'); }
    }, { get(target, key) {
        if (key !== 'init' && !initialized) violations.push(`SDK:${String(key)}`);
        return target[key];
    } });
    let work;
    t.after(async () => {
        win.dispatchEvent(new Event('pagehide')); initialization.resolve();
        if (work) await work;
        assert.equal(timers.size, 0, '頁面計時器必須排空');
        assert.equal(win.listenerCount, 0, '離頁 listener 必須排空');
        for (const node of Object.values(nodes)) assert.equal(node.listenerCount, 0, 'DOM listener 必須排空');
        assert.deepEqual(violations, [], 'init 前不得讀參數或其他 SDK');
    });
    return { win, nodes, initialization, events, requests, navigations, timers,
        start(kind = 'whoami') {
            if (kind === 'whoami') {
                work = require(path.join(base, 'whoami.js')).startWhoamiPage(win);
            } else {
                work = require(path.join(base, 'liff-router.js')).startRouterPage(win);
            }
            return work;
        } };
}

/** 合成回應包含 body cancel，以驗證錯誤時不留下未消費回應；不執行 HTTP。 */
function response(status, data) {
    return { status, ok: status >= 200 && status < 300,
        async json() { return data; }, body: { async cancel() {} } };
}

/** 只排空短微工作及一個事件迴圈回合，讓事件 callback 完成；沒有長時間輪詢。 */
async function flush() { await new Promise(resolve => setImmediate(resolve)); }

test('whoami waits for init before token and parameters', options, async t => {
    const h = harness(t); const work = h.start(); await flush();
    assert.deepEqual(h.events, ['fetch:/api/liff/config', 'init']);
    h.initialization.resolve(); await work;
    assert.deepEqual(h.events, ['fetch:/api/liff/config', 'init', 'initialized',
        'getIDToken', 'fetch:/api/liff/whoami']);
    assert.equal(h.nodes.identity.hidden, false);
    assert.equal(h.nodes['tenant-name'].textContent, '測試教會');
    assert.equal(h.nodes['tenant-id'].textContent, TENANT_A);
    assert.equal(h.nodes.roles.textContent, 'member');
    assert.equal(h.nodes['probe-button'].hidden, true);
    const request = h.requests[1];
    assert.equal(request.init.headers.Authorization, `Bearer ${TOKEN}`);
    assert.equal(request.init.method, 'GET');
    assert.equal(request.init.cache, 'no-store');
    assert.equal(request.init.credentials, 'omit');
    assert.equal(request.init.redirect, 'error');
    assert.ok(request.init.signal instanceof AbortSignal);
    assert.ok(Object.values(h.nodes).every(n => !n.textContent.includes(TOKEN)));
});

for (const page of ['router', 'whoami']) {
    test(`${page} missing ID reports fixed unconfigured text`, options, async t => {
        const h = harness(t, { config: { liffId: '' } }); await h.start(page);
        assert.equal(h.nodes['page-status'].textContent, '尚未設定');
        assert.deepEqual(h.events, ['fetch:/api/liff/config']);
        assert.equal(h.requests.length, 1);
    });
    test(`${page} config failure makes no SDK call`, options, async t => {
        const h = harness(t, { configStatus: 404 }); await h.start(page);
        assert.equal(h.events.length, 1); assert.deepEqual(h.navigations, []);
        assert.ok(h.nodes['page-status'].textContent.includes('無法'));
    });
    test(`${page} init rejection never routes or reads token`, options, async t => {
        const h = harness(t); const work = h.start(page); await flush();
        h.initialization.reject(new Error('synthetic-sdk-secret')); await work;
        assert.equal(h.requests.length, 1); assert.deepEqual(h.navigations, []);
        assert.ok(!h.nodes['page-status'].textContent.includes('synthetic-sdk-secret'));
        assert.ok(h.nodes['page-status'].textContent.includes('無法'));
    });
    test(`${page} pagehide while init pending prevents later work`, options, async t => {
        const h = harness(t); const work = h.start(page); await flush();
        h.win.dispatchEvent(new Event('pagehide'));
        h.initialization.resolve(); await work;
        assert.equal(h.requests.length, 1); assert.deepEqual(h.navigations, []);
        assert.ok(!h.events.includes('getIDToken'));
    });
}

test('router defaults to whoami only after init', options, async t => {
    const h = harness(t); const work = h.start('router'); await flush();
    assert.deepEqual(h.navigations, []); h.initialization.resolve(); await work;
    assert.deepEqual(h.navigations, ['/liff/whoami.html']);
});

test('router unknown liff.state never navigates', options, async t => {
    const h = harness(t, { search: '?liff.state=https%3A%2F%2Fattacker.invalid' });
    const work = h.start('router'); h.initialization.resolve(); await work;
    assert.deepEqual(h.navigations, []);
});

for (const status of [401, 403, 500]) {
    test(`whoami handles HTTP ${status} without identity disclosure`, options, async t => {
        const h = harness(t, { status }); const work = h.start();
        h.initialization.resolve(); await work;
        assert.equal(h.nodes.identity.hidden, true);
        assert.equal(h.nodes['tenant-id'].textContent, '');
        assert.equal(h.nodes['login-button'].hidden, status !== 401);
        if (status === 403) assert.equal(h.nodes['page-status'].textContent, '請選擇教會');
        if (status === 401) {
            assert.equal(h.nodes['page-status'].textContent, '請重新登入');
            h.nodes['login-button'].dispatchEvent(new Event('click')); await flush();
            assert.ok(h.events.includes('login'));
        }
    });
}

test('whoami no token prompts login without authenticated request', options, async t => {
    const h = harness(t, { noToken: true }); const work = h.start();
    h.initialization.resolve(); await work;
    assert.equal(h.requests.length, 1); assert.equal(h.nodes['login-button'].hidden, false);
});

test('whoami renders server text without HTML execution', options, async t => {
    const text = '<img src=x onerror=alert(1)>';
    const h = harness(t, { identity: { tenantId: TENANT_A, tenantName: text, roles: [text] } });
    const work = h.start(); h.initialization.resolve(); await work;
    assert.equal(h.nodes['tenant-name'].textContent, text);
    assert.equal(h.nodes.roles.textContent, text);
});

test('whoami malformed complete response is not partially published', options, async t => {
    const h = harness(t, { identity: { tenantId: TENANT_A, tenantName: '不應發布', roles: null } });
    const work = h.start(); h.initialization.resolve(); await work;
    assert.equal(h.nodes.identity.hidden, true);
    assert.equal(h.nodes['tenant-name'].textContent, '');
});

for (const [name, config, search, visible] of [
    ['development valid GUID', { isDevelopment: true }, `?probe=${TENANT_B}`, true],
    ['production valid GUID', { isDevelopment: false }, `?probe=${TENANT_B}`, false],
    ['string true is not development', { isDevelopment: 'true' }, `?probe=${TENANT_B}`, false],
    ['invalid probe', { isDevelopment: true }, '?probe=../whoami', false],
    ['empty GUID', { isDevelopment: true }, '?probe=00000000-0000-0000-0000-000000000000', false],
    ['duplicate probe', { isDevelopment: true }, `?probe=${TENANT_A}&probe=${TENANT_B}`, false]
]) {
    test(`probe ${name}`, options, async t => {
        const h = harness(t, { config, search, probeStatus: 403 }); const work = h.start();
        h.initialization.resolve(); await work;
        assert.equal(h.nodes['probe-button'].hidden, !visible);
        h.nodes['probe-button'].dispatchEvent(new Event('click')); await flush();
        assert.equal(h.requests.length, visible ? 3 : 2);
        if (visible) {
            assert.equal(h.requests[2].url, `/api/liff/tenant/${TENANT_B}/ping`);
            assert.equal(h.requests[2].init.headers.Authorization, `Bearer ${TOKEN}`);
            assert.ok(h.nodes['probe-result'].textContent.includes('拒絕'));
            assert.equal(h.nodes['tenant-id'].textContent, TENANT_A);
        }
    });
}

test('pagehide aborts pending fetch and removes login listener', options, async t => {
    let signal;
    const h = harness(t, { fetch: async (url, init, config) => {
        if (url === '/api/liff/config') return response(200, config);
        signal = init.signal;
        return new Promise((_resolve, reject) => {
            signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')), { once: true });
        });
    } });
    const work = h.start(); h.initialization.resolve(); await flush();
    assert.equal(signal.aborted, false); h.win.dispatchEvent(new Event('pagehide')); await work;
    assert.equal(signal.aborted, true); assert.equal(h.nodes.identity.hidden, true);
    h.nodes['login-button'].dispatchEvent(new Event('click')); await flush();
    assert.ok(!h.events.includes('login'));
});

test('separate pages never reuse token or actor data', options, async t => {
    const a = harness(t, { token: 'synthetic-person-a' }); const b = harness(t, { token: 'synthetic-person-b', identity: {
        tenantId: TENANT_B, tenantName: '乙教會', roles: [] } });
    const aw = a.start(), bw = b.start();
    a.initialization.resolve(); b.initialization.resolve(); await Promise.all([aw, bw]);
    assert.equal(a.nodes['tenant-id'].textContent, TENANT_A);
    assert.equal(b.nodes['tenant-id'].textContent, TENANT_B);
    assert.equal(a.requests[1].init.headers.Authorization, 'Bearer synthetic-person-a');
    assert.equal(b.requests[1].init.headers.Authorization, 'Bearer synthetic-person-b');
    a.win.dispatchEvent(new Event('pagehide'));
    assert.equal(a.nodes['tenant-id'].textContent, '');
    assert.equal(b.nodes['tenant-id'].textContent, TENANT_B);
});

test('HTML entrypoints load fixed official SDK and matching deferred scripts', options, () => {
    for (const page of ['index', 'whoami']) {
        const html = fs.readFileSync(path.join(base, `${page}.html`), 'utf8');
        assert.match(html, /lang="zh-Hant"/);
        assert.match(html, /<script defer src="https:\/\/static\.line-scdn\.net\/liff\/edge\/2\/sdk\.js"><\/script>/);
        assert.match(html, /<script defer src="\/liff\/liff-router\.js"><\/script>/);
        if (page === 'whoami') assert.match(html, /<script defer src="\/liff\/whoami\.js"><\/script>/);
        assert.doesNotMatch(html, /onclick=|onload=|localStorage|sessionStorage/);
    }
});

test('browser UMD branches start only matching page', options, async t => {
    for (const page of ['router', 'whoami']) {
        const h = harness(t, { page }); h.win.window = h.win;
        for (const file of ['liff-router.js', 'whoami.js']) {
            vm.runInNewContext(fs.readFileSync(path.join(base, file), 'utf8'), h.win,
                { filename: file, timeout: 1000 });
        }
        h.initialization.resolve(); await flush();
        assert.equal(h.requests.filter(r => r.url === '/api/liff/config').length, 1);
        assert.equal(h.requests.length, page === 'router' ? 1 : 2);
    }
});


for (const page of ['router', 'whoami']) {
    test(`${page} config request aborts on pagehide`, options, async t => {
        let signal;
        const h = harness(t, { fetch: (_url, init) => {
            signal = init.signal;
            return new Promise((_resolve, reject) => signal.addEventListener('abort',
                () => reject(new Error('synthetic-abort')), { once: true }));
        } });
        const work = h.start(page); await flush(); h.win.dispatchEvent(new Event('pagehide'));
        await work; assert.equal(signal.aborted, true); assert.equal(h.events.length, 1);
    });
    test(`${page} init timeout is bounded and never reads token`, options, async t => {
        const h = harness(t); const work = h.start(page); await flush();
        assert.equal(h.timers.size, 1); h.win.expireTimers(); const scope = await work;
        assert.equal(scope.signal.aborted, true); assert.equal(h.requests.length, 1);
        assert.ok(!h.events.includes('getIDToken')); assert.deepEqual(h.navigations, []);
        assert.ok(h.nodes['page-status'].textContent.includes('無法'));
        h.initialization.resolve(); await flush();
        assert.equal(h.requests.length, 1); assert.deepEqual(h.navigations, []);
    });
}

test('request timeout aborts fetch and never publishes identity', options, async t => {
    let signal;
    const h = harness(t, { fetch: (url, init, config) => {
        if (url === '/api/liff/config') return response(200, config);
        signal = init.signal;
        return new Promise((_resolve, reject) => signal.addEventListener('abort',
            () => reject(new Error('synthetic-fetch-secret')), { once: true }));
    } });
    const work = h.start(); h.initialization.resolve(); await flush();
    assert.equal(h.timers.size, 1); h.win.expireTimers(); await work;
    assert.equal(signal.aborted, true); assert.equal(h.nodes.identity.hidden, true);
    assert.ok(!h.nodes['page-status'].textContent.includes('synthetic-fetch-secret'));
});

test('pagehide during JSON consumption prevents late publication', options, async t => {
    const json = deferred();
    t.after(() => json.resolve({ tenantId: TENANT_A, tenantName: '過期結果', roles: [] }));
    const h = harness(t, { fetch: (url, _init, config) => url === '/api/liff/config'
        ? response(200, config) : { status: 200, ok: true, json: () => json.promise } });
    const work = h.start(); h.initialization.resolve(); await flush();
    h.win.dispatchEvent(new Event('pagehide')); await work;
    json.resolve({ tenantId: TENANT_A, tenantName: '過期結果', roles: [] }); await flush();
    assert.equal(h.nodes.identity.hidden, true); assert.equal(h.nodes['tenant-name'].textContent, '');
});

test('probe repeated click stays single flight and reacquires token', options, async t => {
    const ping = deferred(); let number = 0, signal;
    t.after(() => ping.resolve(response(200, { ok: true })));
    const h = harness(t, { config: { isDevelopment: true }, search: `?probe=${TENANT_B}`,
        token: () => `synthetic-generation-${++number}`,
        fetch: (url, init, config) => {
            if (url === '/api/liff/config') return response(200, config);
            if (url === '/api/liff/whoami') return response(200,
                { tenantId: TENANT_A, tenantName: '甲', roles: [] });
            signal = init.signal; return ping.promise;
        } });
    const work = h.start(); h.initialization.resolve(); await work;
    for (let i = 0; i < 20; i++) h.nodes['probe-button'].dispatchEvent(new Event('click'));
    await flush(); assert.equal(h.requests.length, 3); assert.equal(number, 2);
    assert.equal(h.requests[1].init.headers.Authorization, 'Bearer synthetic-generation-1');
    assert.equal(h.requests[2].init.headers.Authorization, 'Bearer synthetic-generation-2');
    h.win.dispatchEvent(new Event('pagehide')); assert.equal(signal.aborted, true);
    ping.resolve(response(200, { ok: true })); await flush();
    assert.equal(h.nodes['probe-result'].textContent, '');
    h.nodes['probe-button'].dispatchEvent(new Event('click')); await flush();
    assert.equal(h.requests.length, 3);
});

for (const status of [200, 401, 500]) {
    test(`probe HTTP ${status} has bounded fixed result`, options, async t => {
        const h = harness(t, { config: { isDevelopment: true },
            search: `?probe=${TENANT_B}`, probeStatus: status });
        const work = h.start(); h.initialization.resolve(); await work;
        h.nodes['probe-button'].dispatchEvent(new Event('click')); await flush();
        if (status === 401) {
            assert.equal(h.nodes['page-status'].textContent, '請重新登入');
            assert.equal(h.nodes.identity.hidden, true);
        } else {
            assert.ok(h.nodes['probe-result'].textContent.includes(status === 200 ? '允許' : '無法'));
            assert.equal(h.nodes['tenant-id'].textContent, TENANT_A);
        }
        assert.equal(h.requests.length, 3);
    });
}

for (const state of ['?liff.state=', '?liff.state=whoami&liff.state=whoami']) {
    test(`router refuses ambiguous or empty state ${state}`, options, async t => {
        const h = harness(t, { search: state }); const work = h.start('router');
        h.initialization.resolve(); await work; assert.deepEqual(h.navigations, []);
    });
}

test('error response body is cancelled and not rendered', options, async t => {
    let cancelled = 0;
    const h = harness(t, { fetch: (url, _init, config) => url === '/api/liff/config'
        ? response(200, config) : { ok: false, status: 401,
            body: { async cancel() { cancelled++; } },
            async json() { assert.fail('不得讀取錯誤本文'); } } });
    const work = h.start(); h.initialization.resolve(); await work;
    assert.equal(cancelled, 1); assert.equal(h.nodes['page-status'].textContent, '請重新登入');
});

test('forty concurrent A/B page lifecycles drain timers listeners and visible data', options, async t => {
    const pages = Array.from({ length: 40 }, (_, i) => harness(t, {
        token: `synthetic-person-${i}`, identity: { tenantId: i % 2 ? TENANT_B : TENANT_A,
            tenantName: `教會${i}`, roles: ['相同名稱', '相同名稱'] } }));
    const work = pages.map(h => h.start());
    pages.forEach(h => h.initialization.resolve()); await Promise.all(work);
    for (const [i, h] of pages.entries()) {
        assert.equal(h.nodes['tenant-id'].textContent, i % 2 ? TENANT_B : TENANT_A);
        assert.equal(h.nodes['tenant-name'].textContent, `教會${i}`);
        assert.equal(h.nodes.roles.textContent, '相同名稱、相同名稱');
        assert.equal(h.requests[1].init.headers.Authorization, `Bearer synthetic-person-${i}`);
        h.win.dispatchEvent(new Event('pagehide'));
        assert.equal(h.nodes['tenant-id'].textContent, '');
        assert.equal(h.timers.size, 0); assert.equal(h.win.listenerCount, 0);
        assert.ok(Object.values(h.nodes).every(node => node.listenerCount === 0));
    }
});


test('probe GUID with trailing newline is not a valid target', options, async t => {
    const h = harness(t, { config: { isDevelopment: true }, search: `?probe=${TENANT_B}%0a` });
    const work = h.start(); h.initialization.resolve(); await work;
    assert.equal(h.nodes['probe-button'].hidden, true);
    h.nodes['probe-button'].dispatchEvent(new Event('click')); await flush();
    assert.equal(h.requests.length, 2);
});


for (const page of ['router', 'whoami']) {
    test(`${page} whitespace-only ID is unconfigured`, options, async t => {
        const h = harness(t, { config: { liffId: '   ' } }); await h.start(page);
        assert.equal(h.nodes['page-status'].textContent, '尚未設定');
        assert.equal(h.requests.length, 1); assert.ok(!h.events.includes('init'));
    });
}
