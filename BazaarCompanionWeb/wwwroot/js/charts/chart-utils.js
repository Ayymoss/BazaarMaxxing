export function getLightweightCharts() {
    // Try multiple possible locations where the library might be exposed
    if (window.LightweightCharts) return window.LightweightCharts;
    if (window.lightweightCharts) return window.lightweightCharts;
    if (globalThis.LightweightCharts) return globalThis.LightweightCharts;
    if (globalThis.lightweightCharts) return globalThis.lightweightCharts;
    if (window.lightweightcharts) return window.lightweightcharts;
    return undefined;
}

export async function waitForLibrary(maxAttempts = 10) {
    let LightweightCharts = getLightweightCharts();
    let attempts = 0;

    while (typeof LightweightCharts === 'undefined' && attempts < maxAttempts) {
        await new Promise(resolve => setTimeout(resolve, 100));
        LightweightCharts = getLightweightCharts();
        attempts++;
    }

    return LightweightCharts;
}

export const formatters = {
    price: (price) => `$${price.toLocaleString('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 2 })}`,
    percent: (price) => `${price.toFixed(2)}%`,
    volume: (v) => {
        if (v >= 1000000) return (v / 1000000).toFixed(2) + 'M';
        if (v >= 1000) return (v / 1000).toFixed(2) + 'K';
        return v.toFixed(0);
    }
};
