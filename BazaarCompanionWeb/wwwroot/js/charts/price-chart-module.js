import { charts, series, indicatorSeries, disposeChart } from './chart-globals.js';
import { waitForLibrary, formatters } from './chart-utils.js';
import { themeOptions, candleSeriesOptions, volumeSeriesOptions } from './chart-theme.js';

// Store sub-charts for oscillators
const subCharts = {};

// Store visible range persistently (survives chart recreation)
const savedVisibleRanges = {};

// Lock to prevent rapid calls from overwriting saved range
const rangeSaveLocks = {};

export async function createChart(containerId, data) {
    const LightweightCharts = await waitForLibrary();
    if (!LightweightCharts) return;

    const container = document.getElementById(containerId);
    if (!container) return;

    disposeChart(containerId);

    try {
        const chart = LightweightCharts.createChart(container, {
            ...themeOptions,
            localization: { priceFormatter: formatters.price },
        });

        const candlestickSeries = chart.addCandlestickSeries(candleSeriesOptions);

        const chartData = data.map(item => ({
            time: Math.floor(new Date(item.time).getTime() / 1000),
            open: item.open, high: item.high, low: item.low, close: item.close,
        }));

        candlestickSeries.setData(chartData);
        chart.timeScale().fitContent();

        charts[containerId] = chart;
        series[containerId] = candlestickSeries;
    } catch (error) {
        console.error('Error creating chart:', error);
    }
}

export async function updateChart(containerId, data, fitContent = false) {
    if (!charts[containerId]) {
        await createChart(containerId, data);
        return;
    }

    const candlestickSeries = series[containerId];
    if (!candlestickSeries) return;

    const chartData = data.map(item => ({
        time: Math.floor(new Date(item.time).getTime() / 1000),
        open: item.open, high: item.high, low: item.low, close: item.close,
    }));

    candlestickSeries.setData(chartData);
    if (fitContent) charts[containerId].timeScale().fitContent();
}

export async function updateChartWithTick(containerId, tick) {
    if (!charts[containerId]) return;

    const candlestickSeries = series[containerId].candlestick;
    const volumeSeries = indicatorSeries[containerId]?.['Volume'];

    if (!candlestickSeries) return;

    const formattedTick = {
        time: Math.floor(new Date(tick.time).getTime() / 1000),
        open: tick.open,
        high: tick.high,
        low: tick.low,
        close: tick.close
    };

    candlestickSeries.update(formattedTick);

    if (volumeSeries && tick.volume !== undefined) {
        volumeSeries.update({
            time: formattedTick.time,
            value: tick.volume,
            color: tick.close >= tick.open ? 'rgba(38, 166, 154, 0.2)' : 'rgba(239, 83, 80, 0.2)'
        });
    }
}

