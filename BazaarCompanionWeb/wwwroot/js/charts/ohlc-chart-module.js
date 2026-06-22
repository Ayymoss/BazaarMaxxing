// Lightweight Charts v5 OHLC chart for BazaarCompanion.
// Replaces the old KLineCharts integration. Indicators are computed server-side (C#) and
// delivered as plain series data; this module only renders, pages, and updates.
//
// Data contract (all times are UNIX seconds):
//   payload = { candles:[{time,open,high,low,close}], volume:[{time,value,color}],
//               ma50, ma250, bbUpper, bbMiddle, bbLower, macdLine, signal, rsi, ask : [{time,value}],
//               macdHist:[{time,value,color}] }

const PAGE = 200;
const reg = {};

function lc() {
    const L = window.LightweightCharts;
    if (!L) throw new Error('LightweightCharts global not loaded');
    return L;
}

const C = {
    up: '#16c784', down: '#f6465d',
    text: '#9aa0aa', grid: 'rgba(148,163,184,0.06)', border: '#2a2f38',
    ma50: '#d99a00', ma250: '#5b8def',
    bb: 'rgba(148,163,184,0.5)', bbMid: 'rgba(148,163,184,0.32)',
    macd: '#d99a00', signal: '#5b8def', rsi: '#c98bff', ask: '#fb923c',
    crosshairLabel: '#2a2f38',
};

function baseOptions(opts) {
    const intraday = opts.interval < 1440;
    return {
        autoSize: true,
        layout: {
            background: { type: 'solid', color: 'transparent' },
            textColor: C.text,
            fontFamily: "'IBM Plex Sans', system-ui, sans-serif",
            fontSize: 11,
            panes: { separatorColor: 'rgba(71,85,105,0.5)', separatorHoverColor: 'rgba(148,163,184,0.4)', enableResize: true },
            attributionLogo: false,
        },
        grid: { vertLines: { color: C.grid }, horzLines: { color: C.grid } },
        rightPriceScale: { borderColor: C.border, scaleMargins: { top: 0.1, bottom: 0.1 } },
        leftPriceScale: { visible: !!opts.flags.ask, borderColor: C.border, scaleMargins: { top: 0.1, bottom: 0.1 } },
        timeScale: { borderColor: C.border, timeVisible: intraday, secondsVisible: false, rightOffset: 4 },
        crosshair: {
            mode: lc().CrosshairMode.Normal,
            vertLine: { color: 'rgba(148,163,184,0.45)', width: 1, style: 3, labelBackgroundColor: C.crosshairLabel },
            horzLine: { color: 'rgba(148,163,184,0.45)', width: 1, style: 3, labelBackgroundColor: C.crosshairLabel },
        },
    };
}

const lineOpts = (color, extra) => ({
    color, lineWidth: 1, priceLineVisible: false, lastValueVisible: false,
    crosshairMarkerVisible: false, ...extra,
});

