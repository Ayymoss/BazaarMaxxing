import { disposeChart as globalsDisposeChart } from './charts/chart-globals.js';
import {
    createComparisonChart as scCreateComparisonChart,
    createDepthChart as scCreateDepthChart
} from './charts/specialized-chart-module.js';
import { renderHeatmap as hmRenderHeatmap } from './charts/heatmap-module.js';

// KLineChart imports
import {
    createKLineChart as klCreateKLineChart,
    updateKLineChart as klUpdateKLineChart,
    updateKLineChartWithTick as klUpdateKLineChartWithTick,
    disposeKLineChart as klDisposeKLineChart,
    toggleIndicator as klToggleIndicator,
    resizeKLineChart as klResizeKLineChart
} from './charts/kline-chart-module.js';

// Re-export for Blazor - LightweightCharts (specialized charts still in use)
export const createComparisonChart = scCreateComparisonChart;
export const createDepthChart = scCreateDepthChart;
export const disposeChart = globalsDisposeChart;

// Re-export for Blazor - KLineChart
export const createKLineChart = klCreateKLineChart;
export const updateKLineChart = klUpdateKLineChart;
export const updateKLineChartWithTick = klUpdateKLineChartWithTick;
export const disposeKLineChart = klDisposeKLineChart;
export const toggleKLineIndicator = klToggleIndicator;
export const resizeKLineChart = klResizeKLineChart;

// Attach to window where explicitly required by current implementation
window.renderHeatmap = hmRenderHeatmap;