export async function createChartWithIndicators(containerId, data, indicators, spreadData, supportResistanceLevels, fitContent = false, showVolume = false) {
    const LightweightCharts = await waitForLibrary();
    if (!LightweightCharts) return;

    const container = document.getElementById(containerId);
    if (!container) return;

    // FIRST: Save the current visible range BEFORE ANY changes
    // Use a lock to prevent rapid subsequent calls from overwriting a good saved range
    const isLocked = rangeSaveLocks[containerId];
    
    if (fitContent) {
        // Explicit fit content - clear everything
        delete savedVisibleRanges[containerId];
        delete rangeSaveLocks[containerId];
    } else if (!isLocked && charts[containerId]) {
        // Only save if not locked (first call in a rapid sequence)
        try {
            const currentRange = charts[containerId].timeScale().getVisibleLogicalRange();
            if (currentRange) {
                savedVisibleRanges[containerId] = currentRange;
                // Lock for 500ms to prevent subsequent rapid calls from overwriting
                rangeSaveLocks[containerId] = true;
                setTimeout(() => { delete rangeSaveLocks[containerId]; }, 500);
            }
        } catch (e) { /* ignore */ }
    }
    // If locked, we keep the previously saved range (don't overwrite)

    const chartData = data.map(item => ({
        time: Math.floor(new Date(item.time).getTime() / 1000),
        open: item.open, high: item.high, low: item.low, close: item.close,
    }));

    // Store timestamps for padding sub-chart data
    mainChartTimestamps[containerId] = chartData.map(c => c.time);

    // Determine which oscillators are enabled
    const hasRSI = indicators?.some(i => (i.type || '').toString() === 'RSI');
    const hasMACD = indicators?.some(i => (i.type || '').toString().includes('MACD'));
    const hasSpread = spreadData && spreadData.length > 0;

    // Clean up everything
    cleanupSubCharts(containerId);
    if (charts[containerId]) {
        disposeChart(containerId);
    }

    // Setup container structure for multi-pane layout
    setupChartContainers(container, containerId, hasRSI, hasMACD, hasSpread);

    // Get the main chart container
    const mainChartContainer = document.getElementById(`${containerId}-main`);
    
    const mainHeight = mainChartContainer.clientHeight || parseInt(mainChartContainer.style.height) || 300;
    const mainWidth = mainChartContainer.clientWidth || container.clientWidth || 600;
    
    const mainChart = LightweightCharts.createChart(mainChartContainer, {
        ...themeOptions,
        height: mainHeight,
        width: mainWidth,
        localization: { priceFormatter: formatters.price },
    });

    const candlestickSeries = mainChart.addCandlestickSeries(candleSeriesOptions);
    
    candlestickSeries.priceScale().applyOptions({
        scaleMargins: { top: 0.05, bottom: showVolume ? 0.20 : 0.05 },
    });

    candlestickSeries.setData(chartData);

    // Create legend
    const legend = createLegend(mainChartContainer);
    mainChart.subscribeCrosshairMove((param) => updateLegend(param, containerId, candlestickSeries, chartData, showVolume, data, legend));

    // Subscribe to visible range changes - but ignore during setup
    let ignoreRangeChanges = true;
    mainChart.timeScale().subscribeVisibleLogicalRangeChange((range) => {
        if (range && !ignoreRangeChanges) {
            savedVisibleRanges[containerId] = range;
        }
    });

    charts[containerId] = mainChart;
    series[containerId] = { candlestick: candlestickSeries };
    indicatorSeries[containerId] = {};
    subCharts[containerId] = [];

    // Handle volume on main chart
    if (showVolume) {
        handleVolume(mainChart, containerId, data);
    }

    // Handle overlay indicators (BB, SMA, EMA, VWAP) on main chart
    handleOverlayIndicators(mainChart, containerId, indicators);

    // Handle support/resistance on main chart
    handleSupportResistance(candlestickSeries, containerId, supportResistanceLevels);

    // Create separate sub-charts for oscillators
    if (hasRSI) {
        const rsiIndicator = indicators.find(i => (i.type || '').toString() === 'RSI');
        if (rsiIndicator && rsiIndicator.dataPoints?.length > 0) {
            createRSIChart(LightweightCharts, containerId, rsiIndicator, mainChart);
        }
    }

    if (hasMACD) {
        const macdIndicators = indicators.filter(i => 
            (i.type || '').toString().includes('MACD') || 
            (i.name || '').includes('MACD')
        );
        if (macdIndicators.length > 0 && macdIndicators.some(i => i.dataPoints?.length > 0)) {
            createMACDChart(LightweightCharts, containerId, macdIndicators, mainChart);
        }
    }

    if (hasSpread && spreadData.length > 0) {
        createSpreadChart(LightweightCharts, containerId, spreadData, mainChart);
    }

    // LAST: Restore visible range or fit content
    const finalRange = savedVisibleRanges[containerId] || null;
    
    // Use double requestAnimationFrame to ensure chart is fully rendered
    requestAnimationFrame(() => {
        requestAnimationFrame(() => {
            if (finalRange) {
                mainChart.timeScale().setVisibleLogicalRange(finalRange);
            } else {
                mainChart.timeScale().fitContent();
            }
            
            // Sync sub-charts
            const logicalRange = mainChart.timeScale().getVisibleLogicalRange();
            if (logicalRange && subCharts[containerId]) {
                subCharts[containerId].forEach(({ chart }) => {
                    try {
                        chart.timeScale().setVisibleLogicalRange(logicalRange);
                    } catch (e) { /* ignore */ }
                });
            }
            
            // Enable range saving after everything settles
            setTimeout(() => { ignoreRangeChanges = false; }, 200);
        });
    });
}

