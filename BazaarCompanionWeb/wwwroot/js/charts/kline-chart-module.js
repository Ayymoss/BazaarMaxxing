// KLineChart integration module
// Documentation: https://klinecharts.com/en-US/guide/quick-start

// Store chart instances
const klineCharts = {};

// Track indicator pane IDs for each chart (for proper removal)
const indicatorPanes = {};

// Store real-time subscription callbacks for each chart
const realtimeCallbacks = {};

// Store the latest formatted data for each chart (needed for data loader re-subscription)
const chartDataCache = {};

// Store chart metadata for lazy loading (productKey, interval)
const chartMetadata = {};

// Track loading state to prevent multiple simultaneous loads
const loadingState = {};

// Font family used throughout the app
const fontFamily = 'Inter, system-ui, -apple-system, sans-serif';

// Track if custom indicators have been registered
let askLineIndicatorRegistered = false;

/**
 * Register the custom ASK line indicator globally
 * Per KLineChart docs: custom indicators must use registerIndicator before createIndicator
 * @param {object} klinecharts - The klinecharts object from waitForKLineCharts
 */
function registerAskLineIndicator(klinecharts) {
    if (askLineIndicatorRegistered) return;

    klinecharts.registerIndicator({
        name: 'ASK_LINE',
        shortName: 'ASK',
        series: 'price', // Share y-axis with candles
        figures: [
            { key: 'askClose', title: 'ASK: ', type: 'line' }
        ],
        calc: (kLineDataList) => {
            return kLineDataList.map(kLineData => ({
                // Return null when askClose is missing/zero - this creates gaps in the line for old data
                askClose: (kLineData.askClose && kLineData.askClose !== 0) ? kLineData.askClose : null
            }));
        },
        styles: {
            lines: [
                {
                    color: '#ef4444', // Red ASK line
                    size: 1.5,
                    style: 'solid'
                }
            ]
        }
    });

    askLineIndicatorRegistered = true;
}

// Dark theme styles matching your existing theme
const darkThemeStyles = {
    grid: {
        horizontal: {
            color: '#334155'
        },
        vertical: {
            color: '#334155'
        }
    },
    candle: {
        priceMark: {
            last: {
                upColor: '#10b981',
                downColor: '#ef4444',
                noChangeColor: '#94a3b8',
                text: {
                    family: fontFamily,
                    size: 11,
                    color: '#ffffff'
                }
            },
            high: {
                color: '#d1d5db',
                textFamily: fontFamily,
                textSize: 10
            },
            low: {
                color: '#d1d5db',
                textFamily: fontFamily,
                textSize: 10
            }
        },
        tooltip: {
            title: {
                family: fontFamily,
                size: 12,
                color: '#94a3b8'
            },
            legend: {
                family: fontFamily,
                size: 11,
                color: '#d1d5db'
            }
        },
        bar: {
            upColor: '#10b981',
            downColor: '#ef4444',
            noChangeColor: '#94a3b8',
            upBorderColor: '#10b981',
            downBorderColor: '#ef4444',
            noChangeBorderColor: '#94a3b8',
            upWickColor: '#10b981',
            downWickColor: '#ef4444',
            noChangeWickColor: '#94a3b8'
        }
    },
    indicator: {
        tooltip: {
            title: {
                family: fontFamily,
                size: 11,
                color: '#94a3b8'
            },
            legend: {
                family: fontFamily,
                size: 11,
                color: '#d1d5db'
            }
        },
        lastValueMark: {
            text: {
                family: fontFamily,
                size: 11
            }
        }
    },
    xAxis: {
        axisLine: {
            color: '#475569'
        },
        tickLine: {
            color: '#475569'
        },
        tickText: {
            color: '#94a3b8',
            family: fontFamily,
            size: 11
        }
    },
    yAxis: {
        axisLine: {
            color: '#475569'
        },
        tickLine: {
            color: '#475569'
        },
        tickText: {
            color: '#94a3b8',
            family: fontFamily,
            size: 11
        }
    },
    crosshair: {
        horizontal: {
            line: {
                color: '#64748b'
            },
            text: {
                backgroundColor: '#334155',
                color: '#d1d5db',
                family: fontFamily,
                size: 11
            }
        },
        vertical: {
            line: {
                color: '#64748b'
            },
            text: {
                backgroundColor: '#334155',
                color: '#d1d5db',
                family: fontFamily,
                size: 11
            }
        }
    },
    separator: {
        color: '#334155'
    }
};

