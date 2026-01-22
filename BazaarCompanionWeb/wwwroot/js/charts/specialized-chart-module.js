import { charts, series, disposeChart } from './chart-globals.js';
import { waitForLibrary, formatters } from './chart-utils.js';
import { themeOptions } from './chart-theme.js';

export async function createComparisonChart(containerId, normalizedData, productKeys) {
    const LightweightCharts = await waitForLibrary();
    if (!LightweightCharts) return;

    const container = document.getElementById(containerId);
    if (!container) return;

    disposeChart(containerId);

    const chart = LightweightCharts.createChart(container, {
        ...themeOptions,
        localization: { priceFormatter: formatters.percent },
    });

    const colors = ['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899'];

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
            if (!series[containerId]) series[containerId] = {};
            series[containerId][productKey] = lineSeries;
        }
    });

    chart.timeScale().fitContent();
    charts[containerId] = chart;
}

export async function createDepthChart(containerId, bidData, askData) {
    const LightweightCharts = await waitForLibrary();
    if (!LightweightCharts) return;

    const container = document.getElementById(containerId);
    if (!container) return;

    disposeChart(containerId);

    const chart = LightweightCharts.createChart(container, {
        layout: { background: { type: 'solid', color: 'transparent' }, textColor: '#94a3b8' },
        grid: { vertLines: { color: 'rgba(51, 65, 85, 0.3)' }, horzLines: { color: 'rgba(51, 65, 85, 0.3)' } },
        rightPriceScale: { borderVisible: false },
        timeScale: { visible: false },
        crosshair: { mode: 0 },
        handleScale: false,
        handleScroll: false,
    });

    const addDepthSeries = (color, top, bottom) => {
        const options = { lineColor: color, topColor: top, bottomColor: bottom, lineWidth: 2 };
        return typeof chart.addAreaSeries === 'function'
            ? chart.addAreaSeries(options)
            : chart.addSeries('Area', options);
    };

    const bidSeries = addDepthSeries('#22c55e', 'rgba(34, 197, 94, 0.4)', 'rgba(34, 197, 94, 0.05)');
    const askSeries = addDepthSeries('#ef4444', 'rgba(239, 68, 68, 0.4)', 'rgba(239, 68, 68, 0.05)');

    const bidPoints = bidData.map((d, i) => ({ time: i, value: d.volume }));
    const askPoints = askData.map((d, i) => ({ time: bidData.length + i, value: d.volume }));

    if (bidSeries && bidPoints.length > 0) bidSeries.setData(bidPoints);
    if (askSeries && askPoints.length > 0) askSeries.setData(askPoints);

    chart.timeScale().fitContent();
    charts[containerId] = chart;
    series[containerId] = { bid: bidSeries, ask: askSeries };
}