function setupChartContainers(container, containerId, hasRSI, hasMACD, hasSpread) {
    // Get container dimensions
    const containerHeight = container.clientHeight || 400;
    const containerWidth = container.clientWidth || 600;
    
    // Check if we already have the structure
    let wrapper = container.querySelector('.chart-panes-wrapper');
    
    if (wrapper) {
        // Remove existing sub-chart containers
        const existingSubCharts = wrapper.querySelectorAll('.sub-chart-container');
        existingSubCharts.forEach(el => el.remove());
    } else {
        // Create wrapper structure
        wrapper = document.createElement('div');
        wrapper.className = 'chart-panes-wrapper';
        wrapper.style.cssText = `display: flex; flex-direction: column; height: ${containerHeight}px; width: ${containerWidth}px; position: absolute; top: 0; left: 0;`;
        
        // Move any existing content
        while (container.firstChild) {
            container.removeChild(container.firstChild);
        }
        container.appendChild(wrapper);
        
        // Create main chart container
        const mainContainer = document.createElement('div');
        mainContainer.id = `${containerId}-main`;
        mainContainer.className = 'main-chart-container';
        wrapper.appendChild(mainContainer);
    }

    // Calculate heights - give sub-charts more room for better visibility
    const subChartCount = (hasRSI ? 1 : 0) + (hasMACD ? 1 : 0) + (hasSpread ? 1 : 0);
    const subChartHeight = subChartCount > 0 ? Math.floor(Math.min(120, containerHeight * 0.22)) : 0;
    const mainChartHeight = containerHeight - (subChartCount * subChartHeight);

    // Update main chart height and width
    const mainContainer = wrapper.querySelector('.main-chart-container');
    if (mainContainer) {
        mainContainer.style.cssText = `height: ${mainChartHeight}px; width: ${containerWidth}px; position: relative;`;
    }

    // Create sub-chart containers
    if (hasRSI) {
        createSubChartContainer(wrapper, containerId, 'rsi', 'RSI (14)', subChartHeight, containerWidth);
    }
    if (hasMACD) {
        createSubChartContainer(wrapper, containerId, 'macd', 'MACD (12,26,9)', subChartHeight, containerWidth);
    }
    if (hasSpread) {
        createSubChartContainer(wrapper, containerId, 'spread', 'Spread %', subChartHeight, containerWidth);
    }
}

function createSubChartContainer(wrapper, containerId, type, label, height, width) {
    const subContainer = document.createElement('div');
    subContainer.id = `${containerId}-${type}`;
    subContainer.className = 'sub-chart-container';
    subContainer.style.cssText = `height: ${height}px; width: ${width}px; position: relative; border-top: 1px solid #334155;`;
    
    // Add label
    const labelEl = document.createElement('div');
    labelEl.className = 'sub-chart-label';
    labelEl.style.cssText = 'position: absolute; left: 8px; top: 4px; z-index: 10; font-size: 10px; color: #94a3b8; font-family: Inter, system-ui, sans-serif; background: rgba(15, 23, 42, 0.7); padding: 2px 6px; border-radius: 3px;';
    labelEl.textContent = label;
    subContainer.appendChild(labelEl);
    
    wrapper.appendChild(subContainer);
}

