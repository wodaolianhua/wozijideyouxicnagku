/* ===== 主应用程序控制器 ===== */

let currentChartType = 'hashrate';
let updateInterval = null;
let miningLoop = null;

// ==================== 初始化 ====================
document.addEventListener('DOMContentLoaded', () => {
    // 初始化图表系统
    setTimeout(() => {
        initCharts();
    }, 100);
    
    // 设置显示刷新循环（200ms 一次平滑更新）
    setInterval(updateAllDisplays, 200);
    
    // 温度模拟更新（每 500ms）
    setInterval(updateTemperatures, 500);
    
    // 份额提交（每 4 秒模拟间隔）
    setInterval(() => {
        if (miningState.running && !miningState.paused) {
            submitShare();
            
            // 收益追踪
            const earningsPerShare = Math.random() * 0.00000005 + 0.00000001;
            miningState.stats.earnings.today += earningsPerShare;
            miningState.stats.earnings.today_usd += earningsPerShare * networkParams.priceUSD;
            miningState.stats.earnings.week += earningsPerShare * 7;
            miningState.stats.earnings.week_usd += earningsPerShare * 7 * networkParams.priceUSD;
            miningState.stats.earnings.month += earningsPerShare * 30;
            miningState.stats.earnings.month_usd += earningsPerShare * 30 * networkParams.priceUSD;
        }
    }, 4000);
    
    // 区块发现检查（每 2 秒）
    setInterval(checkBlockDiscovery, 2000);
    
    // 价格更新（每 5 秒）
    setInterval(updatePrice, 5000);
    
    // 网络参数波动
    setInterval(fluctuateNetworkParams, 3000);
    
    // 模拟终端哈希输出
    setInterval(simulateHashOutput, 1500);
    
    // 难度调整（每 10 秒模拟一次约 2 万块的周期）
    setInterval(adjustDifficulty, 10000);
    
    // 初始日志条目
    addLog('info', '[CONFIG] 显卡: ' + HARDWARE_DB['rtx4090'].name);
    addLog('info', '[CONFIG] 算法: Ethash');
    addLog('info', '[NET] 矿池: ' + getPoolConfig().name);
    
    console.log('🎮 比特币挖矿模拟器 Pro版 v3.2.1 已初始化');
});

// ==================== 控制函数 ====================
function togglePause() {
    if (!miningState.running) {
        startMining();
    } else {
        pauseMining();
    }
}

function resetMining() {
    window.resetMining();
}

function changeGPU(value) {
    miningState.hardware.gpu = value;
    document.getElementById('gpu-name').textContent = HARDWARE_DB[value].name;
    
    // 重置超频设置
    miningState.overclocking.coreClock = 0;
    miningState.overclocking.memoryClock = 0;
    miningState.overclocking.powerLimit = 100;
    
    document.getElementById('core-slider').value = 0;
    document.getElementById('mem-slider').value = 0;
    document.getElementById('power-slider').value = 100;
    document.getElementById('core-value').textContent = '+0 MHz';
    document.getElementById('mem-value').textContent = '+0 MHz';
    document.getElementById('power-value').textContent = '100%';
    
    addLog('info', `[硬件] 更换显卡为 ${HARDWARE_DB[value].name}`);
    
    if (miningState.running) {
        addLog('warning', '[挖矿] 算力已更新 — 应用新设置');
    }
}

function changeAlgorithm(value) {
    miningState.hardware.algorithm = value;
    const algo = ALGORITHM_DB[value];
    document.getElementById('algo-name').textContent = algo.name + ' (' + algo.coin + ')';
    
    addLog('info', `[算法] 切换到 ${algo.name} (${algo.coin})`);
    addTerminalLine(`算法已更改为 ${algo.name}`, 'warning');
}

function changePool(value) {
    miningState.hardware.pool = value;
    document.getElementById('pool-name').textContent = getPoolName();
    
    addLog('info', `[矿池] 连接到 ${getPoolConfig().name}（手续费 ${(getPoolConfig().fee * 100).toFixed(1)}%`);
    addTerminalLine(`正在连接 ${getPoolConfig().url}...`, 'info');
}