// ---------------------------------------------------------------- build / rebuild
function build(id) {
    const e = reg[id];
    const LWC = lc();
    const el = document.getElementById(id);
    if (!el) return;

    // Preserve viewport across a rebuild (toggling indicators).
    let savedRange = null;
    if (e.chart) {
        try { savedRange = e.chart.timeScale().getVisibleLogicalRange(); } catch { /* */ }
        teardown(e);
    }

    const f = e.opts.flags;
    const chart = LWC.createChart(el, baseOptions(e.opts));
    const s = {};

    // --- pane 0: price + overlays ---
    s.candle = chart.addSeries(LWC.CandlestickSeries, {
        upColor: C.up, downColor: C.down, borderUpColor: C.up, borderDownColor: C.down,
        wickUpColor: C.up, wickDownColor: C.down,
        priceFormat: { type: 'price', precision: 2, minMove: 0.01 },
    }, 0);
    if (f.bb) {
        s.bbUpper = chart.addSeries(LWC.LineSeries, lineOpts(C.bb), 0);
        s.bbLower = chart.addSeries(LWC.LineSeries, lineOpts(C.bb), 0);
        s.bbMiddle = chart.addSeries(LWC.LineSeries, lineOpts(C.bbMid, { lineStyle: 2 }), 0);
    }
    if (f.ma) {
        s.ma50 = chart.addSeries(LWC.LineSeries, lineOpts(C.ma50, { lineWidth: 2 }), 0);
        s.ma250 = chart.addSeries(LWC.LineSeries, lineOpts(C.ma250, { lineWidth: 2 }), 0);
    }
    if (f.ask) {
        s.ask = chart.addSeries(LWC.LineSeries, lineOpts(C.ask, { lineWidth: 2, priceScaleId: 'left' }), 0);
    }

    // --- sub-panes: assigned sequentially among the enabled ones ---
    let pane = 1;
    if (f.vol) {
        s.volume = chart.addSeries(LWC.HistogramSeries, {
            priceFormat: { type: 'volume' }, priceScaleId: 'right', lastValueVisible: false, priceLineVisible: false,
        }, pane);
        pane++;
    }
    if (f.macd) {
        s.macdHist = chart.addSeries(LWC.HistogramSeries, {
            priceFormat: { type: 'price', precision: 3, minMove: 0.001 }, lastValueVisible: false, priceLineVisible: false,
        }, pane);
        s.macdLine = chart.addSeries(LWC.LineSeries, lineOpts(C.macd, { lineWidth: 2 }), pane);
        s.signal = chart.addSeries(LWC.LineSeries, lineOpts(C.signal), pane);
        pane++;
    }
    if (f.rsi) {
        s.rsi = chart.addSeries(LWC.LineSeries, lineOpts(C.rsi, { lineWidth: 2 }), pane);
        for (const [v, col] of [[70, 'rgba(239,68,68,0.35)'], [50, 'rgba(148,163,184,0.25)'], [30, 'rgba(16,185,129,0.35)']]) {
            s.rsi.createPriceLine({ price: v, color: col, lineWidth: 1, lineStyle: 2, axisLabelVisible: true });
        }
        pane++;
    }

    applyData(s, e.cache, f);

    // Pane sizing: price dominant, indicators compact.
    try {
        const panes = chart.panes();
        if (panes[0]) panes[0].setStretchFactor(6);
        for (let i = 1; i < panes.length; i++) panes[i].setStretchFactor(2);
    } catch { /* older API */ }

    chart.subscribeCrosshairMove(p => onCrosshair(id, p));
    const ts = chart.timeScale();
    const onRange = r => { if (r && r.from <= 10) loadOlder(id); };
    ts.subscribeVisibleLogicalRangeChange(onRange);

    if (savedRange) {
        ts.setVisibleLogicalRange(savedRange);
    } else {
        const n = e.cache.candles.length;
        if (n > 0) ts.setVisibleLogicalRange({ from: Math.max(0, n - 150), to: n - 1 });
    }

    e.chart = chart;
    e.series = s;
    e.unsub = () => { try { ts.unsubscribeVisibleLogicalRangeChange(onRange); } catch { /* */ } };
    renderLegend(id, lastValues(e.cache));
}

function teardown(e) {
    try { e.unsub?.(); } catch { /* */ }
    try { e.chart?.remove(); } catch { /* */ }
    e.chart = null;
    e.series = null;
}

function applyData(s, cache, f) {
    s.candle.setData(cache.candles);
    if (f.bb) { s.bbUpper.setData(cache.bbUpper); s.bbLower.setData(cache.bbLower); s.bbMiddle.setData(cache.bbMiddle); }
    if (f.ma) { s.ma50.setData(cache.ma50); s.ma250.setData(cache.ma250); }
    if (f.ask) s.ask.setData(cache.ask);
    if (f.vol) s.volume.setData(cache.volume);
    if (f.macd) { s.macdHist.setData(cache.macdHist); s.macdLine.setData(cache.macdLine); s.signal.setData(cache.signal); }
    if (f.rsi) s.rsi.setData(cache.rsi);
}

