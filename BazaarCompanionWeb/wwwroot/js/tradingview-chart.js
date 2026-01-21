let charts = {};
let series = {};
let indicatorSeries = {};

function getLightweightCharts() {
    // Try multiple possible locations where the library might be exposed
    // The standalone build typically exposes it as window.LightweightCharts
    if (window.LightweightCharts) {
        return window.LightweightCharts;
    }
    if (window.lightweightCharts) {
        return window.lightweightCharts;
    }
    if (globalThis.LightweightCharts) {
        return globalThis.LightweightCharts;
    }
    if (globalThis.lightweightCharts) {
        return globalThis.lightweightCharts;
    }
    // Some builds might expose it differently
    if (window.lightweightcharts) {
        return window.lightweightcharts;
    }
    return undefined;
}

export async function createChart(containerId, data) {
    // Wait a bit for the library to load if it's not immediately available
    let LightweightCharts = getLightweightCharts();
    let attempts = 0;
    const maxAttempts = 10;

    while (typeof LightweightCharts === 'undefined' && attempts < maxAttempts) {
        await new Promise(resolve => setTimeout(resolve, 100));
        LightweightCharts = getLightweightCharts();
        attempts++;
    }

    if (typeof LightweightCharts === 'undefined') {
        console.error('TradingView Lightweight Charts library not loaded after', maxAttempts, 'attempts');
        console.error('Available on window:', Object.keys(window).filter(k => k.toLowerCase().includes('light') || k.toLowerCase().includes('chart')));
        return;
    }

    if (typeof LightweightCharts.createChart !== 'function') {
        console.error('LightweightCharts.createChart is not a function. LightweightCharts object:', LightweightCharts);
        return;
    }

    const container = document.getElementById(containerId);
    if (!container) {
        console.error(`Container ${containerId} not found`);
        return;
    }

    // Dispose existing chart if any
    if (charts[containerId]) {
        charts[containerId].remove();
        delete charts[containerId];
        delete series[containerId];
    }

    let chart;
    try {
        chart = LightweightCharts.createChart(container, {
            layout: {
                background: { color: '#1e293b' },
                textColor: '#d1d5db',
            },
            grid: {
                vertLines: { color: '#334155' },
                horzLines: { color: '#334155' },
            },
            crosshair: {
                mode: LightweightCharts.CrosshairMode?.Normal || 0,
            },
            rightPriceScale: {
                borderColor: '#475569',
            },
            timeScale: {
                borderColor: '#475569',
                timeVisible: true,
            },
            localization: {
                priceFormatter: (price) => `$${price.toLocaleString('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 2 })}`,
            },
        });
    } catch (error) {
        console.error('Error creating chart:', error);
        return;
    }

    if (!chart) {
        console.error('Chart creation returned null/undefined');
        return;
    }

    // Check if addCandlestickSeries exists, if not try addSeries with type
    let candlestickSeries;
    const seriesOptions = {
        upColor: '#10b981',
        downColor: '#ef4444',
        borderVisible: false,
        wickUpColor: '#10b981',
        wickDownColor: '#ef4444',
        priceFormat: {
            type: 'price',
            precision: 2,
            minMove: 0.1,
        },
    };

    if (typeof chart.addCandlestickSeries === 'function') {
        candlestickSeries = chart.addCandlestickSeries(seriesOptions);
    } else if (typeof chart.addSeries === 'function') {
        // Try the unified API (v5+)
        candlestickSeries = chart.addSeries('Candlestick', seriesOptions);
    } else {
        console.error('Chart object does not have addCandlestickSeries or addSeries method. Available methods:', Object.getOwnPropertyNames(chart).filter(name => typeof chart[name] === 'function'));
        console.error('Chart object:', chart);
        return;
    }

    if (!candlestickSeries) {
        console.error('Failed to create candlestick series');
        return;
    }

    // Transform data to TradingView format
    const chartData = data.map(item => ({
        time: Math.floor(new Date(item.time).getTime() / 1000),
        open: item.open,
        high: item.high,
        low: item.low,
        close: item.close,
    }));

    try {
        candlestickSeries.setData(chartData);
        chart.timeScale().fitContent();
        charts[containerId] = chart;
        series[containerId] = candlestickSeries;
    } catch (error) {
        console.error('Error setting chart data:', error);
    }
}

