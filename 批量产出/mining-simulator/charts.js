/* ===== Chart Rendering System ===== */

class MiningChart {
    constructor(canvasId) {
        this.canvas = document.getElementById(canvasId);
        this.ctx = this.canvas.getContext('2d');
        this.data = [];
        this.maxPoints = 200;
        this.lineWidth = 2;
        this.gridColor = 'rgba(30, 48, 71, 0.5)';
        this.textColor = '#64748b';
        this.animFrame = null;
    }

    setData(points) {
        this.data = points.slice(-this.maxPoints);
        this.draw();
    }

    addPoint(value) {
        this.data.push(value);
        if (this.data.length > this.maxPoints) {
            this.data.shift();
        }
        this.draw();
    }

    clear() {
        this.data = [];
        this.draw();
    }

    draw() {
        const ctx = this.ctx;
        const canvas = this.canvas;
        
        // High DPI support
        const dpr = window.devicePixelRatio || 1;
        const rect = canvas.getBoundingClientRect();
        canvas.width = rect.width * dpr;
        canvas.height = rect.height * dpr;
        ctx.scale(dpr, dpr);
        canvas.style.width = rect.width + 'px';
        canvas.style.height = rect.height + 'px';

        const width = rect.width;
        const height = rect.height;
        const padding = { top: 20, right: 50, bottom: 25, left: 60 };

        ctx.clearRect(0, 0, width, height);

        if (this.data.length < 2) {
            ctx.fillStyle = this.textColor;
            ctx.font = '12px Consolas, monospace';
            ctx.textAlign = 'center';
            ctx.fillText('No data yet...', width / 2, height / 2);
            return;
        }

        const chartWidth = width - padding.left - padding.right;
        const chartHeight = height - padding.top - padding.bottom;

        // Find min/max
        let minVal = Math.min(...this.data);
        let maxVal = Math.max(...this.data);
        const range = maxVal - minVal || 1;
        minVal -= range * 0.05;
        maxVal += range * 0.05;
        const dataRange = maxVal - minVal;

        // Draw grid
        ctx.strokeStyle = this.gridColor;
        ctx.lineWidth = 0.5;
        const gridLines = 5;
        for (let i = 0; i <= gridLines; i++) {
            const y = padding.top + (chartHeight / gridLines) * i;
            ctx.beginPath();
            ctx.moveTo(padding.left, y);
            ctx.lineTo(width - padding.right, y);
            ctx.stroke();

            // Y-axis labels
            const val = maxVal - (dataRange / gridLines) * i;
            ctx.fillStyle = this.textColor;
            ctx.font = '10px Consolas, monospace';
            ctx.textAlign = 'right';
            ctx.fillText(this.formatValue(val), padding.left - 8, y + 3);
        }

        // X-axis time labels
        ctx.textAlign = 'center';
        const timeLabels = 6;
        for (let i = 0; i <= timeLabels; i++) {
            const x = padding.left + (chartWidth / timeLabels) * i;
            const idx = Math.floor((this.data.length - 1) * (i / timeLabels));
            const timeIdx = this.data.length - 1 - idx;
            const label = this.getTimeLabel(timeIdx);
            ctx.fillText(label, x, height - 5);
        }

        // Draw gradient fill
        const gradient = ctx.createLinearGradient(0, padding.top, 0, height - padding.bottom);
        gradient.addColorStop(0, this.fillColorStart || 'rgba(6, 182, 212, 0.2)');
        gradient.addColorStop(1, 'rgba(6, 182, 212, 0.01)');

        ctx.beginPath();
        ctx.moveTo(padding.left, height - padding.bottom);
        
        for (let i = 0; i < this.data.length; i++) {
            const x = padding.left + (chartWidth / (this.maxPoints - 1)) * (i - (this.maxPoints - this.data.length));
            const normalizedY = (this.data[i] - minVal) / dataRange;
            const y = padding.top + chartHeight * (1 - normalizedY);
            
            if (i === 0) {
                ctx.lineTo(x, y);
            } else {
                // Smooth curve
                const prevX = padding.left + (chartWidth / (this.maxPoints - 1)) * (i - 1 - (this.maxPoints - this.data.length));
                const prevNormalizedY = (this.data[i - 1] - minVal) / dataRange;
                const prevY = padding.top + chartHeight * (1 - prevNormalizedY);
                const cpX = (prevX + x) / 2;
                ctx.bezierCurveTo(cpX, prevY, cpX, y, x, y);
            }
        }
        
        ctx.lineTo(padding.left + chartWidth, height - padding.bottom);
        ctx.closePath();
        ctx.fillStyle = gradient;
        ctx.fill();

        // Draw line
        ctx.beginPath();
        ctx.strokeStyle = this.lineColor || '#06b6d4';
        ctx.lineWidth = this.lineWidth;
        ctx.lineJoin = 'round';
        ctx.lineCap = 'round';

        for (let i = 0; i < this.data.length; i++) {
            const x = padding.left + (chartWidth / (this.maxPoints - 1)) * (i - (this.maxPoints - this.data.length));
            const normalizedY = (this.data[i] - minVal) / dataRange;
            const y = padding.top + chartHeight * (1 - normalizedY);
            
            if (i === 0) {
                ctx.moveTo(x, y);
            } else {
                const prevX = padding.left + (chartWidth / (this.maxPoints - 1)) * (i - 1 - (this.maxPoints - this.data.length));
                const prevNormalizedY = (this.data[i - 1] - minVal) / dataRange;
                const prevY = padding.top + chartHeight * (1 - prevNormalizedY);
                const cpX = (prevX + x) / 2;
                ctx.bezierCurveTo(cpX, prevY, cpX, y, x, y);
            }
        }
        ctx.stroke();

        // Draw last point dot
        const lastX = padding.left + chartWidth;
        const lastNormY = (this.data[this.data.length - 1] - minVal) / dataRange;
        const lastY = padding.top + chartHeight * (1 - lastNormY);

        ctx.beginPath();
        ctx.arc(lastX, lastY, 4, 0, Math.PI * 2);
        ctx.fillStyle = this.lineColor || '#06b6d4';
        ctx.fill();

        ctx.beginPath();
        ctx.arc(lastX, lastY, 6, 0, Math.PI * 2);
        ctx.strokeStyle = 'rgba(6, 182, 212, 0.3)';
        ctx.lineWidth = 2;
        ctx.stroke();

        // Store for tooltip
        this._lastPoint = { x: lastX, y: lastY, value: this.data[this.data.length - 1] };
    }