const KEYS = ['candles', 'volume', 'ma50', 'ma250', 'bbUpper', 'bbMiddle', 'bbLower', 'macdHist', 'macdLine', 'signal', 'rsi', 'ask'];

function emptyCache() {
    const c = {};
    for (const k of KEYS) c[k] = [];
    return c;
}

function fillCache(cache, payload) {
    for (const k of KEYS) cache[k] = payload[k] || [];
}

// ---------------------------------------------------------------- public API
export function createOhlcChart(id, payload, opts) {
    if (reg[id]) disposeOhlcChart(id);
    const cache = emptyCache();
    fillCache(cache, payload);
    reg[id] = {
        chart: null, series: null, cache, unsub: null,
        opts: { productKey: opts.productKey, interval: opts.interval, flags: opts.flags },
        meta: { loading: false, hasMore: (payload.candles?.length || 0) > 0 },
    };
    build(id);
    return true;
}

/** Toggle indicators: update flags and rebuild from cache, preserving the viewport. */
export function applyOhlcConfig(id, flags) {
    const e = reg[id];
    if (!e) return;
    e.opts.flags = flags;
    build(id);
}

export function disposeOhlcChart(id) {
    const e = reg[id];
    if (!e) return;
    teardown(e);
    delete reg[id];
}

export function resizeOhlcChart(id) {
    // autoSize handles container changes; nudge the time scale just in case.
    try { reg[id]?.chart?.timeScale().scrollToRealTime?.(); } catch { /* */ }
}

// ---------------------------------------------------------------- live tick
export function updateOhlcTick(id, t) {
    const e = reg[id];
    if (!e || !e.series) return;
    const s = e.series, c = e.cache, f = e.opts.flags;

    push(c.candles, t.candle); s.candle.update(t.candle);
    if (t.volume) { push(c.volume, t.volume); if (f.vol) s.volume.update(t.volume); }
    pushLine(c.ma50, t.ma50, f.ma && s.ma50);
    pushLine(c.ma250, t.ma250, f.ma && s.ma250);
    pushLine(c.bbUpper, t.bbUpper, f.bb && s.bbUpper);
    pushLine(c.bbMiddle, t.bbMiddle, f.bb && s.bbMiddle);
    pushLine(c.bbLower, t.bbLower, f.bb && s.bbLower);
    pushLine(c.macdHist, t.macdHist, f.macd && s.macdHist);
    pushLine(c.macdLine, t.macdLine, f.macd && s.macdLine);
    pushLine(c.signal, t.signal, f.macd && s.signal);
    pushLine(c.rsi, t.rsi, f.rsi && s.rsi);
    pushLine(c.ask, t.ask, f.ask && s.ask);

    renderLegend(id, lastValues(c));
}

function push(arr, point) {
    if (!point) return;
    if (arr.length && arr[arr.length - 1].time === point.time) arr[arr.length - 1] = point;
    else arr.push(point);
}

function pushLine(arr, point, series) {
    if (!point) return;
    push(arr, point);
    if (series) series.update(point);
}

// ---------------------------------------------------------------- paging
async function loadOlder(id) {
    const e = reg[id];
    if (!e || e.meta.loading || !e.meta.hasMore) return;
    const earliest = e.cache.candles[0]?.time;
    if (!earliest) { e.meta.hasMore = false; return; }

    e.meta.loading = true;
    try {
        const beforeMs = earliest * 1000;
        const key = e.opts.productKey;
        const url = key.startsWith('index:')
            ? `/api/chart/index/${encodeURIComponent(key.slice(6))}/${e.opts.interval}?before=${beforeMs}&limit=${PAGE}`
            : `/api/chart/${encodeURIComponent(key)}/${e.opts.interval}?before=${beforeMs}&limit=${PAGE}`;
        const res = await fetch(url);
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const page = await res.json();

        const added = page.candles?.length || 0;
        if (added === 0) { e.meta.hasMore = false; return; }
        if (added < PAGE) e.meta.hasMore = false;

        for (const k of KEYS) e.cache[k] = (page[k] || []).concat(e.cache[k]);

        const range = e.chart.timeScale().getVisibleLogicalRange();
        applyData(e.series, e.cache, e.opts.flags);
        if (range) e.chart.timeScale().setVisibleLogicalRange({ from: range.from + added, to: range.to + added });
    } catch (err) {
        console.error('[ohlc] paging error', err);
    } finally {
        e.meta.loading = false;
    }
}