export async function updateChart(containerId, data, fitContent = false) {
    if (!charts[containerId]) {
        await createChart(containerId, data);
        return;
    }

    const chart = charts[containerId];
    const candlestickSeries = series[containerId];

    if (!candlestickSeries) {
        await createChart(containerId, data);
        return;
    }

    const chartData = data.map(item => ({
        time: Math.floor(new Date(item.time).getTime() / 1000),
        open: item.open,
        high: item.high,
        low: item.low,
        close: item.close,
    }));

    candlestickSeries.setData(chartData);

    if (fitContent) {
        chart.timeScale().fitContent();
    }
}

export async function createChartWithIndicators(containerId, data, indicators, spreadData, supportResistanceLevels, fitContent = false, showVolume = false) {
    // Wait for library
    let LightweightCharts = getLightweightCharts();
    let attempts = 0;
    const maxAttempts = 10;

    while (typeof LightweightCharts === 'undefined' && attempts < maxAttempts) {
        await new Promise(resolve => setTimeout(resolve, 100));
        LightweightCharts = getLightweightCharts();
        attempts++;
    }

    if (typeof LightweightCharts === 'undefined') {
        console.error('TradingView Lightweight Charts library not loaded');
        return;
    }

    const container = document.getElementById(containerId);
    if (!container) {
        console.error(`Container ${containerId} not found`);
        return;
    }

    // Dispose existing chart if any
    if (charts[containerId]) {
        charts[containerId].remove();
        delete charts[containerId];
        delete series[containerId];
        delete indicatorSeries[containerId];
    }

    // Create chart
    let chart;
    try {
        chart = LightweightCharts.createChart(container, {
            layout: {
                background: { color: '#1e293b' },
                textColor: '#d1d5db',
            },
            grid: {
                vertLines: { color: '#334155' },
                horzLines: { color: '#334155' },
            },
            crosshair: {
                mode: LightweightCharts.CrosshairMode?.Normal || 0,
            },
            rightPriceScale: {
                borderColor: '#475569',
            },
            timeScale: {
                borderColor: '#475569',
                timeVisible: true,
            },
            localization: {
                priceFormatter: (price) => `$${price.toLocaleString('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 2 })}`,
            },
        });
    } catch (error) {
        console.error('Error creating chart:', error);
        return;
    }

    if (!chart) {
        console.error('Chart creation returned null/undefined');
        return;
    }

    // Add candlestick series
    const seriesOptions = {
        upColor: '#10b981',
        downColor: '#ef4444',
        borderVisible: false,
        wickUpColor: '#10b981',
        wickDownColor: '#ef4444',
        priceFormat: {
            type: 'price',
            precision: 2,
            minMove: 0.1,
        },
    };

    let candlestickSeries;
    if (typeof chart.addCandlestickSeries === 'function') {
        candlestickSeries = chart.addCandlestickSeries(seriesOptions);
    } else if (typeof chart.addSeries === 'function') {
        candlestickSeries = chart.addSeries('Candlestick', seriesOptions);
    } else {
        console.error('Chart object does not have addCandlestickSeries or addSeries method');
        return;
    }

    // Transform OHLC data
    const chartData = data.map(item => ({
        time: Math.floor(new Date(item.time).getTime() / 1000),
        open: item.open,
        high: item.high,
        low: item.low,
        close: item.close,
    }));

    candlestickSeries.setData(chartData);

    // Add volume bars if volume data is available and enabled
    let volumeSeries = null;
    if (showVolume) {
        const volumeData = data.filter(item => item.volume != null && item.volume > 0);
        if (volumeData.length > 0) {
            try {
                // Try addHistogramSeries first (v4+)
                if (typeof chart.addHistogramSeries === 'function') {
                    volumeSeries = chart.addHistogramSeries({
                        color: '#26a69a',
                        priceFormat: {
                            type: 'volume',
                        },
                        priceScaleId: '',
                        scaleMargins: {
                            top: 0.8,
                            bottom: 0,
                        },
                    });
                } else if (typeof chart.addSeries === 'function') {
                    // Try unified API (v5+)
                    volumeSeries = chart.addSeries('Histogram', {
                        color: '#26a69a',
                        priceFormat: {
                            type: 'volume',
                        },
                        priceScaleId: '',
                        scaleMargins: {
                            top: 0.8,
                            bottom: 0,
                        },
                    });
                }

                if (volumeSeries) {
                    const volumeChartData = volumeData.map(item => ({
                        time: Math.floor(new Date(item.time).getTime() / 1000),
                        value: item.volume,
                        color: item.close >= item.open ? '#26a69a' : '#ef5350',
                    }));

                    volumeSeries.setData(volumeChartData);
                    if (!indicatorSeries[containerId]) {
                        indicatorSeries[containerId] = {};
                    }
                    indicatorSeries[containerId]['Volume'] = volumeSeries;
                }
            } catch (error) {
                console.warn('Error adding volume series:', error);
            }
        }
    }

    // Store series
    series[containerId] = { candlestick: candlestickSeries };
    indicatorSeries[containerId] = {};

    // Add indicators
    if (indicators && indicators.length > 0) {
        indicators.forEach(indicator => {
            try {
                const indicatorType = (indicator.type || '').toString();
                const isRSI = indicatorType.includes('RSI');
                const isMACD = indicatorType.includes('MACD');
                const isOscillator = isRSI || isMACD;
                const isBollinger = indicatorType.includes('Bollinger');

                if (isOscillator) {
                    // Use right price scale for oscillators (separate from main price scale)
                    const lineSeries = chart.addLineSeries({
                        color: indicator.color || '#f59e0b',
                        lineWidth: indicator.lineWidth || 1,
                        priceScaleId: 'right',
                        priceFormat: {
                            type: 'price',
                            precision: 2,
                        },
                        scaleMargins: {
                            top: 0.7,
                            bottom: 0.1,
                        },
                    });

                    // Configure right price scale for RSI (0-100 range)
                    if (isRSI) {
                        chart.priceScale('right').applyOptions({
                            autoScale: false,
                            scaleMargins: {
                                top: 0.7,
                                bottom: 0.1,
                            },
                        });
                    }

                    const indicatorData = (indicator.dataPoints || []).map(dp => ({
                        time: Math.floor(new Date(dp.time).getTime() / 1000),
                        value: dp.value,
                    })).filter(d => d.value != null && !isNaN(d.value));

                    if (indicatorData.length > 0) {
                        lineSeries.setData(indicatorData);
                        indicatorSeries[containerId][indicator.name] = lineSeries;
                    }
                } else if (isBollinger) {
                    // Bollinger Bands
                    const lineSeries = chart.addLineSeries({
                        color: indicator.color || '#6b7280',
                        lineWidth: indicator.lineWidth || 1,
                        lineStyle: 2, // Dashed
                    });

                    const indicatorData = (indicator.dataPoints || []).map(dp => ({
                        time: Math.floor(new Date(dp.time).getTime() / 1000),
                        value: dp.value,
                    })).filter(d => d.value != null && !isNaN(d.value));

                    if (indicatorData.length > 0) {
                        lineSeries.setData(indicatorData);
                        indicatorSeries[containerId][indicator.name] = lineSeries;
                    }
                } else {
                    // Regular line indicators (SMA, EMA, VWAP, etc.)
                    const lineSeries = chart.addLineSeries({
                        color: indicator.color || '#3b82f6',
                        lineWidth: indicator.lineWidth || 1,
                    });

                    const indicatorData = (indicator.dataPoints || []).map(dp => ({
                        time: Math.floor(new Date(dp.time).getTime() / 1000),
                        value: dp.value,
                    })).filter(d => d.value != null && !isNaN(d.value));

                    if (indicatorData.length > 0) {
                        lineSeries.setData(indicatorData);
                        indicatorSeries[containerId][indicator.name] = lineSeries;
                    }
                }
            } catch (error) {
                console.warn(`Error adding indicator ${indicator.name || 'unknown'}:`, error);
            }
        });
    }

    // Add support/resistance levels as horizontal lines
    if (supportResistanceLevels && supportResistanceLevels.length > 0) {
        supportResistanceLevels.forEach(level => {
            try {
                const levelType = (level.type || '').toString();
                const isSupport = levelType === 'Support';
                const lineSeries = chart.addLineSeries({
                    color: isSupport ? '#10b981' : '#ef4444',
                    lineWidth: Math.max(1, Math.floor((level.strength || 0.5) * 3)),
                    lineStyle: 2, // Dashed
                    priceFormat: {
                        type: 'price',
                        precision: 2,
                    },
                });

                const lineData = chartData.map(cd => ({
                    time: cd.time,
                    value: level.price || 0,
                }));

                lineSeries.setData(lineData);
                indicatorSeries[containerId][`${levelType}_${level.price}`] = lineSeries;
            } catch (error) {
                console.warn('Error adding support/resistance level:', error);
            }
        });
    }

    if (fitContent) {
        chart.timeScale().fitContent();
    }

    charts[containerId] = chart;
}

