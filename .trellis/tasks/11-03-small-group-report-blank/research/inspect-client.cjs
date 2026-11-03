'use strict';
// 唯讀研究：解析實際 Razor 內的腳本並以無網路替身檢查全域符號。
// 不啟動網站、不載入個資、不呼叫 AJAX；不把此探測當成真實瀏覽器重現。
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const cp = require('node:child_process');
const crypto = require('node:crypto');
const root = path.resolve(__dirname, '../../../..');
const product = path.join(root, 'SpeechMessageProducts.ChurchReport');
const views = [
    'Views/Shared/_Layout.cshtml',
    'Views/Home/IntegrateView.cshtml',
    'Views/Home/_GeneralGroupGrids.cshtml',
    'Views/Shared/_MemberInfoDetailPopupHost.cshtml'
];
const results = { assets: [], inlineScripts: [], globals: {}, lifecycle: {}, baseline: {} };
// Git 物件可能儲存 LF，工作區依規範使用 CRLF；比較文字內容時只正規化換行，避免假差異。
const hash = buffer => crypto.createHash('sha256')
    .update(buffer.toString('utf8').replace(/\r\n/g, '\n')).digest('hex');
for (const file of views) {
    const text = fs.readFileSync(path.join(product, file), 'utf8');
    for (const match of text.matchAll(/(?:src|href)="(~\/[^"]+)"/g)) {
        const relative = match[1].slice(2).split('?')[0];
        if (!/\.(?:js|css)$/.test(relative)) continue;
        const asset = path.join(product, 'wwwroot', relative);
        const exists = fs.existsSync(asset);
        const entry = { view: file, path: relative, exists };
        if (!exists) {
            entry.trackedAtBaseline = cp.spawnSync('git',
                ['cat-file', '-e', '73b6e6cf:SpeechMessageProducts.ChurchReport/wwwroot/' + relative],
                { cwd: root, stdio: 'ignore' }).status === 0;
        }
        results.assets.push(entry);
    }
    const gitPath = 'SpeechMessageProducts.ChurchReport/' + file;
    const baseline = cp.execFileSync('git', ['show', '73b6e6cf:' + gitPath],
        { cwd: root, maxBuffer: 16 * 1024 * 1024 });
    results.baseline[file] = hash(baseline) === hash(fs.readFileSync(path.join(product, file)));
}
for (const relative of [
    'js/devextreme/dx.all.js', 'js/devextreme/aspnet/dx.aspnet.mvc.js',
    'js/collection-load-coordinator.js'
]) {
    const gitPath = 'SpeechMessageProducts.ChurchReport/wwwroot/' + relative;
    const baseline = cp.execFileSync('git', ['show', '73b6e6cf:' + gitPath],
        { cwd: root, maxBuffer: 16 * 1024 * 1024 });
    const bytes = fs.readFileSync(path.join(product, 'wwwroot', relative));
    results.baseline[relative] = hash(baseline) === hash(bytes);
}
const readyCallbacks = [];
const browserChain = {
    ready(callback) { readyCallbacks.push(callback); return this; },
    on() { return this; },
    off() { return this; },
    dxPopup() { return this; }
};
const fakeJquery = function (argument) {
    if (typeof argument === 'function') readyCallbacks.push(argument);
    return browserChain;
};
fakeJquery.ajax = () => { throw new Error('Network prohibited in triage.'); };
const sandbox = {
    document: {},
    console: { log() {}, error() {}, warn() {} },
    $: fakeJquery,
    jQuery: fakeJquery,
    addEventListener() {},
    sessionStorage: { setItem() {}, getItem() { return 'zh-tw'; } },
    DevExpress: { localization: { loadMessages() {}, locale() {} } },
    CollectionLoadCoordinator: require(path.join(product, 'wwwroot/js/collection-load-coordinator.js'))
};
sandbox.window = sandbox;
vm.createContext(sandbox);
// 實際 layout 先於 body，其後為部分頁、主要頁的腳本；此探測不執行 Razor 產生的元件初始化。
for (const file of [views[0], views[3], views[2], views[1]]) {
    const text = fs.readFileSync(path.join(product, file), 'utf8');
    let index = 0;
    for (const match of text.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
        if (/\bsrc=/.test(match[1]) || !match[2].trim()) continue;
        const entry = { view: file, index: ++index, parses: false, definesWithoutNetwork: false };
        try {
            const script = new vm.Script(match[2], { filename: file + '#' + index });
            entry.parses = true;
            script.runInContext(sandbox);
            entry.definesWithoutNetwork = true;
        } catch (error) {
            entry.errorType = error.name;
            // 只輸出腳本診斷；沒有讀取 API、Cookie、Session 或其他實際執行資料。
            entry.reason = error.message;
        }
        results.inlineScripts.push(entry);
    }
}
for (const symbol of [
    'contactImageCellTemplate', 'memberInfoDetailNameCellTemplate',
    'membershipStatusDataSource', 'spiritualIdentityDataSource', 'visitDataSource',
    'initializeWeeklyReportGridPublicationGuard', 'openMemberInfoDetailPopup'
]) {
    results.globals[symbol] = Array.isArray(sandbox[symbol]) ? 'array' : typeof sandbox[symbol];
}
// 分別注入 OnInitialized 時沒有 DataSource、已有 DataSource 的兩種生命週期。
// 只證明 adapter 的必要條件；尚未證明使用者瀏覽器實際落在哪個時機。
for (const hasDataSource of [false, true]) {
    const owner = {};
    const store = { load() { return Promise.resolve({ data: [] }); } };
    const component = {
        element() { return [owner]; },
        getDataSource() { return hasDataSource ? { store() { return store; } } : null; },
        dispose() {}
    };
    const label = hasDataSource ? 'dataSourceReady' : 'dataSourceMissing';
    try {
        sandbox.initializeWeeklyReportGridPublicationGuard({ component });
        results.lifecycle[label] = { succeeds: true };
    } catch (error) {
        results.lifecycle[label] = { succeeds: false, errorType: error.name, reason: error.message };
    } finally {
        sandbox.CollectionLoadCoordinator.disposeOwner(owner);
    }
}
sandbox.disposeIntegratePageLoadCoordinator();
results.readyCallbacksNotExecuted = readyCallbacks.length;
results.noAjaxExecuted = true;
process.stdout.write(JSON.stringify(results, null, 2) + '\n');