/**
 * Wait for KLineCharts library to be available
 */
function waitForKLineCharts(maxWait = 5000) {
    return new Promise((resolve, reject) => {
        if (window.klinecharts) {
            resolve(window.klinecharts);
            return;
        }

        const startTime = Date.now();
        const checkInterval = setInterval(() => {
            if (window.klinecharts) {
                clearInterval(checkInterval);
                resolve(window.klinecharts);
            } else if (Date.now() - startTime > maxWait) {
                clearInterval(checkInterval);
                reject(new Error('KLineCharts library not found'));
            }
        }, 50);
    });
}

/**
 * Create a KLineChart with MACD and VOL indicators enabled by default
 * @param {string} containerId - The container element ID
 * @param {Array} data - OHLC data array with { timestamp, open, high, low, close, volume }
 * @param {Object} options - Additional options (productKey, interval, productName)
 */
export async function createKLineChart(containerId, data, options = {}) {
    try {
        //console.log(`[KLineChart] Creating chart for ${containerId}`, { options, dataLength: data?.length });
        const klinecharts = await waitForKLineCharts();

        const container = document.getElementById(containerId);
        if (!container) {
            console.error('[KLineChart] Container not found:', containerId);
            return;
        }

        // Dispose existing chart if any
        disposeKLineChart(containerId);

        const interval = options.interval || 15;
        // Store metadata for lazy loading
        chartMetadata[containerId] = {
            productKey: options.productKey || null,
            interval: interval,
            hasMoreHistory: true // Assume there's more until we get an empty response
        };

        // Create chart with layout including VOL and MACD by default
        const chart = klinecharts.init(container, {
            layout: [
                {
                    type: 'candle',
                    options: {
                        id: 'candle_pane'
                    }
                },
                {
                    type: 'indicator',
                    content: ['VOL'],
                    options: {
                        id: 'vol_pane',
                        height: 80,
                        minHeight: 60
                    }
                },
                {
                    type: 'indicator',
                    content: ['MACD'],
                    options: {
                        id: 'macd_pane',
                        height: 100,
                        minHeight: 80
                    }
                },
                { type: 'xAxis' }
            ],
            styles: darkThemeStyles,
            locale: 'en-US'
        });

        // Store reference
        klineCharts[containerId] = chart;

        // Initialize indicator tracking
        indicatorPanes[containerId] = {};

        try {
            const volIndicators = chart.getIndicators({ name: 'VOL' });
            if (volIndicators && volIndicators.length > 0) {
                indicatorPanes[containerId]['VOL'] = {
                    indicatorId: volIndicators[0].id,
                    paneId: volIndicators[0].paneId || 'vol_pane'
                };
            }

            const macdIndicators = chart.getIndicators({ name: 'MACD' });
            if (macdIndicators && macdIndicators.length > 0) {
                indicatorPanes[containerId]['MACD'] = {
                    indicatorId: macdIndicators[0].id,
                    paneId: macdIndicators[0].paneId || 'macd_pane'
                };
            }
        } catch (e) {
            console.warn('[KLineChart] Error querying initial indicators:', e);
            indicatorPanes[containerId] = {
                'VOL': { indicatorId: null, paneId: 'vol_pane' },
                'MACD': { indicatorId: null, paneId: 'macd_pane' }
            };
        }

        // Set symbol and period info
        chart.setSymbol({ ticker: options.productName || 'Product' });

        // Set correct period based on interval
        // CandleInterval enum values are in minutes — map to appropriate KLineChart period types
        if (interval >= 10080) {
            chart.setPeriod({ span: Math.round(interval / 10080), type: 'week' });
        } else if (interval >= 1440) {
            chart.setPeriod({ span: Math.round(interval / 1440), type: 'day' });
        } else if (interval >= 60) {
            chart.setPeriod({ span: Math.round(interval / 60), type: 'hour' });
        } else {
            chart.setPeriod({ span: interval, type: 'minute' });
        }
        //console.log(`[KLineChart] Set period to ${interval}m`);

        // Apply data if provided
        if (data && data.length > 0) {
            const formattedData = data.map(item => ({
                timestamp: typeof item.time === 'number'
                    ? (item.time < 10000000000 ? item.time * 1000 : item.time)
                    : new Date(item.time).getTime(),
                open: item.open,
                high: item.high,
                low: item.low,
                close: item.close,
                volume: item.volume || 0,
                askClose: item.askClose // No fallback - null/0 means no ASK data
            }));

            // Cache the data
            chartDataCache[containerId] = formattedData;

            // v10 uses setDataLoader with getBars for all data operations.
            // KNOWN ISSUE: for backward requests, KLineChart passes the LATEST bar's
            // timestamp (not earliest). We work around this by ignoring its timestamp
            // and using our cache's earliest bar. After fetching, we merge into cache
            // and call resetData() which re-triggers getBars({type:'init'}) with the
            // full merged dataset, then scrollToTimestamp() to restore the viewport.
            chart.setDataLoader({
                getBars: async ({ type, timestamp, callback }) => {
                    const meta = chartMetadata[containerId];

                    if (!type || type === 'init') {
                        const cached = chartDataCache[containerId] || formattedData;
                        const hasMore = meta?.hasMoreHistory ?? true;
                        if (meta) meta.initComplete = true;
                        callback(cached, { backward: hasMore, forward: false });
                        return;
                    }

                    if (type === 'forward') {
                        callback([], { backward: false, forward: false });
                        return;
                    }

                    // BACKWARD
                    if (!meta?.initComplete || !meta?.productKey || !meta.hasMoreHistory) {
                        callback([], { backward: false, forward: false });
                        return;
                    }

                    if (loadingState[containerId]) {
                        callback([], { backward: true, forward: false });
                        return;
                    }

                    // Use our cache's earliest timestamp (not KLineChart's, which is the latest bar)
                    const cache = chartDataCache[containerId];
                    const beforeTs = cache?.length > 0 ? cache[0].timestamp : null;

                    if (!beforeTs) {
                        callback([], { backward: false, forward: false });
                        return;
                    }

                    // Guard: don't re-fetch the same boundary
                    if (meta.lastBackwardTs === beforeTs) {
                        meta.hasMoreHistory = false;
                        callback([], { backward: false, forward: false });
                        return;
                    }

                    // Return empty for now — we'll resetData after fetching
                    callback([], { backward: true, forward: false });

                    loadingState[containerId] = true;
                    meta.lastBackwardTs = beforeTs;

                    try {
                        const url = meta.productKey?.startsWith('index:')
                            ? `/api/chart/index/${encodeURIComponent(meta.productKey.slice(6))}/${meta.interval}?before=${beforeTs}&limit=200`
                            : `/api/chart/${encodeURIComponent(meta.productKey)}/${meta.interval}?before=${beforeTs}&limit=200`;
                        const response = await fetch(url);

                        if (!response.ok) throw new Error(`HTTP ${response.status}`);

                        const historicalData = await response.json();

                        if (!historicalData || historicalData.length === 0) {
                            meta.hasMoreHistory = false;
                            return;
                        }

                        meta.hasMoreHistory = historicalData.length >= 200;

                        // Merge: prepend older data to cache
                        chartDataCache[containerId] = [...historicalData, ...cache];

                        // resetData() clears chart and re-triggers getBars({type:'init'}),
                        // which returns the full merged cache. KLineChart defaults to
                        // positioning at the right (most recent) end after init, so
                        // no scroll adjustment needed — the user stays near where they were.
                        klineCharts[containerId]?.resetData();

                    } catch (error) {
                        console.error('[KLineChart] Error loading historical data:', error);
                    } finally {
                        loadingState[containerId] = false;
                    }
                },
                subscribeBar: ({ callback }) => {
                    realtimeCallbacks[containerId] = callback;
                },
                unsubscribeBar: () => {
                    delete realtimeCallbacks[containerId];
                }
            });
        } else {
            console.warn(`[KLineChart] No initial data provided for ${containerId}`);
        }

        // Add the ASK_LINE indicator to the candle pane as an overlay
        // Per KLineChart docs: custom indicators must use registerIndicator first, then createIndicator by name
        try {
            // Register the custom indicator globally first (only once)
            registerAskLineIndicator(klinecharts);

            // Now create it on the chart by name
            const askIndicatorId = chart.createIndicator('ASK_LINE', true, { id: 'candle_pane' });

            if (askIndicatorId) {
                indicatorPanes[containerId]['ASK_LINE'] = {
                    indicatorId: askIndicatorId,
                    paneId: 'candle_pane'
                };
            }
        } catch (e) {
            console.warn('[KLineChart] Error adding ASK_LINE indicator:', e);
        }

        return chart;
    } catch (error) {
        console.error('[KLineChart] Error in createKLineChart:', error);
    }
}

