export function renderHeatmap(canvasId, data) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    const width = canvas.offsetWidth;
    const height = canvas.offsetHeight;

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

    ctx.clearRect(0, 0, width, height);

    const uniqueTimes = [...new Set(times)].length;
    const uniquePrices = [...new Set(prices.map(p => Math.round(p * 100)))].length;
    const cellWidth = Math.max(2, width / Math.min(uniqueTimes, 100));
    const cellHeight = Math.max(2, height / Math.min(uniquePrices, 50));

    data.forEach(point => {
        const t = new Date(point.time).getTime();
        const x = ((t - minTime) / timeRange) * (width - cellWidth);
        const y = height - ((point.price - minPrice) / priceRange) * (height - cellHeight) - cellHeight;
        const intensity = Math.min(1, point.volume / maxVolume);

        const color = getIntensityColor(intensity);
        ctx.fillStyle = `rgba(${color.r}, ${color.g}, ${color.b}, 0.85)`;
        ctx.fillRect(x, y, cellWidth + 1, cellHeight + 1);
    });

    drawAxisLabels(ctx, width, height, minTime, maxTime, minPrice, maxPrice);
}

function getIntensityColor(intensity) {
    if (intensity < 0.25) {
        const t = intensity / 0.25;
        return { r: Math.floor(30 * (1 - t)), g: Math.floor(58 + 140 * t), b: Math.floor(138 + 80 * t) };
    } else if (intensity < 0.5) {
        const t = (intensity - 0.25) / 0.25;
        return { r: Math.floor(0 + 234 * t), g: Math.floor(198 + 41 * t), b: Math.floor(218 - 190 * t) };
    } else if (intensity < 0.75) {
        const t = (intensity - 0.5) / 0.25;
        return { r: Math.floor(234 + 15 * t), g: Math.floor(239 - 100 * t), b: Math.floor(28 - 20 * t) };
    } else {
        const t = (intensity - 0.75) / 0.25;
        return { r: Math.floor(249 - 10 * t), g: Math.floor(139 - 70 * t), b: Math.floor(8 + 60 * t) };
    }
}

function drawAxisLabels(ctx, width, height, minTime, maxTime, minPrice, maxPrice) {
    ctx.fillStyle = '#64748b';
    ctx.font = '10px sans-serif';

    const startTime = new Date(minTime);
    const endTime = new Date(maxTime);
    ctx.textAlign = 'left';
    ctx.fillText(startTime.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }), 4, height - 4);
    ctx.textAlign = 'right';
    ctx.fillText(endTime.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }), width - 4, height - 4);

    ctx.textAlign = 'left';
    ctx.fillText(maxPrice.toFixed(1), 4, 14);
    ctx.fillText(minPrice.toFixed(1), 4, height - 16);
}
