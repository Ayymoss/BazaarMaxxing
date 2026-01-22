export const charts = {};
export const series = {};
export const indicatorSeries = {};

export function disposeChart(containerId) {
    if (charts[containerId]) {
        charts[containerId].remove();
        delete charts[containerId];
        delete series[containerId];
        delete indicatorSeries[containerId];
    }
}