function createRSIChart(LightweightCharts, containerId, rsiIndicator, mainChart) {
    const container = document.getElementById(`${containerId}-rsi`);
    if (!container) return;

    const timestamps = mainChartTimestamps[containerId] || [];
    const height = parseInt(container.style.height) || container.clientHeight || 100;
    const width = parseInt(container.style.width) || container.clientWidth || 600;

    const rsiChart = LightweightCharts.createChart(container, {
        ...themeOptions,
        height: height,
        width: width,
        rightPriceScale: {
            scaleMargins: { top: 0.1, bottom: 0.1 },
            borderVisible: false,
        },
        timeScale: {
            ...themeOptions.timeScale,
            visible: false, // Hide time scale on sub-charts
        },
        crosshair: {
            horzLine: { visible: true, labelVisible: true },
            vertLine: { visible: true, labelVisible: false },
        },
    });

    // Sync time scale with main chart
    syncTimeScales(mainChart, rsiChart);

    const rsiSeries = rsiChart.addLineSeries({
        color: '#f59e0b',
        lineWidth: 2,
        priceFormat: { type: 'price', precision: 0, minMove: 1 },
    });

    const dataPoints = (rsiIndicator.dataPoints || []).map(dp => ({
        time: Math.floor(new Date(dp.time).getTime() / 1000),
        value: dp.value,
    })).filter(d => d.value != null && !isNaN(d.value));

    // Pad with whitespace to match main chart bar count
    const paddedData = padIndicatorDataWithWhitespace(dataPoints, timestamps);

    if (paddedData.length > 0) {
        rsiSeries.setData(paddedData);
    }

    // Add reference lines
    addRSIReferenceLines(rsiSeries);

    subCharts[containerId].push({ type: 'rsi', chart: rsiChart });
}

function createMACDChart(LightweightCharts, containerId, macdIndicators, mainChart) {
    const container = document.getElementById(`${containerId}-macd`);
    if (!container) return;

    const timestamps = mainChartTimestamps[containerId] || [];
    const height = parseInt(container.style.height) || container.clientHeight || 100;
    const width = parseInt(container.style.width) || container.clientWidth || 600;

    const macdChart = LightweightCharts.createChart(container, {
        ...themeOptions,
        height: height,
        width: width,
        rightPriceScale: {
            scaleMargins: { top: 0.1, bottom: 0.1 },
            borderVisible: false,
        },
        timeScale: {
            ...themeOptions.timeScale,
            visible: false,
        },
        crosshair: {
            horzLine: { visible: true, labelVisible: true },
            vertLine: { visible: true, labelVisible: false },
        },
    });

    // Sync time scale with main chart
    syncTimeScales(mainChart, macdChart);

    // Helper to convert and pad indicator data
    const convertAndPad = (dataPoints, addColor = false) => {
        const converted = (dataPoints || []).map(dp => {
            const point = {
                time: Math.floor(new Date(dp.time).getTime() / 1000),
                value: dp.value,
            };
            if (addColor) {
                point.color = dp.value >= 0 ? 'rgba(38, 166, 154, 0.5)' : 'rgba(239, 83, 80, 0.5)';
            }
            return point;
        }).filter(d => d.value != null && !isNaN(d.value));
        
        // Pad with whitespace to match main chart bar count
        return padIndicatorDataWithWhitespace(converted, timestamps);
    };

    // Add MACD histogram
    const histIndicator = macdIndicators.find(i => (i.type || '').toString() === 'MACDHistogram');
    if (histIndicator && histIndicator.dataPoints?.length > 0) {
        const histSeries = macdChart.addHistogramSeries({
            priceFormat: { type: 'price', precision: 2, minMove: 0.01 },
        });
        const histData = convertAndPad(histIndicator.dataPoints, true);
        if (histData.length > 0) {
            histSeries.setData(histData);
        }
    }

    // Add MACD line
    const macdLine = macdIndicators.find(i => (i.type || '').toString() === 'MACD');
    if (macdLine && macdLine.dataPoints?.length > 0) {
        const macdSeries = macdChart.addLineSeries({
            color: '#3b82f6',
            lineWidth: 2,
            priceFormat: { type: 'price', precision: 2, minMove: 0.01 },
        });
        const macdData = convertAndPad(macdLine.dataPoints);
        if (macdData.length > 0) {
            macdSeries.setData(macdData);
        }
    }

    // Add Signal line
    const signalLine = macdIndicators.find(i => i.name === 'MACD Signal');
    if (signalLine && signalLine.dataPoints?.length > 0) {
        const signalSeries = macdChart.addLineSeries({
            color: '#f59e0b',
            lineWidth: 1,
            priceFormat: { type: 'price', precision: 2, minMove: 0.01 },
        });
        const signalData = convertAndPad(signalLine.dataPoints);
        if (signalData.length > 0) {
            signalSeries.setData(signalData);
        }
    }

    // Add zero line
    const zeroLine = macdChart.addLineSeries({
        color: 'rgba(148, 163, 184, 0.3)',
        lineWidth: 1,
        lineStyle: 2,
        priceFormat: { type: 'price', precision: 2, minMove: 0.01 },
    });
    // Set zero line data spanning the time range
    if (macdLine?.dataPoints?.length > 0) {
        const times = macdLine.dataPoints.map(dp => Math.floor(new Date(dp.time).getTime() / 1000));
        zeroLine.setData([
            { time: Math.min(...times), value: 0 },
            { time: Math.max(...times), value: 0 },
        ]);
    }

    subCharts[containerId].push({ type: 'macd', chart: macdChart });
}

