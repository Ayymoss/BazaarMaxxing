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

        // IMPORTANT: Set correct period based on interval
        // CandleInterval enum values are in minutes
        chart.setPeriod({ span: interval, type: 'minute' });
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
                volume: item.volume || 0
            }));

            // Cache the data before setting data loader
            chartDataCache[containerId] = formattedData;
            //console.log(`[KLineChart] Cached ${formattedData.length} records for ${containerId}`);

            // Use setDataLoader with lazy loading support
            chart.setDataLoader({
                getBars: async ({ type, data: currentData, callback }) => {
                    //console.log(`[KLineChart] Data loading requested: type=${type}, currentCount=${currentData?.length || 0}`);

                    // type: 'init' (initial load), 'forward' (newer data), 'backward' (older data)

                    // INIT: Return the full cached dataset
                    if (!type || type === 'init') {
                        const cached = chartDataCache[containerId] || formattedData;
                        const meta = chartMetadata[containerId];
                        const hasMore = meta?.hasMoreHistory ?? true;
                        //console.log(`[KLineChart] [INIT] Returning ${cached.length} cached bars (hasMoreHistory=${hasMore})`);
                        // Mark init as complete so backward loading can proceed
                        if (meta) meta.initComplete = true;
                        callback(cached, hasMore);
                        return;
                    }

                    // FORWARD: Return empty - we don't have future candles to provide
                    // Real-time updates come through subscribeBar, not forward loading
                    if (type === 'forward') {
                        //console.log('[KLineChart] [FORWARD] No future data available, returning empty');
                        callback([], false);
                        return;
                    }

                    // Backward loading - fetch more historical data
                    const meta = chartMetadata[containerId];

                    // GUARD: Skip backward loading if init hasn't completed yet
                    // We track this ourselves since KLineChart's currentData is unreliable
                    if (!meta?.initComplete) {
                        //console.log('[KLineChart] [BACKWARD] Skipping - init not yet complete');
                        callback([], true); // true = still has more, just not ready yet
                        return;
                    }

                    if (!meta || !meta.productKey || !meta.hasMoreHistory) {
                        //console.log('[KLineChart] No more history flags or metadata missing, skipping backward load');
                        callback([], false);
                        return;
                    }

                    if (loadingState[containerId]) {
                        //console.log('[KLineChart] Already loading history, skipping');
                        callback([], false);
                        return;
                    }

                    loadingState[containerId] = true;
                    //console.log(`[KLineChart] Fetching history for ${meta.productKey} before ${chartDataCache[containerId][0].timestamp}`);

                    try {
                        const cache = chartDataCache[containerId];
                        const earliestTimestamp = cache[0].timestamp;

                        const url = `/api/chart/${encodeURIComponent(meta.productKey)}/${meta.interval}?before=${earliestTimestamp}&limit=200`;
                        const response = await fetch(url);

                        if (!response.ok) {
                            throw new Error(`HTTP error! status: ${response.status}`);
                        }

                        const historicalData = await response.json();
                        //console.log(`[KLineChart] [BACKWARD] Received ${historicalData?.length || 0} historical records`);

                        if (!historicalData || historicalData.length === 0) {
                            meta.hasMoreHistory = false;
                            callback([], false);
                            return;
                        }

                        // Log the time range for debugging
                        const firstTs = historicalData[0]?.timestamp;
                        const lastTs = historicalData[historicalData.length - 1]?.timestamp;
                        //console.log(`[KLineChart] [BACKWARD] Historical range: ${new Date(firstTs).toISOString()} to ${new Date(lastTs).toISOString()}`);
                        //console.log(`[KLineChart] [BACKWARD] Current cache starts at: ${new Date(cache[0].timestamp).toISOString()}`);

                        // Prepend to cache (historical data comes BEFORE current data)
                        const mergedData = [...historicalData, ...cache];
                        chartDataCache[containerId] = mergedData;
                        //console.log(`[KLineChart] [BACKWARD] Cache updated with ${mergedData.length} bars, calling resetData()`);

                        // Signal no new data via callback (we'll reset instead)
                        callback([], historicalData.length >= 200);

                        // Force chart to re-initialize with merged data
                        // resetData() clears data and triggers getBars with type='init'
                        const chart = klineCharts[containerId];
                        if (chart) {
                            chart.resetData();
                        }

                    } catch (error) {
                        console.error('[KLineChart] Error loading historical data:', error);
                        callback([], false);
                    } finally {
                        loadingState[containerId] = false;
                    }
                },
                subscribeBar: ({ callback }) => {
                    //console.log(`[KLineChart] Real-time subscription started for ${containerId}`);
                    realtimeCallbacks[containerId] = callback;
                },
                unsubscribeBar: () => {
                    //console.log(`[KLineChart] Real-time subscription stopped for ${containerId}`);
                    delete realtimeCallbacks[containerId];
                }
            });
        } else {
            console.warn(`[KLineChart] No initial data provided for ${containerId}`);
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
        const existing = cache[existingIndex];
        mergedTick = {
            timestamp: timestamp,
            open: existing.open,  // Preserve original open
            high: Math.max(existing.high, tick.close),  // Max of existing high and new price
            low: Math.min(existing.low, tick.close),    // Min of existing low and new price
            close: tick.close,    // Latest price
            volume: tick.volume || existing.volume || 0 // Use latest volume
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
            volume: tick.volume || 0
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
