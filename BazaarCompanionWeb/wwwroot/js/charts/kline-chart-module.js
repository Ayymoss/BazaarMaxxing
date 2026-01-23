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
 * @param {Object} options - Additional options
 */
export async function createKLineChart(containerId, data, options = {}) {
    try {
        const klinecharts = await waitForKLineCharts();
        
        const container = document.getElementById(containerId);
        if (!container) {
            console.error('Container not found:', containerId);
            return;
        }

        // Dispose existing chart if any
        disposeKLineChart(containerId);

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
        
        // Initialize indicator tracking - we need to get the actual indicator IDs from the chart
        // since they were created via layout, not via createIndicator
        indicatorPanes[containerId] = {};
        
        // Query the chart for the layout-created indicators to get their actual IDs
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
            // Fallback - store without indicator IDs (removal will use name-based fallback)
            indicatorPanes[containerId] = {
                'VOL': { indicatorId: null, paneId: 'vol_pane' },
                'MACD': { indicatorId: null, paneId: 'macd_pane' }
            };
        }

        // Set symbol and period info
        chart.setSymbol({ ticker: options.productName || 'Product' });
        chart.setPeriod({ span: 1, type: 'minute' });

        // Apply data if provided
        if (data && data.length > 0) {
            // Convert data to KLineChart format (timestamp in ms)
            const formattedData = data.map(item => ({
                timestamp: typeof item.time === 'number' 
                    ? (item.time < 10000000000 ? item.time * 1000 : item.time) // Convert seconds to ms if needed
                    : new Date(item.time).getTime(),
                open: item.open,
                high: item.high,
                low: item.low,
                close: item.close,
                volume: item.volume || 0
            }));

            // Cache the data for potential re-subscription
            chartDataCache[containerId] = formattedData;

            // Use setDataLoader with subscribeBar for real-time updates
            chart.setDataLoader({
                getBars: ({ callback }) => {
                    callback(chartDataCache[containerId] || formattedData);
                },
                subscribeBar: ({ callback }) => {
                    // Store the callback so we can push real-time updates
                    realtimeCallbacks[containerId] = callback;
                },
                unsubscribeBar: () => {
                    // Clean up the callback reference
                    delete realtimeCallbacks[containerId];
                }
            });
        }

        return chart;
    } catch (error) {
        console.error('Error creating KLineChart:', error);
    }
}

/**
 * Update chart with new data (full replacement)
 * @param {string} containerId - The container element ID
 * @param {Array} data - New OHLC data array
 */
export async function updateKLineChart(containerId, data) {
    const chart = klineCharts[containerId];
    if (!chart) {
        // Chart doesn't exist, create it
        await createKLineChart(containerId, data);
        return;
    }

    if (!data || data.length === 0) return;

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

    // Reset data - this replaces all existing data
    chart.setDataLoader({
        getBars: ({ callback }) => {
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
        console.warn('[KLineChart] No real-time callback registered for', containerId);
        return;
    }

    const timestamp = typeof tick.time === 'number'
        ? (tick.time < 10000000000 ? tick.time * 1000 : tick.time)
        : new Date(tick.time).getTime();

    const formattedTick = {
        timestamp: timestamp,
        open: tick.open,
        high: tick.high,
        low: tick.low,
        close: tick.close,
        volume: tick.volume || 0
    };

    // Call the subscription callback with the new tick data
    callback(formattedTick);
    
    // Also update the cache with this new data point
    if (chartDataCache[containerId]) {
        const existingIndex = chartDataCache[containerId].findIndex(d => d.timestamp === timestamp);
        if (existingIndex >= 0) {
            // Update existing candle
            chartDataCache[containerId][existingIndex] = formattedTick;
        } else {
            // Add new candle
            chartDataCache[containerId].push(formattedTick);
        }
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