function createSpreadChart(LightweightCharts, containerId, spreadData, mainChart) {
    const container = document.getElementById(`${containerId}-spread`);
    if (!container) return;

    const height = parseInt(container.style.height) || container.clientHeight || 100;
    const width = parseInt(container.style.width) || container.clientWidth || 600;

    const spreadChart = LightweightCharts.createChart(container, {
        ...themeOptions,
        height: height,
        width: width,
        rightPriceScale: {
            scaleMargins: { top: 0.1, bottom: 0.1 },
            borderVisible: false,
        },
        timeScale: {
            ...themeOptions.timeScale,
            visible: false,
        },
        crosshair: {
            horzLine: { visible: true, labelVisible: true },
            vertLine: { visible: true, labelVisible: false },
        },
    });

    // Sync time scale with main chart
    syncTimeScales(mainChart, spreadChart);

    const spreadSeries = spreadChart.addLineSeries({
        color: '#06b6d4',
        lineWidth: 2,
        priceFormat: { type: 'price', precision: 2, minMove: 0.01 },
    });

    const dataPoints = spreadData.map(dp => ({
        time: Math.floor(new Date(dp.time).getTime() / 1000),
        value: dp.value,
    })).filter(d => d.value != null && !isNaN(d.value));

    if (dataPoints.length > 0) {
        spreadSeries.setData(dataPoints);
    }

    subCharts[containerId].push({ type: 'spread', chart: spreadChart });
}

function syncTimeScales(mainChart, subChart) {
    let isSyncing = false;

    // Standard approach: sync using logical range
    // This works because we pad indicator data to match main chart length
    mainChart.timeScale().subscribeVisibleLogicalRangeChange((range) => {
        if (range && !isSyncing) {
            isSyncing = true;
            subChart.timeScale().setVisibleLogicalRange(range);
            isSyncing = false;
        }
    });

    subChart.timeScale().subscribeVisibleLogicalRangeChange((range) => {
        if (range && !isSyncing) {
            isSyncing = true;
            mainChart.timeScale().setVisibleLogicalRange(range);
            isSyncing = false;
        }
    });
}

// Store main chart timestamps for padding sub-chart data
let mainChartTimestamps = {};

