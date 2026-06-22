export const themeOptions = {
    layout: {
        background: { type: 'solid', color: 'transparent' },
        textColor: '#9aa0aa',
        fontFamily: "'IBM Plex Sans', system-ui, sans-serif",
        attributionLogo: false,
    },
    grid: {
        vertLines: { color: 'rgba(148,163,184,0.06)' },
        horzLines: { color: 'rgba(148,163,184,0.06)' },
    },
    rightPriceScale: {
        borderColor: '#2a2f38',
        autoScale: true,
    },
    timeScale: {
        borderColor: '#2a2f38',
        timeVisible: true,
        rightOffset: 5,
        barSpacing: 6,
        minBarSpacing: 2,
    },
};

export const candleSeriesOptions = {
    upColor: '#16c784',
    downColor: '#f6465d',
    borderVisible: false,
    wickUpColor: '#16c784',
    wickDownColor: '#f6465d',
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
