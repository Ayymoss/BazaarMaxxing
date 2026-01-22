export const themeOptions = {
    layout: {
        background: { color: '#1e293b' },
        textColor: '#d1d5db',
    },
    grid: {
        vertLines: { color: '#334155' },
        horzLines: { color: '#334155' },
    },
    rightPriceScale: {
        borderColor: '#475569',
        autoScale: true,
    },
    timeScale: {
        borderColor: '#475569',
        timeVisible: true,
        rightOffset: 5,
        barSpacing: 6,
        minBarSpacing: 2,
    },
};

export const candleSeriesOptions = {
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

export const volumeSeriesOptions = {
    priceFormat: {
        type: 'volume',
    },
    priceScaleId: 'volume',
    scaleMargins: {
        top: 0.8,
        bottom: 0,
    },
    lastValueVisible: false,
    priceLineVisible: false,
};