// ---------------------------------------------------------------- legend
const pick = arr => (arr && arr.length ? arr[arr.length - 1].value : null);

function lastValues(cache) {
    const c = cache.candles[cache.candles.length - 1];
    if (!c) return null;
    return {
        o: c.open, h: c.high, l: c.low, cl: c.close, up: c.close >= c.open,
        ask: pick(cache.ask), vol: pick(cache.volume),
        ma50: pick(cache.ma50), ma250: pick(cache.ma250),
        bbU: pick(cache.bbUpper), bbL: pick(cache.bbLower),
        macd: pick(cache.macdLine), sig: pick(cache.signal), rsi: pick(cache.rsi),
    };
}

function onCrosshair(id, p) {
    const e = reg[id];
    if (!e) return;
    if (!p || !p.time || !p.seriesData || !p.seriesData.size) { renderLegend(id, lastValues(e.cache)); return; }
    const s = e.series;
    const c = p.seriesData.get(s.candle);
    if (!c) { renderLegend(id, lastValues(e.cache)); return; }
    const g = ser => { const v = ser && p.seriesData.get(ser); return v ? (v.value ?? null) : null; };
    renderLegend(id, {
        o: c.open, h: c.high, l: c.low, cl: c.close, up: c.close >= c.open,
        ask: g(s.ask), vol: g(s.volume), ma50: g(s.ma50), ma250: g(s.ma250),
        bbU: g(s.bbUpper), bbL: g(s.bbLower), macd: g(s.macdLine), sig: g(s.signal), rsi: g(s.rsi),
    });
}

const n2 = v => v == null ? '—' : v.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const n3 = v => v == null ? '—' : v.toFixed(3);
const vol = v => v == null ? '—' : v >= 1e6 ? (v / 1e6).toFixed(2) + 'M' : v >= 1e3 ? (v / 1e3).toFixed(1) + 'K' : v.toFixed(0);

function renderLegend(id, x) {
    const host = document.getElementById(id + '-legend');
    if (!host) return;
    if (!x) { host.innerHTML = ''; return; }
    const col = x.up ? C.up : C.down;
    const item = (label, val, c) => `<span class="ohlc-lg-item"><i style="color:${c}">${label}</i>${val}</span>`;
    const parts = [
        `<span class="ohlc-lg-ohlc" style="color:${col}">O<b>${n2(x.o)}</b> H<b>${n2(x.h)}</b> L<b>${n2(x.l)}</b> C<b>${n2(x.cl)}</b></span>`,
    ];
    if (x.ask != null) parts.push(item('ASK', n2(x.ask), C.ask));
    if (x.ma50 != null) parts.push(item('MA50', n2(x.ma50), C.ma50));
    if (x.ma250 != null) parts.push(item('MA250', n2(x.ma250), C.ma250));
    if (x.bbU != null) parts.push(item('BB', `${n2(x.bbU)}/${n2(x.bbL)}`, C.bb));
    if (x.vol != null) parts.push(item('VOL', vol(x.vol), C.text));
    if (x.macd != null) parts.push(item('MACD', n3(x.macd), C.macd));
    if (x.sig != null) parts.push(item('SIG', n3(x.sig), C.signal));
    if (x.rsi != null) parts.push(item('RSI', n2(x.rsi), C.rsi));
    host.innerHTML = parts.join('');
}