export async function createComparisonChart(containerId, normalizedData, productKeys) {
    let LightweightCharts = getLightweightCharts();
    let attempts = 0;
    const maxAttempts = 10;

    while (typeof LightweightCharts === 'undefined' && attempts < maxAttempts) {
        await new Promise(resolve => setTimeout(resolve, 100));
        LightweightCharts = getLightweightCharts();
        attempts++;
    }

    if (typeof LightweightCharts === 'undefined') {
        console.error('TradingView Lightweight Charts library not loaded');
        return;
    }

    const container = document.getElementById(containerId);
    if (!container) {
        console.error(`Container ${containerId} not found`);
        return;
    }

    // Dispose existing chart
    if (charts[containerId]) {
        charts[containerId].remove();
        delete charts[containerId];
        delete series[containerId];
        delete indicatorSeries[containerId];
    }

    // Create chart
    const chart = LightweightCharts.createChart(container, {
        layout: {
            background: { color: '#1e293b' },
            textColor: '#d1d5db',
        },
        grid: {
            vertLines: { color: '#334155' },
            horzLines: { color: '#334155' },
        },
        crosshair: {
            mode: LightweightCharts.CrosshairMode?.Normal || 0,
        },
        rightPriceScale: {
            borderColor: '#475569',
        },
        timeScale: {
            borderColor: '#475569',
            timeVisible: true,
        },
        localization: {
            priceFormatter: (price) => `${price.toFixed(2)}%`,
        },
    });

    // Color palette for multiple products
    const colors = ['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899'];

    // Add line series for each product
    productKeys.forEach((productKey, index) => {
        const productData = normalizedData[productKey];
        if (!productData || !Array.isArray(productData) || productData.length === 0) return;

        const lineSeries = chart.addLineSeries({
            color: colors[index % colors.length],
            lineWidth: 2,
            title: productKey,
        });

        const chartData = productData.map(item => ({
            time: Math.floor(new Date(item.time).getTime() / 1000),
            value: item.value || 0,
        })).filter(d => d.value != null && !isNaN(d.value));

        if (chartData.length > 0) {
            lineSeries.setData(chartData);

            if (!series[containerId]) {
                series[containerId] = {};
            }
            series[containerId][productKey] = lineSeries;
        }
    });

    chart.timeScale().fitContent();
    charts[containerId] = chart;
}