function updateFanSpeed(value) {
    miningState.overclocking.fanSpeed = parseInt(value);
    document.getElementById('fan-value').textContent = value + '%';
}

function updateOC(type, value) {
    value = parseInt(value);
    
    switch(type) {
        case 'core':
            miningState.overclocking.coreClock = value;
            document.getElementById('core-value').textContent = (value >= 0 ? '+' : '') + value + ' MHz';
            break;
        case 'mem':
            miningState.overclocking.memoryClock = value;
            document.getElementById('mem-value').textContent = (value >= 0 ? '+' : '') + value + ' MHz';
            break;
        case 'power':
            miningState.overclocking.powerLimit = value;
            document.getElementById('power-value').textContent = value + '%';
            break;
    }
    
    if (miningState.running && !miningState.paused) {
        addLog('info', `[超频] ${type.toUpperCase()} 已调整: ${value}`);
    }
}

function updateCosts() {
    updateAllDisplays();
}

// ==================== 网络模拟 ====================
function fluctuateNetworkParams() {
    // 小幅难度变化
    networkParams.difficulty += (Math.random() - 0.5) * 1e10;
    networkParams.difficulty = Math.max(50e12, networkParams.difficulty);
    
    // 内存池交易数波动
    networkParams.mempoolTx += Math.floor((Math.random() - 0.5) * 500);
    networkParams.mempoolTx = Math.max(5000, networkParams.mempoolTx);
    
    // 活跃地址波动
    networkParams.activeAddresses += Math.floor((Math.random() - 0.5) * 10000);
    networkParams.activeAddresses = Math.max(500000, networkParams.activeAddresses);
}

function adjustDifficulty() {
    // 每个"难度调整周期"
    const diffChange = (Math.random() - 0.45) * 5; // 轻微上涨偏向
    const diffPct = diffChange / networkParams.difficulty * 1e12;
    
    if (Math.abs(diffPct) > 0.5) {
        networkParams.difficulty *= (1 + diffChange / 10000);
        networkParams.difficulty = Math.max(10e12, networkParams.difficulty);
        
        addLog('warning', `[网络] 难度调整: ${diffChange > 0 ? '+' : ''}${diffChange.toFixed(2)}%`);
    }
}

// ==================== 终端模拟 ====================
function simulateHashOutput() {
    if (!miningState.running || miningState.paused) return;
    
    // 偶尔在终端显示哈希尝试
    if (Math.random() < 0.3) {
        const nonce = Math.floor(Math.random() * 0xFFFFFFFF).toString(16).padStart(8, '0');
        const headerHash = generateHex(20);
        
        addTerminalLine(
            `${getTimestamp()} ⛏ ${headerHash} | Nonce:${nonce}`,
            'hash-line'
        );
    }
}

// ==================== 工具函数 ====================
window.togglePause = togglePause;
window.resetMining = resetMining;
window.changeGPU = changeGPU;
window.changeAlgorithm = changeAlgorithm;
window.changePool = changePool;
window.updateFanSpeed = updateFanSpeed;
window.updateOC = updateOC;
window.updateCosts = updateCosts;
window.switchChart = switchChart;
window.clearLog = clearLog;

// ==================== 键盘快捷键 ====================
document.addEventListener('keydown', (e) => {
    if (e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT') return;
    
    switch(e.key.toLowerCase()) {
        case ' ':
            e.preventDefault();
            togglePause();
            break;
        case 'r':
            if (e.ctrlKey) {
                e.preventDefault();
                resetMining();
            }
            break;
    }
});

console.log('%c⛏️ 比特币挖矿模拟器 Pro版 v3.2.1', 'color: #f7931a; font-size: 16px; font-weight: bold;');
console.log('%c操作说明:', 'color: #06b6d4; font-size: 12px;');
console.log('  空格键 — 开始/暂停挖矿');
console.log('  Ctrl+R — 重置所有数据');
console.log('  滑块 — 调整超频设置');