/**
 * Update chart with new data (full replacement)
 * @param {string} containerId - The container element ID
 * @param {Array} data - New OHLC data array
 */
export async function updateKLineChart(containerId, data) {
    //console.log(`[KLineChart] Updating chart ${containerId} with ${data?.length || 0} bars`);
    const chart = klineCharts[containerId];
    if (!chart) {
        console.warn(`[KLineChart] Chart ${containerId} not found during update, falling back to create`);
        await createKLineChart(containerId, data);
        return;
    }

    if (!data || data.length === 0) {
        console.warn(`[KLineChart] updateKLineChart called with empty data for ${containerId}`);
        return;
    }

    // Convert and apply new data
    const formattedData = data.map(item => ({
        timestamp: typeof item.time === 'number'
            ? (item.time < 10000000000 ? item.time * 1000 : item.time)
            : new Date(item.time).getTime(),
        open: item.open,
        high: item.high,
        low: item.low,
        close: item.close,
        volume: item.volume || 0
    }));

    // Cache the data and reset — triggers getBars({type:'init'}) which reads from cache
    chartDataCache[containerId] = formattedData;
    chart.resetData();
}

/**
 * Update chart with a single tick (real-time update)
 * @param {string} containerId - The container element ID  
 * @param {Object} tick - Single OHLC tick { time/timestamp, open, high, low, close, volume }
 */
