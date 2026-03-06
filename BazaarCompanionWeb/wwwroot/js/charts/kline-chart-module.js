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
            console.log('[KLineChart DEBUG] ASK_LINE calc() called with', kLineDataList.length, 'data points');
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
    console.log('[KLineChart DEBUG] ASK_LINE indicator registered globally');
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

            // === DEBUG: Log initial data from Blazor ===
            console.log(`[KLineChart DEBUG] Initial data from Blazor: ${formattedData.length} bars, interval=${interval}m`);
            if (formattedData.length > 0) {
                const first = formattedData[0];
                const last = formattedData[formattedData.length - 1];
                console.log(`[KLineChart DEBUG] Initial range: ${new Date(first.timestamp).toISOString()} → ${new Date(last.timestamp).toISOString()}`);
                console.log('[KLineChart DEBUG] First bar:', JSON.stringify(first));
                console.log('[KLineChart DEBUG] Last bar:', JSON.stringify(last));
                // Check for flat OHLC (O==H==L==C)
                const flatBars = formattedData.filter(d => d.open === d.high && d.high === d.low && d.low === d.close);
                console.log(`[KLineChart DEBUG] Flat OHLC bars (O==H==L==C): ${flatBars.length} of ${formattedData.length}`);
                if (flatBars.length > 0) {
                    console.log('[KLineChart DEBUG] Sample flat bar:', JSON.stringify(flatBars[0]));
                }
                // Check askClose coverage
                const withAsk = formattedData.filter(d => d.askClose && d.askClose !== 0);
                console.log(`[KLineChart DEBUG] Bars with askClose data: ${withAsk.length} of ${formattedData.length}`);
            }

            // Cache the data before setting data loader
            chartDataCache[containerId] = formattedData;

            // Use setDataLoader with lazy loading support
            // KLineChart getBars signature: { type, symbol, period, timestamp, callback }
            // type: 'init' | 'backward' | 'forward'
            // timestamp: earliest/latest bar timestamp (null for init)
            // callback(data[], more) where more = { backward?: bool, forward?: bool } or bool
            chart.setDataLoader({
                getBars: async ({ type, timestamp, callback }) => {
                    console.log(`[KLineChart DEBUG] getBars called: type=${type}, timestamp=${timestamp} (${timestamp ? new Date(timestamp).toISOString() : 'null'})`);

                    // INIT: Return the full cached dataset
                    if (!type || type === 'init') {
                        const cached = chartDataCache[containerId] || formattedData;
                        const meta = chartMetadata[containerId];
                        const hasMore = meta?.hasMoreHistory ?? true;
                        if (meta) meta.initComplete = true;
                        console.log(`[KLineChart DEBUG] [INIT] Returning ${cached.length} bars, hasMoreBackward=${hasMore}`);
                        callback(cached, { backward: hasMore, forward: false });
                        return;
                    }

                    // FORWARD: No future candles — real-time comes via subscribeBar
                    if (type === 'forward') {
                        console.log('[KLineChart DEBUG] [FORWARD] No future data, returning empty');
                        callback([], { backward: false, forward: false });
                        return;
                    }

                    // BACKWARD: fetch older historical data from the API
                    const meta = chartMetadata[containerId];

                    if (!meta?.initComplete) {
                        console.log('[KLineChart DEBUG] [BACKWARD] Skipping — init not complete');
                        callback([], { backward: true, forward: false });
                        return;
                    }

                    if (!meta?.productKey || !meta.hasMoreHistory) {
                        console.log(`[KLineChart DEBUG] [BACKWARD] Skipping — productKey=${meta?.productKey}, hasMoreHistory=${meta?.hasMoreHistory}`);
                        callback([], { backward: false, forward: false });
                        return;
                    }

                    if (loadingState[containerId]) {
                        console.log('[KLineChart DEBUG] [BACKWARD] Skipping — already loading');
                        callback([], { backward: true, forward: false });
                        return;
                    }

                    loadingState[containerId] = true;

                    try {
                        // Use the timestamp KLineChart gives us (earliest visible bar)
                        // Fall back to our cache's earliest entry
                        const cache = chartDataCache[containerId];
                        const beforeTs = timestamp || (cache?.length > 0 ? cache[0].timestamp : null);
                        console.log(`[KLineChart DEBUG] [BACKWARD] beforeTs=${beforeTs} (${beforeTs ? new Date(beforeTs).toISOString() : 'null'}), cache size=${cache?.length || 0}`);

                        if (!beforeTs) {
                            console.log('[KLineChart DEBUG] [BACKWARD] No beforeTs available, returning empty');
                            callback([], { backward: false, forward: false });
                            return;
                        }

                        const url = meta.productKey?.startsWith('index:')
                            ? `/api/chart/index/${encodeURIComponent(meta.productKey.slice(6))}/${meta.interval}?before=${beforeTs}&limit=200`
                            : `/api/chart/${encodeURIComponent(meta.productKey)}/${meta.interval}?before=${beforeTs}&limit=200`;
                        console.log(`[KLineChart DEBUG] [BACKWARD] Fetching: ${url}`);
                        const response = await fetch(url);

                        if (!response.ok) {
                            throw new Error(`HTTP error! status: ${response.status}`);
                        }

                        const historicalData = await response.json();
                        console.log(`[KLineChart DEBUG] [BACKWARD] API returned ${historicalData?.length || 0} bars`);

                        if (!historicalData || historicalData.length === 0) {
                            meta.hasMoreHistory = false;
                            console.log('[KLineChart DEBUG] [BACKWARD] No more history available');
                            callback([], { backward: false, forward: false });
                            return;
                        }

                        // Log the received data details
                        const firstBar = historicalData[0];
                        const lastBar = historicalData[historicalData.length - 1];
                        console.log(`[KLineChart DEBUG] [BACKWARD] Received range: ${new Date(firstBar.timestamp).toISOString()} → ${new Date(lastBar.timestamp).toISOString()}`);
                        console.log('[KLineChart DEBUG] [BACKWARD] First bar:', JSON.stringify(firstBar));
                        console.log('[KLineChart DEBUG] [BACKWARD] Last bar:', JSON.stringify(lastBar));
                        // Check for flat OHLC in backward data
                        const flatBars = historicalData.filter(d => d.open === d.high && d.high === d.low && d.low === d.close);
                        console.log(`[KLineChart DEBUG] [BACKWARD] Flat OHLC bars: ${flatBars.length} of ${historicalData.length}`);
                        // Check askClose coverage
                        const withAsk = historicalData.filter(d => d.askClose && d.askClose !== 0);
                        console.log(`[KLineChart DEBUG] [BACKWARD] Bars with askClose: ${withAsk.length} of ${historicalData.length}`);

                        const hasMoreHistory = historicalData.length >= 200;
                        meta.hasMoreHistory = hasMoreHistory;

                        // Prepend to our local cache for tick merging
                        if (cache) {
                            chartDataCache[containerId] = [...historicalData, ...cache];
                            console.log(`[KLineChart DEBUG] [BACKWARD] Cache updated: ${chartDataCache[containerId].length} total bars`);
                        }

                        // Pass historical data directly to the callback
                        // KLineChart will prepend it to its internal data list
                        console.log(`[KLineChart DEBUG] [BACKWARD] Passing ${historicalData.length} bars to callback, hasMore=${hasMoreHistory}`);
                        callback(historicalData, { backward: hasMoreHistory, forward: false });

                    } catch (error) {
                        console.error('[KLineChart] Error loading historical data:', error);
                        callback([], { backward: false, forward: false });
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
            // Debug: Check if askClose data exists in the cache
            const cachedData = chartDataCache[containerId];
            if (cachedData && cachedData.length > 0) {
                console.log('[KLineChart DEBUG] Sample data point:', cachedData[cachedData.length - 1]);
                console.log('[KLineChart DEBUG] askClose values present:', cachedData.filter(d => d.askClose && d.askClose !== d.close).length, 'of', cachedData.length);
            }

            // Register the custom indicator globally first (only once)
            registerAskLineIndicator(klinecharts);

            // Now create it on the chart by name
            const askIndicatorId = chart.createIndicator('ASK_LINE', true, { id: 'candle_pane' });
            console.log('[KLineChart DEBUG] createIndicator returned:', askIndicatorId);

            if (askIndicatorId) {
                indicatorPanes[containerId]['ASK_LINE'] = {
                    indicatorId: askIndicatorId,
                    paneId: 'candle_pane'
                };
            }
        } catch (e) {
            console.error('[KLineChart DEBUG] Error adding ASK_LINE indicator:', e);
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

    // Cache the data
    chartDataCache[containerId] = formattedData;

    // Reset data loader to use the new data
    //console.log(`[KLineChart] Re-assigning data loader for ${containerId} after update`);
    chart.setDataLoader({
        getBars: ({ type, callback }) => {
            //console.log(`[KLineChart] Update-triggered getBars: type=${type}`);
            callback(chartDataCache[containerId] || formattedData);
        },
        subscribeBar: ({ callback }) => {
            realtimeCallbacks[containerId] = callback;
        },
        unsubscribeBar: () => {
            delete realtimeCallbacks[containerId];
        }
    });
}

/**
 * Update chart with a single tick (real-time update)
 * @param {string} containerId - The container element ID  
 * @param {Object} tick - Single OHLC tick { time/timestamp, open, high, low, close, volume }
 */
export function updateKLineChartWithTick(containerId, tick) {
    const chart = klineCharts[containerId];
    if (!chart) return;

    const callback = realtimeCallbacks[containerId];
    if (!callback) {
        // This is expected if 'init' hasn't finished or subscribeBar hasn't been called yet
        console.warn('[KLineChart] No real-time callback registered for', containerId);
        return;
    }

    const timestamp = typeof tick.time === 'number'
        ? (tick.time < 10000000000 ? tick.time * 1000 : tick.time)
        : new Date(tick.time).getTime();

    // Check if we have an existing candle for this timestamp in the cache
    // If so, we need to MERGE the values, not replace them
    let mergedTick;
    const cache = chartDataCache[containerId];
    const existingIndex = cache ? cache.findIndex(d => d.timestamp === timestamp) : -1;

    if (existingIndex >= 0) {
        // MERGE with existing candle:
        // - Open: PRESERVE the original open (first price of the interval)
        // - High: MAX of existing high and new close
        // - Low: MIN of existing low and new close
        // - Close: new close (latest price)
        // - Volume: use the latest volume snapshot (or accumulate if preferred)
        // - AskClose: always use latest ASK price
        const existing = cache[existingIndex];
        mergedTick = {
            timestamp: timestamp,
            open: existing.open,  // Preserve original open
            high: Math.max(existing.high, tick.close),  // Max of existing high and new price
            low: Math.min(existing.low, tick.close),    // Min of existing low and new price
            close: tick.close,    // Latest price
            volume: tick.volume || existing.volume || 0, // Use latest volume
            askClose: tick.askClose || existing.askClose // No fallback to close
        };
        // Update the cache with merged values
        cache[existingIndex] = mergedTick;
    } else {
        // New candle - use the tick's values directly
        mergedTick = {
            timestamp: timestamp,
            open: tick.open,
            high: tick.high,
            low: tick.low,
            close: tick.close,
            volume: tick.volume || 0,
            askClose: tick.askClose // No fallback to close
        };
        // Add new candle to cache
        if (cache) {
            cache.push(mergedTick);
        }
    }

    // Call the subscription callback with the properly merged tick data
    callback(mergedTick);
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