// Pad indicator data with whitespace to match main chart bar count
// LightweightCharts uses "whitespace" data (time only, no value) to maintain bar alignment
function padIndicatorDataWithWhitespace(indicatorData, allTimestamps) {
    if (!indicatorData || indicatorData.length === 0 || !allTimestamps || allTimestamps.length === 0) {
        return indicatorData;
    }
    
    // Create a map of indicator values by timestamp
    const indicatorMap = new Map();
    indicatorData.forEach(point => {
        indicatorMap.set(point.time, point);
    });
    
    // Build array with all timestamps - use whitespace for missing values
    return allTimestamps.map(time => {
        const existing = indicatorMap.get(time);
        if (existing) {
            return existing; // Has actual value
        }
        // Whitespace data - just time, no value field
        // This maintains bar count alignment while not drawing anything
        return { time };
    });
}

function cleanupSubCharts(containerId) {
    if (subCharts[containerId]) {
        subCharts[containerId].forEach(({ chart }) => {
            try {
                chart.remove();
            } catch (e) { /* ignore */ }
        });
        subCharts[containerId] = [];
    }
}

function addRSIReferenceLines(rsiSeries) {
    try {
        rsiSeries.createPriceLine({
            price: 70,
            color: 'rgba(239, 68, 68, 0.5)',
            lineWidth: 1,
            lineStyle: 2,
            axisLabelVisible: true,
        });
        rsiSeries.createPriceLine({
            price: 30,
            color: 'rgba(16, 185, 129, 0.5)',
            lineWidth: 1,
            lineStyle: 2,
            axisLabelVisible: true,
        });
        rsiSeries.createPriceLine({
            price: 50,
            color: 'rgba(148, 163, 184, 0.3)',
            lineWidth: 1,
            lineStyle: 2,
            axisLabelVisible: false,
        });
    } catch (e) { /* ignore */ }
}

function createLegend(container) {
    // Remove existing legend
    const existingLegend = container.querySelector('.chart-legend');
    if (existingLegend) existingLegend.remove();

    const legend = document.createElement('div');
    legend.className = 'chart-legend';
    Object.assign(legend.style, {
        position: 'absolute', left: '12px', top: '12px', zIndex: '100',
        fontFamily: 'Inter, system-ui, sans-serif', fontSize: '12px',
        pointerEvents: 'none', color: '#d1d5db', background: 'rgba(15, 23, 42, 0.4)',
        padding: '4px 8px', borderRadius: '4px', lineHeight: '1.4'
    });
    container.appendChild(legend);
    return legend;
}

function updateLegend(param, containerId, candlestickSeries, chartData, showVolume, data, legend) {
    const validData = param.time !== undefined;
    const candle = validData ? param.seriesData.get(candlestickSeries) : chartData[chartData.length - 1];
    const vol = validData ? param.seriesData.get(indicatorSeries[containerId]?.['Volume']) : null;

    if (!candle) return;

    const color = candle.close >= candle.open ? '#10b981' : '#ef4444';
    let legendText = `<div style="display: flex; gap: 8px; flex-wrap: wrap;">
        <span style="color: #94a3b8">O</span> <span style="color: ${color}">${formatters.price(candle.open)}</span>
        <span style="color: #94a3b8">H</span> <span style="color: ${color}">${formatters.price(candle.high)}</span>
        <span style="color: #94a3b8">L</span> <span style="color: ${color}">${formatters.price(candle.low)}</span>
        <span style="color: #94a3b8">C</span> <span style="color: ${color}">${formatters.price(candle.close)}</span>`;

    if (vol || (showVolume && !validData)) {
        const volVal = vol ? vol.value : (data[data.length - 1]?.volume || 0);
        legendText += `<span style="margin-left: 8px; color: #94a3b8">Vol</span> <span style="color: #cbd5e1">${formatters.volume(volVal)}</span>`;
    }

    legend.innerHTML = legendText + `</div>`;
}