export function updateKLineChartWithTick(containerId, tick) {
    const chart = klineCharts[containerId];
    if (!chart) return;

    const timestamp = typeof tick.time === 'number'
        ? (tick.time < 10000000000 ? tick.time * 1000 : tick.time)
        : new Date(tick.time).getTime();

    // Check if we have an existing candle for this timestamp in the cache
    // If so, we need to MERGE the values, not replace them
    let mergedTick;
    const cache = chartDataCache[containerId];
    const existingIndex = cache ? cache.findIndex(d => d.timestamp === timestamp) : -1;

    if (existingIndex >= 0) {
        const existing = cache[existingIndex];
        mergedTick = {
            timestamp: timestamp,
            open: existing.open,
            high: Math.max(existing.high, tick.close),
            low: Math.min(existing.low, tick.close),
            close: tick.close,
            volume: tick.volume || existing.volume || 0,
            askClose: tick.askClose || existing.askClose
        };
        cache[existingIndex] = mergedTick;
    } else {
        mergedTick = {
            timestamp: timestamp,
            open: tick.open,
            high: tick.high,
            low: tick.low,
            close: tick.close,
            volume: tick.volume || 0,
            askClose: tick.askClose
        };
        if (cache) {
            cache.push(mergedTick);
        }
    }

    // Push via subscribeBar callback (v10 real-time update mechanism)
    const callback = realtimeCallbacks[containerId];
    if (callback) {
        callback(mergedTick);
    }
}