export function disposeChart(containerId) {
    if (charts[containerId]) {
        charts[containerId].remove();
        delete charts[containerId];
        delete series[containerId];
        delete indicatorSeries[containerId];
    }
}

/**
 * Creates a depth chart showing cumulative bid/ask volume
 */
export async function createDepthChart(containerId, bidData, askData) {
    const container = document.getElementById(containerId);
    if (!container) {
        console.error(`Container ${containerId} not found`);
        return;
    }

    let LightweightCharts = getLightweightCharts();
    let attempts = 0;
    const maxAttempts = 10;

    while (typeof LightweightCharts === 'undefined' && attempts < maxAttempts) {
        await new Promise(resolve => setTimeout(resolve, 100));
        LightweightCharts = getLightweightCharts();
        attempts++;
    }

    if (typeof LightweightCharts === 'undefined') {
        console.error('TradingView Lightweight Charts library not loaded');
        return;
    }

    // Dispose existing chart
    if (charts[containerId]) {
        charts[containerId].remove();
        delete charts[containerId];
        delete series[containerId];
    }

    const chart = LightweightCharts.createChart(container, {
        layout: {
            background: { type: 'solid', color: 'transparent' },
            textColor: '#94a3b8',
        },
        grid: {
            vertLines: { color: 'rgba(51, 65, 85, 0.3)' },
            horzLines: { color: 'rgba(51, 65, 85, 0.3)' },
        },
        rightPriceScale: {
            borderVisible: false,
        },
        timeScale: {
            visible: false,
        },
        crosshair: {
            mode: 0,
        },
        handleScale: false,
        handleScroll: false,
    });

    // Create combined price-indexed data for the depth chart
    // We'll use a line series with area fill for each side

    // Bid side (green) - add area series
    let bidSeries;
    if (typeof chart.addAreaSeries === 'function') {
        bidSeries = chart.addAreaSeries({
            lineColor: '#22c55e',
            topColor: 'rgba(34, 197, 94, 0.4)',
            bottomColor: 'rgba(34, 197, 94, 0.05)',
            lineWidth: 2,
        });
    } else if (typeof chart.addSeries === 'function') {
        bidSeries = chart.addSeries('Area', {
            lineColor: '#22c55e',
            topColor: 'rgba(34, 197, 94, 0.4)',
            bottomColor: 'rgba(34, 197, 94, 0.05)',
            lineWidth: 2,
        });
    }

    // Ask side (red) - add area series
    let askSeries;
    if (typeof chart.addAreaSeries === 'function') {
        askSeries = chart.addAreaSeries({
            lineColor: '#ef4444',
            topColor: 'rgba(239, 68, 68, 0.4)',
            bottomColor: 'rgba(239, 68, 68, 0.05)',
            lineWidth: 2,
        });
    } else if (typeof chart.addSeries === 'function') {
        askSeries = chart.addSeries('Area', {
            lineColor: '#ef4444',
            topColor: 'rgba(239, 68, 68, 0.4)',
            bottomColor: 'rgba(239, 68, 68, 0.05)',
            lineWidth: 2,
        });
    }

    // Convert price-based data to indexed time for rendering
    // Bid data goes from high price to low (right to center)
    const bidPoints = bidData.map((d, i) => ({
        time: i,
        value: d.volume
    }));

    // Ask data goes from low price to high (center to right)
    const askPoints = askData.map((d, i) => ({
        time: bidData.length + i,
        value: d.volume
    }));

    if (bidSeries && bidPoints.length > 0) {
        bidSeries.setData(bidPoints);
    }

    if (askSeries && askPoints.length > 0) {
        askSeries.setData(askPoints);
    }

    chart.timeScale().fitContent();
    charts[containerId] = chart;
    series[containerId] = { bid: bidSeries, ask: askSeries };
}