function handleVolume(chart, containerId, data) {
    const volumeData = data.filter(item => item.volume != null && item.volume > 0);
    if (volumeData.length === 0) return;

    const volumeSeries = chart.addHistogramSeries(volumeSeriesOptions);

    chart.priceScale('volume').applyOptions({
        visible: false,
        scaleMargins: { top: 0.85, bottom: 0 },
    });

    const volumeChartData = volumeData.map(item => ({
        time: Math.floor(new Date(item.time).getTime() / 1000),
        value: item.volume,
        color: item.close >= item.open ? 'rgba(38, 166, 154, 0.3)' : 'rgba(239, 83, 80, 0.3)',
    }));
    volumeSeries.setData(volumeChartData);

    indicatorSeries[containerId]['Volume'] = volumeSeries;
}

function removeOverlayIndicators(chart, containerId) {
    // Remove all overlay indicator series from the chart
    const indicatorNames = Object.keys(indicatorSeries[containerId] || {});
    indicatorNames.forEach(name => {
        if (name === 'Volume') return; // Keep volume, it's handled separately
        try {
            const series = indicatorSeries[containerId][name];
            if (series) {
                chart.removeSeries(series);
            }
        } catch (e) { /* ignore */ }
    });
    // Clear the indicator series (except volume)
    const volumeSeries = indicatorSeries[containerId]?.['Volume'];
    indicatorSeries[containerId] = {};
    if (volumeSeries) {
        indicatorSeries[containerId]['Volume'] = volumeSeries;
    }
}

function handleOverlayIndicators(chart, containerId, indicators) {
    if (!indicators) return;

    // Only handle overlay indicators (not oscillators)
    // Exclude by type AND name to catch 'MACD Signal' which has type='EMA'
    const overlayIndicators = indicators.filter(i => {
        const type = (i.type || '').toString();
        const name = (i.name || '').toString();
        return !type.includes('RSI') && !type.includes('MACD') && !name.includes('MACD');
    });

    overlayIndicators.forEach(indicator => {
        try {
            const type = (indicator.type || '').toString();
            const isBollinger = type.includes('Bollinger');

            const lineSeries = chart.addLineSeries({
                color: indicator.color || '#3b82f6',
                lineWidth: indicator.lineWidth || 1,
                lineStyle: isBollinger ? 2 : 0,
                priceScaleId: 'left',
            });

            const dataPoints = (indicator.dataPoints || []).map(dp => ({
                time: Math.floor(new Date(dp.time).getTime() / 1000),
                value: dp.value,
            })).filter(d => d.value != null && !isNaN(d.value));

            if (dataPoints.length > 0) {
                lineSeries.setData(dataPoints);
                indicatorSeries[containerId][indicator.name] = lineSeries;
            }
        } catch (e) { console.warn('Error adding overlay indicator:', indicator.name, e); }
    });
}

function handleSupportResistance(candlestickSeries, containerId, levels) {
    // Remove existing S/R price lines
    if (series[containerId]?.priceLines) {
        series[containerId].priceLines.forEach(line => {
            try {
                candlestickSeries.removePriceLine(line);
            } catch (e) { /* ignore */ }
        });
    }
    series[containerId].priceLines = [];

    if (!levels || levels.length === 0) return;

    levels.forEach((level, index) => {
        const isSupport = level.type === 'Support';
        const color = isSupport ? '#10b981' : '#ef4444';

        const priceLine = candlestickSeries.createPriceLine({
            price: level.price,
            color: color,
            lineWidth: Math.ceil(level.strength * 2) + 1,
            lineStyle: 2,
            axisLabelVisible: true,
            title: `${isSupport ? 'S' : 'R'} (${level.touchCount})`,
        });

        series[containerId].priceLines.push(priceLine);
    });
}

// Export cleanup function
export function disposeChartWithIndicators(containerId) {
    cleanupSubCharts(containerId);
    disposeChart(containerId);
    
    // Clean up container structure
    const container = document.getElementById(containerId);
    if (container) {
        const wrapper = container.querySelector('.chart-panes-wrapper');
        if (wrapper) {
            wrapper.remove();
        }
    }
}