/**
 * Dispose/cleanup a KLineChart instance
 * @param {string} containerId - The container element ID
 */
export function disposeKLineChart(containerId) {
    const chart = klineCharts[containerId];
    if (chart) {
        try {
            // KLineCharts uses dispose() to cleanup
            if (window.klinecharts && window.klinecharts.dispose) {
                window.klinecharts.dispose(containerId);
            }
        } catch (e) {
            console.warn('Error disposing KLineChart:', e);
        }
        delete klineCharts[containerId];
        delete indicatorPanes[containerId];
        delete realtimeCallbacks[containerId];
        delete chartDataCache[containerId];
        delete chartMetadata[containerId];
        delete loadingState[containerId];
    }
}

/**
 * Add or remove an indicator from the chart
 * @param {string} containerId - The container element ID
 * @param {string} indicatorName - Name of the indicator (e.g., 'MA', 'EMA', 'BOLL', 'RSI')
 * @param {boolean} show - Whether to show or hide the indicator
 * @param {string} paneId - Pane ID ('candle_pane' for overlays, or specific pane ID for sub-charts)
 */
export function toggleIndicator(containerId, indicatorName, show, paneId = 'candle_pane') {
    const chart = klineCharts[containerId];
    if (!chart) return;

    // Initialize tracking for this chart if not exists
    if (!indicatorPanes[containerId]) {
        indicatorPanes[containerId] = {};
    }

    try {
        if (show) {
            const isOverlay = paneId === 'candle_pane';

            if (isOverlay) {
                // Add to main candle pane with isStack=true to allow multiple overlays
                // createIndicator returns the INDICATOR ID, not pane ID
                const indicatorId = chart.createIndicator(indicatorName, true, { id: paneId });
                // Store the indicator ID for later removal
                indicatorPanes[containerId][indicatorName] = { indicatorId, paneId };
            } else {
                // For sub-pane indicators, create in their own pane
                const actualPaneId = paneId || `${indicatorName.toLowerCase()}_pane`;
                // createIndicator returns the INDICATOR ID, not pane ID
                const indicatorId = chart.createIndicator(indicatorName, false, {
                    id: actualPaneId,
                    height: 80,
                    minHeight: 60
                });
                // Store both indicator ID and pane ID
                indicatorPanes[containerId][indicatorName] = { indicatorId, paneId: actualPaneId };
            }
        } else {
            // Remove the indicator
            const tracked = indicatorPanes[containerId][indicatorName];

            // KLineChart v10 API: removeIndicator takes a filter OBJECT
            // Use 'id' (indicator ID) for precise removal
            // See: https://klinecharts.com/en-US/api/instance/removeIndicator
            let filter;
            if (tracked && tracked.indicatorId) {
                // Use indicator ID for precise removal
                filter = { id: tracked.indicatorId };
            } else if (tracked && tracked.paneId) {
                // Fallback to paneId + name
                filter = { paneId: tracked.paneId, name: indicatorName };
            } else {
                // Last resort: just use name (removes all with that name)
                filter = { name: indicatorName };
            }

            chart.removeIndicator(filter);
            delete indicatorPanes[containerId][indicatorName];
        }
    } catch (e) {
        console.warn(`[KLineChart] Error toggling indicator ${indicatorName}:`, e);
    }
}

/**
 * Resize the chart (call after container size changes)
 * @param {string} containerId - The container element ID
 */
export function resizeKLineChart(containerId) {
    const chart = klineCharts[containerId];
    if (chart) {
        chart.resize();
    }
}