/**
 * Renders a heatmap on a canvas element for order book visualization over time
 */
window.renderHeatmap = function (canvasId, data) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) {
        console.error(`Canvas ${canvasId} not found`);
        return;
    }

    const ctx = canvas.getContext('2d');
    const width = canvas.offsetWidth;
    const height = canvas.offsetHeight;

    // Set canvas resolution to match display size
    canvas.width = width * window.devicePixelRatio;
    canvas.height = height * window.devicePixelRatio;
    ctx.scale(window.devicePixelRatio, window.devicePixelRatio);

    if (!data || data.length === 0) {
        ctx.fillStyle = '#64748b';
        ctx.font = '12px sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText('No heatmap data available', width / 2, height / 2);
        return;
    }

    // Find data bounds
    const times = data.map(d => new Date(d.time).getTime());
    const prices = data.map(d => d.price);
    const volumes = data.map(d => d.volume);

    const minTime = Math.min(...times);
    const maxTime = Math.max(...times);
    const minPrice = Math.min(...prices);
    const maxPrice = Math.max(...prices);
    const maxVolume = Math.max(...volumes);

    const timeRange = maxTime - minTime || 1;
    const priceRange = maxPrice - minPrice || 1;

    // Clear canvas
    ctx.clearRect(0, 0, width, height);

    // Calculate cell dimensions based on data density
    const uniqueTimes = [...new Set(times)].length;
    const uniquePrices = [...new Set(prices.map(p => Math.round(p * 100)))].length;

    const cellWidth = Math.max(2, width / Math.min(uniqueTimes, 100));
    const cellHeight = Math.max(2, height / Math.min(uniquePrices, 50));

    // Render each data point as a cell
    data.forEach(point => {
        const t = new Date(point.time).getTime();
        const x = ((t - minTime) / timeRange) * (width - cellWidth);
        const y = height - ((point.price - minPrice) / priceRange) * (height - cellHeight) - cellHeight;
        const intensity = Math.min(1, point.volume / maxVolume);

        // Color gradient: blue (low) -> cyan -> yellow -> orange -> red (high)
        let r, g, b;
        if (intensity < 0.25) {
            // Blue to cyan
            const t = intensity / 0.25;
            r = Math.floor(30 * (1 - t));
            g = Math.floor(58 + 140 * t);
            b = Math.floor(138 + 80 * t);
        } else if (intensity < 0.5) {
            // Cyan to yellow
            const t = (intensity - 0.25) / 0.25;
            r = Math.floor(0 + 234 * t);
            g = Math.floor(198 + 41 * t);
            b = Math.floor(218 - 190 * t);
        } else if (intensity < 0.75) {
            // Yellow to orange
            const t = (intensity - 0.5) / 0.25;
            r = Math.floor(234 + 15 * t);
            g = Math.floor(239 - 100 * t);
            b = Math.floor(28 - 20 * t);
        } else {
            // Orange to red
            const t = (intensity - 0.75) / 0.25;
            r = Math.floor(249 - 10 * t);
            g = Math.floor(139 - 70 * t);
            b = Math.floor(8 + 60 * t);
        }

        ctx.fillStyle = `rgba(${r}, ${g}, ${b}, 0.85)`;
        ctx.fillRect(x, y, cellWidth + 1, cellHeight + 1);
    });

    // Draw axis labels
    ctx.fillStyle = '#64748b';
    ctx.font = '10px sans-serif';

    // Time labels (bottom)
    const startTime = new Date(minTime);
    const endTime = new Date(maxTime);
    ctx.textAlign = 'left';
    ctx.fillText(startTime.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }), 4, height - 4);
    ctx.textAlign = 'right';
    ctx.fillText(endTime.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }), width - 4, height - 4);

    // Price labels (left side)
    ctx.textAlign = 'left';
    ctx.fillText(maxPrice.toFixed(1), 4, 14);
    ctx.fillText(minPrice.toFixed(1), 4, height - 16);
};