    formatValue(value) {
        if (Math.abs(value) >= 1e12) return (value / 1e12).toFixed(1) + 'T';
        if (Math.abs(value) >= 1e9) return (value / 1e9).toFixed(1) + 'B';
        if (Math.abs(value) >= 1e6) return (value / 1e6).toFixed(1) + 'M';
        if (Math.abs(value) >= 1e3) return (value / 1e3).toFixed(1) + 'K';
        return value.toFixed(1);
    }

    getTimeLabel(offset) {
        const seconds = offset * 2;
        const mins = Math.floor(seconds / 60);
        const secs = seconds % 60;
        return `${mins}:${secs.toString().padStart(2, '0')}`;
    }

    setStyle({ lineWidth, lineColor, fillColorStart }) {
        if (lineWidth) this.lineWidth = lineWidth;
        if (lineColor) this.lineColor = lineColor;
        if (fillColorStart) this.fillColorStart = fillColorStart;
    }
}

// Initialize charts
let hashrateChart, priceChart, earningsChart, tempChart;

function initCharts() {
    hashrateChart = new MiningChart('main-chart');
    hashrateChart.setStyle({
        lineColor: '#22c55e',
        fillColorStart: 'rgba(34, 197, 94, 0.2)',
        lineWidth: 2
    });

    priceChart = new MiningChart('main-chart');
    priceChart.setStyle({
        lineColor: '#f7931a',
        fillColorStart: 'rgba(247, 147, 26, 0.2)',
        lineWidth: 2
    });

    earningsChart = new MiningChart('main-chart');
    earningsChart.setStyle({
        lineColor: '#a855f7',
        fillColorStart: 'rgba(168, 85, 247, 0.2)',
        lineWidth: 2
    });

    tempChart = new MiningChart('main-chart');
    tempChart.setStyle({
        lineColor: '#ef4444',
        fillColorStart: 'rgba(239, 68, 68, 0.15)',
        lineWidth: 2
    });

    // Default to hashrate chart
    switchChart('hashrate');
}

function switchChart(type) {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    event.target.classList.add('active');
    
    currentChartType = type;
    
    switch(type) {
        case 'hashrate':
            hashrateChart.setData(hashRateHistory);
            break;
        case 'price':
            priceChart.setData(priceHistory);
            break;
        case 'earnings':
            earningsChart.setData(earningsHistory);
            break;
        case 'temp':
            tempChart.setData(tempHistory);
            break;
    }
}
