import { disposeChart as globalsDisposeChart } from './charts/chart-globals.js';
import {
    createChart as pcCreateChart,
    updateChart as pcUpdateChart,
    updateChartWithTick as pcUpdateChartWithTick,
    createChartWithIndicators as pcCreateChartWithIndicators,
    disposeChartWithIndicators as pcDisposeChartWithIndicators
} from './charts/price-chart-module.js';
import {
    createComparisonChart as scCreateComparisonChart,
    createDepthChart as scCreateDepthChart
} from './charts/specialized-chart-module.js';
import { renderHeatmap as hmRenderHeatmap } from './charts/heatmap-module.js';

// Re-export for Blazor and other modules
export const createChart = pcCreateChart;
export const updateChart = pcUpdateChart;
export const updateChartWithTick = pcUpdateChartWithTick;
export const createChartWithIndicators = pcCreateChartWithIndicators;
export const createComparisonChart = scCreateComparisonChart;
export const createDepthChart = scCreateDepthChart;
export const disposeChart = globalsDisposeChart;
export const disposeChartWithIndicators = pcDisposeChartWithIndicators;

// Attach to window where explicitly required by current implementation
window.renderHeatmap = hmRenderHeatmap;
