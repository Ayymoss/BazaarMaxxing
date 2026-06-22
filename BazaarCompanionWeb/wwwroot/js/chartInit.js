import { disposeChart as globalsDisposeChart } from './charts/chart-globals.js';
import {
    createComparisonChart as scCreateComparisonChart,
    createDepthChart as scCreateDepthChart
} from './charts/specialized-chart-module.js';

// OHLC price chart (Lightweight Charts v5; indicators computed server-side in C#)
import {
    createOhlcChart as ohlcCreate,
    applyOhlcConfig as ohlcApplyConfig,
    updateOhlcTick as ohlcUpdateTick,
    disposeOhlcChart as ohlcDispose,
    resizeOhlcChart as ohlcResize
} from './charts/ohlc-chart-module.js';

// Re-export for Blazor — specialized charts (depth / comparison)
export const createComparisonChart = scCreateComparisonChart;
export const createDepthChart = scCreateDepthChart;
export const disposeChart = globalsDisposeChart;

// Re-export for Blazor — OHLC price chart
export const createOhlcChart = ohlcCreate;
export const applyOhlcConfig = ohlcApplyConfig;
export const updateOhlcTick = ohlcUpdateTick;
export const disposeOhlcChart = ohlcDispose;
export const resizeOhlcChart = ohlcResize;
