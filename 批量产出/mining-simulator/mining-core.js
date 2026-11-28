/* ===== 挖矿核心模拟引擎 ===== */

// ==================== 硬件数据库 ====================
const HARDWARE_DB = {
    rtx4090: {
        name: 'NVIDIA RTX 4090',
        baseHashRate: 125.5,
        powerDraw: 380,
        tdp: 450,
        memorySize: 24,
        algo: 'ethash',
        efficiency: 3.03,
        tempBase: 55
    },
    rtx4080: {
        name: 'NVIDIA RTX 4080',
        baseHashRate: 96.0,
        powerDraw: 270,
        tdp: 320,
        memorySize: 16,
        algo: 'ethash',
        efficiency: 2.81,
        tempBase: 52
    },
    rtx4070ti: {
        name: 'NVIDIA RTX 4070 Ti',
        baseHashRate: 70.0,
        powerDraw: 180,
        tdp: 285,
        memorySize: 12,
        algo: 'ethash',
        efficiency: 2.57,
        tempBase: 48
    },
    rtx3090: {
        name: 'NVIDIA RTX 3090',
        baseHashRate: 63.0,
        powerDraw: 280,
        tdp: 350,
        memorySize: 24,
        algo: 'ethash',
        efficiency: 4.44,
        tempBase: 60
    },
    rtx3080: {
        name: 'NVIDIA RTX 3080',
        baseHashRate: 52.0,
        powerDraw: 200,
        tdp: 320,
        memorySize: 10,
        algo: 'ethash',
        efficiency: 3.85,
        tempBase: 56
    },
    rtx3070: {
        name: 'NVIDIA RTX 3070',
        baseHashRate: 38.0,
        powerDraw: 120,
        tdp: 220,
        memorySize: 8,
        algo: 'ethash',
        efficiency: 3.16,
        tempBase: 50
    },
    rx7900xtx: {
        name: 'AMD RX 7900 XTX',
        baseHashRate: 104.0,
        powerDraw: 260,
        tdp: 355,
        memorySize: 24,
        algo: 'ethash',
        efficiency: 2.50,
        tempBase: 54
    },
    rx6800: {
        name: 'AMD RX 6800',
        baseHashRate: 36.5,
        powerDraw: 145,
        tdp: 250,
        memorySize: 16,
        algo: 'ethash',
        efficiency: 3.97,
        tempBase: 52
    },
    antminer_s19: {
        name: '比特大陆 AntMiner S19 Pro',
        baseHashRate: 110000,
        powerDraw: 2770,
        tdp: 3000,
        memorySize: 0,
        algo: 'sha256',
        efficiency: 25.2,
        tempBase: 65
    },
    antminer_s21: {
        name: '比特大陆 AntMiner S21',
        baseHashRate: 200000,
        powerDraw: 3500,
        tdp: 4000,
        memorySize: 0,
        algo: 'sha256',
        efficiency: 17.5,
        tempBase: 62
    }
};

// ==================== 算法数据库 ====================
const ALGORITHM_DB = {
    ethash: {
        name: 'Ethash',
        coin: 'ETH',
        coinName: '以太坊经典',
        poolUrl: 'stratum+tcp://etc.2miners.com:1010',
        rewardBlock: 2.5,
        avgTime: 13.5
    },
    kawpow: {
        name: 'KawPow',
        coin: 'RVN',
        coinName: '瑞云币',
        poolUrl: 'stratum+tcp://rvn.pool.minergate.com:4756',
        rewardBlock: 7.5,
        avgTime: 1.0
    },
    sha256: {
        name: 'SHA-256',
        coin: 'BTC',
        coinName: '比特币',
        poolUrl: 'stratum+tcp://btc.pool.binance.com:3333',
        rewardBlock: 3.125,
        avgTime: 10.0
    },
    scrypt: {
        name: 'Scrypt',
        coin: 'LTC',
        coinName: '莱特币',
        poolUrl: 'stratum+tcp://ltc.pool.antpool.org:3333',
        rewardBlock: 6.25,
        avgTime: 2.5
    },
    randomx: {
        name: 'RandomX',
        coin: 'XMR',
        coinName: '门罗币',
        poolUrl: 'stratum+tcp://monero.crypto-pool.fr:3333',
        rewardBlock: 0.6,
        avgTime: 2.0
    },
    autolykos: {
        name: 'Autolykos',
        coin: 'ERG',
        coinName: '艾格瑞姆',
        poolUrl: 'stratum+tcp://erg.2miners.com:1111',
        rewardBlock: 1.92,
        avgTime: 3.0
    }
};

// ==================== 挖矿状态 ====================
const miningState = {
    running: false,
    paused: false,
    startTime: null,
    
    hardware: {
        gpu: 'rtx4090',
        algorithm: 'ethash',
        pool: 'binance'
    },
    
    overclocking: {
        coreClock: 0,
        memoryClock: 0,
        powerLimit: 100,
        fanSpeed: 65
    },
    
    stats: {
        hashRate: 0,
        targetHashRate: 0,
        sharesAccepted: 0,
        sharesRejected: 0,
        totalShares: 0,
        blocksFound: 0,
        currentNonce: 0,
        lastShareTime: Date.now(),
        shareInterval: 8000,
        blockTarget: null,
        
        temperatures: {
            gpuCore: 45,
            gpuMemory: 35,
            vram: 38,
            pcb: 30
        },
        
        power: {
            gpuWatts: 0,
            systemWatts: 0
        },
        
        earnings: {
            btc: 0,
            usd: 0,
            today: 0,
            week: 0,
            month: 0
        }
    },
    
    history: {
        hashRates: [],
        prices: [],
        earnings: [],
        temps: []
    }
};

// ==================== 网络参数（模拟）====================
const networkParams = {
    difficulty: 83.23e12,
    networkHashRate: 620e12,
    blockHeight: 840127,
    blockReward: 3.125,
    halvingDistance: 210000,
    nextHalvingBlocks: 69873,
    priceUSD: 104250,
    priceChange24h: 2.34,
    mempoolTx: 15234,
    avgBlockTime: 9.8,
    activeAddresses: 923456
};

// ==================== 价格历史生成器 ====================
let priceHistoryData = [];
for (let i = 0; i < 100; i++) {
    const base = 98000 + Math.sin(i * 0.1) * 5000 + Math.cos(i * 0.05) * 3000;
    priceHistoryData.push(base);
}

let hashRateHistoryData = [];
let earningsHistoryData = [];
let tempHistoryData = [];

// ==================== 辅助函数 ====================
function generateHex(length) {
    const chars = '0123456789abcdef';
    let result = '';
    for (let i = 0; i < length; i++) {
        result += chars[Math.floor(Math.random() * 16)];
    }
    return result;
}

function formatHash(hash) {
    if (!hash) return generateHex(64);
    return hash;
}

function formatHashRate(rate, unit) {
    if (unit === 'TH') {
        return rate.toFixed(2) + ' TH/s';
    } else if (unit === 'GH') {
        return rate.toFixed(2) + ' GH/s';
    } else if (unit === 'MH') {
        return rate.toFixed(2) + ' MH/s';
    }
    return rate.toFixed(2) + ' H/s';
}

function formatBTC(amount) {
    return amount.toFixed(8) + ' BTC';
}

function formatUSD(amount) {
    return '$' + amount.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function getTimestamp() {
    const now = new Date();
    return now.toTimeString().split(' ')[0];
}

function calculateEffectiveHashRate() {
    const hw = HARDWARE_DB[miningState.hardware.gpu];
    if (!hw) return 0;
    
    let rate = hw.baseHashRate;
    
    // 超频调整
    rate *= (1 + miningState.overclocking.coreClock / 1000);
    rate *= (1 + miningState.overclocking.memoryClock / 2000);
    rate *= (miningState.overclocking.powerLimit / 100);
    
    // 风扇转速影响温控降频
    if (miningState.overclocking.fanSpeed < 40 && hw.tempBase > 55) {
        rate *= 0.95; // 低风扇轻微降频
    }
    
    // 温度降频
    if (miningState.stats.temperatures.gpuCore > 85) {
        rate *= 0.8; // 严重降频
    } else if (miningState.stats.temperatures.gpuCore > 80) {
        rate *= 0.92;
    }
    
    return rate;
}

function calculatePowerDraw() {
    const hw = HARDWARE_DB[miningState.hardware.gpu];
    if (!hw) return { gpu: 0, system: 0 };
    
    const pcieSystemBase = hw.algo === 'sha256' ? 150 : 80;
    
    let gpuPower = hw.powerDraw * (miningState.overclocking.powerLimit / 100);
    
    // 高频增加功耗
    const extraCorePower = Math.abs(miningState.overclocking.coreClock) * 0.3;
    const extraMemPower = Math.abs(miningState.overclocking.memoryClock) * 0.05;
    gpuPower += extraCorePower + extraMemPower;
    
    // 不超过 TDP
    gpuPower = Math.min(gpuPower, hw.tdp);
    gpuPower = Math.max(gpuPower, hw.powerDraw * 0.3); // 最低功耗
    
    return {
        gpu: Math.round(gpuPower),
        system: Math.round(gpuPower + pcieSystemBase)
    };
}

function getDifficultyTarget() {
    const hw = HARDWARE_DB[miningState.hardware.gpu];
    if (!hw) return null;
    
    const rate = calculateEffectiveHashRate();
    
    if (hw.algo === 'sha256') {
        return networkParams.difficulty;
    } else {
        const baseDiff = networkParams.difficulty / 1e6;
        return baseDiff;
    }
}

function estimateBlockTime(targetDifficulty) {
    if (!targetDifficulty) return Infinity;
    
    const rate = calculateEffectiveHashRate();
    const algo = ALGORITHM_DB[miningState.hardware.algorithm];
    
    let networkDifficulty;
    if (miningState.hardware.algorithm === 'sha256') {
        networkDifficulty = networkParams.difficulty;
    } else {
        networkDifficulty = networkParams.difficulty / 1e6;
    }
    
    const probability = rate / (networkDifficulty * 1e6);
    
    if (probability <= 0) return Infinity;
    
    const blocksPerSecond = probability;
    const expectedSeconds = 1 / blocksPerSecond;
    
    const adjustedTime = expectedSeconds * (algo.avgTime / 10);
    
    return adjustedTime;
}

// ==================== 挖矿操作 ====================
function startMining() {
    miningState.running = true;
    miningState.paused = false;
    miningState.startTime = Date.now();
    
    addLog('success', '[MINER] 启动挖矿流程...');
    addLog('info', `[GPU] ${HARDWARE_DB[miningState.hardware.gpu].name}`);
    addLog('info', `[ALGO] ${ALGORITHM_DB[miningState.hardware.algorithm].name} (${ALGORITHM_DB[miningState.hardware.algorithm].coin})`);
    addLog('info', `[POOL] ${getPoolConfig().url}`);
    addLog('success', '[MINER] 挖矿启动成功');
    
    document.querySelector('.status-badge').textContent = '● 挖矿中';
    document.querySelector('.status-badge').className = 'status-badge connected';
    document.body.classList.add('mining-active');
}

function pauseMining() {
    miningState.paused = !miningState.paused;
    
    if (miningState.paused) {
        addLog('warning', '[MINER] 挖矿已暂停');
        document.querySelector('.status-badge').textContent = '● 已暂停';
        document.querySelector('.status-badge').className = 'status-badge disconnected';
        document.body.classList.remove('mining-active');
    } else {
        addLog('info', '[MINER] 挖矿已恢复');
        document.querySelector('.status-badge').textContent = '● 挖矿中';
        document.querySelector('.status-badge').className = 'status-badge connected';
        document.body.classList.add('mining-active');
    }
}

function resetMining() {
    if (!confirm('确定要重置所有挖矿数据吗？')) return;
    
    miningState.running = false;
    miningState.paused = false;
    miningState.startTime = null;
    
    miningState.stats = {
        hashRate: 0,
        targetHashRate: 0,
        sharesAccepted: 0,
        sharesRejected: 0,
        totalShares: 0,
        blocksFound: 0,
        currentNonce: 0,
        lastShareTime: Date.now(),
        shareInterval: 8000,
        blockTarget: null,
        
        temperatures: { gpuCore: 45, gpuMemory: 35, vram: 38, pcb: 30 },
        power: { gpuWatts: 0, systemWatts: 0 },
        earnings: { btc: 0, usd: 0, today: 0, week: 0, month: 0 }
    };
    
    hashRateHistoryData = [];
    earningsHistoryData = [];
    tempHistoryData = [];
    
    updateAllDisplays();
    
    // 清空终端
    const terminal = document.getElementById('terminal');
    terminal.innerHTML = `
        <div class="terminal-line system">系统重置完成</div>
        <div class="terminal-line">&nbsp;</div>
    `;
    
    // 清空日志
    const logContainer = document.getElementById('event-log');
    logContainer.innerHTML = '<div class="log-entry info">[SYS] 系统重置完成</div>';
    
    // 清空区块列表
    document.getElementById('blocks-list').innerHTML = 
        '<div class="block-item empty"><span>暂无区块记录...</span></div>';
    
    addLog('warning', '[SYS] 挖矿会话已重置');
}

// ==================== 份额提交 ====================
function submitShare() {
    if (!miningState.running || miningState.paused) return;
    
    const nonce = Math.floor(Math.random() * 0xFFFFFFFF).toString(16).padStart(8, '0');
    const header = generateHex(16) + ':' + nonce;
    
    // 验证份额是否符合难度要求
    const difficulty = getDifficultyTarget();
    const isValid = Math.random() < 0.97; // 97% 接受率
    
    miningState.stats.lastShareTime = Date.now();
    miningState.stats.totalShares++;
    
    if (isValid) {
        miningState.stats.sharesAccepted++;
        
        // 偶尔在终端显示
        if (miningState.stats.sharesAccepted % 10 === 0) {
            addTerminalLine(`${getTimestamp()} 份额已确认 (难度${difficulty.toExponential(1)}) Nonce:${nonce}`, 'success');
        }
        
        // 小额随机奖励
        const shareReward = (Math.random() * 0.00000001 + 0.000000001);
        miningState.stats.earnings.btc += shareReward;
        miningState.stats.earnings.usd += shareReward * networkParams.priceUSD;
        
    } else {
        miningState.stats.sharesRejected++;
        addTerminalLine(`${getTimestamp()} 份额被拒绝 (过期工作)`, 'error');
        addLog('warning', '[SHARE] 被拒绝：过期工作');
    }
    
    // 动态更新间隔
    const elapsed = Date.now() - miningState.stats.lastShareTime;
    miningState.stats.shareInterval = Math.max(3000, Math.min(30000, elapsed));
}

// ==================== 区块发现 ====================
function checkBlockDiscovery() {
    if (!miningState.running || miningState.paused) return;
    
    const difficulty = getDifficultyTarget();
    const hashRate = calculateEffectiveHashRate();
    const hw = HARDWARE_DB[miningState.hardware.gpu];
    
    // 计算概率 - 极罕见事件
    let prob;
    if (hw.algo === 'sha256') {
        // BTC 难度极高
        prob = (hashRate * 1e12) / (difficulty * 1e12);
        prob *= 0.001; // 额外缩放因子以增加真实感
    } else {
        // 山寨币难度较低
        prob = (hashRate * 1e6) / (difficulty * 1e6);
        prob *= 0.1; // 对山寨币更现实的概率
    }
    
    // 每次检查的极低概率
    if (Math.random() < prob) {
        findBlock();
    }
}

function findBlock() {
    miningState.stats.blocksFound++;
    const hw = HARDWARE_DB[miningState.hardware.gpu];
    const algo = ALGORITHM_DB[miningState.hardware.algorithm];
    const pool = getPoolConfig();
    
    const reward = algo.rewardBlock * (1 - pool.fee);
    const feeAmount = algo.rewardBlock * pool.fee;
    
    // 首次区块奖励和累积缩放
    const bonus = 1 + (miningState.stats.blocksFound - 1) * 0.1;
    const finalReward = reward * bonus;
    
    miningState.stats.earnings.btc += finalReward;
    miningState.stats.earnings.usd += finalReward * networkParams.priceUSD;
    
    // 创建区块条目
    const blockHash = generateHex(64);
    const prevHash = generateHex(64);
    const txCount = Math.floor(Math.random() * 2000) + 500;
    
    const blockEntry = {
        height: networkParams.blockHeight + miningState.stats.blocksFound,
        hash: blockHash,
        prevHash: prevHash,
        reward: finalReward,
        fee: feeAmount,
        txs: txCount,
        timestamp: new Date().toLocaleString('zh-CN')
    };
    
    addBlockToList(blockEntry);
    
    // 终端庆祝
    addTerminalLine('', '');
    addTerminalLine('═══════════════════════════════════════════════', 'success');
    addTerminalLine(`🎉 挖到区块 #${blockEntry.height}！🎉`, 'success');
    addTerminalLine('═══════════════════════════════════════════════', 'success');
    addTerminalLine(`  Hash: ${blockEntry.hash.substring(0, 20)}...`, 'info');
    addTerminalLine(`  奖励: ${finalReward.toFixed(8)} ${algo.coin}`, 'success');
    addTerminalLine(`  价值: ${formatUSD(finalReward * networkParams.priceUSD)}`, 'success');
    addTerminalLine(`  交易数: ${txCount}`, 'info');
    addTerminalLine(`  矿池手续费: ${(feeAmount * networkParams.priceUSD).toFixed(2)} 美元`, 'warning');
    addTerminalLine(`  时间戳: ${blockEntry.timestamp}`, 'info');
    addTerminalLine('───────────────────────────────────────────────', '');
    
    // 日志条目
    addLog('block', `🧱 挖到区块 #${blockEntry.height}！+${finalReward.toFixed(8)} ${algo.coin}`);
    
    // 庆祝动画
    celebrateBlockFound();
    
    // 增加区块高度
    networkParams.blockHeight += miningState.stats.blocksFound;
}

function celebrateBlockFound() {
    // 闪烁指标卡片
    document.querySelectorAll('.metric-card').forEach(card => {
        card.classList.add('block-celebration');
        setTimeout(() => card.classList.remove('block-celebration'), 800);
    });
    
    // 闪烁钱包余额
    const walletEl = document.getElementById('wallet-balance');
    walletEl.classList.add('number-updated');
    setTimeout(() => walletEl.classList.remove('number-updated'), 500);
}

// ==================== 温度模拟 ====================
function updateTemperatures() {
    if (!miningState.running) {
        // 冷却
        Object.keys(miningState.stats.temperatures).forEach(key => {
            miningState.stats.temperatures[key] = Math.max(25, miningState.stats.temperatures[key] - 0.5);
        });
        return;
    }
    
    if (miningState.paused) {
        // 暂停时缓慢冷却
        Object.keys(miningState.stats.temperatures).forEach(key => {
            miningState.stats.temperatures[key] = Math.max(30, miningState.stats.temperatures[key] - 0.2);
        });
        return;
    }
    
    const hw = HARDWARE_DB[miningState.hardware.gpu];
    const oc = miningState.overclocking;
    const fanSpeed = oc.fanSpeed / 100;
    
    // 基于负载和散热的目标温度
    const loadFactor = Math.min(1, oc.powerLimit / 100);
    const coolingFactor = Math.max(0.2, fanSpeed);
    
    const targetCore = hw.tempBase + (oc.powerLimit - 100) * 0.5 + Math.abs(oc.coreClock) * 0.15;
    const targetMemory = hw.tempBase - 10 + Math.abs(oc.memoryClock) * 0.1;
    
    // 带随机的温度趋近
    const noise = () => (Math.random() - 0.5) * 2;
    
    miningState.stats.temperatures.gpuCore += (targetCore - miningState.stats.temperatures.gpuCore) * 0.02 * coolingFactor + noise() * 0.1;
    miningState.stats.temperatures.gpuMemory += (targetMemory - miningState.stats.temperatures.gpuMemory) * 0.015 * coolingFactor + noise() * 0.05;
    miningState.stats.temperatures.vram = miningState.stats.temperatures.gpuMemory + 3;
    miningState.stats.temperatures.pcb = miningState.stats.temperatures.gpuCore * 0.85;
    
    // 环境温度波动
    const ambientNoise = Math.sin(Date.now() / 60000) * 1;
    miningState.stats.temperatures.gpuCore += ambientNoise;
    
    // 限制范围
    miningState.stats.temperatures.gpuCore = Math.max(25, Math.min(105, miningState.stats.temperatures.gpuCore));
    miningState.stats.temperatures.gpuMemory = Math.max(20, Math.min(110, miningState.stats.temperatures.gpuMemory));
    miningState.stats.temperatures.vram = Math.max(20, Math.min(115, miningState.stats.temperatures.vram));
    miningState.stats.temperatures.pcb = Math.max(20, Math.min(95, miningState.stats.temperatures.pcb));
}

// ==================== 价格模拟 ====================
function updatePrice() {
    // 带漂移的随机游走
    const change = (Math.random() - 0.48) * 500;
    networkParams.priceUSD = Math.max(50000, networkParams.priceUSD + change);
    
    // 偶尔的大幅度波动
    if (Math.random() < 0.02) {
        const bigMove = (Math.random() - 0.5) * 3000;
        networkParams.priceUSD += bigMove;
    }
    
    // 偶尔的新闻事件
    if (Math.random() < 0.005) {
        const events = [
            { text: '突发：大型机构宣布购买 BTC', impact: 2000 },
            { text: 'SEC 批准新的加密货币 ETF', impact: 1500 },
            { text: '主要市场监管担忧', impact: -1800 },
            { text: '网络升级成功部署', impact: 800 },
            { text: '鲸鱼转移大量 BTC', impact: -500 }
        ];
        const event = events[Math.floor(Math.random() * events.length)];
        networkParams.priceUSD += event.impact;
        addLog(event.impact > 0 ? 'success' : 'warning', `[新闻] ${event.text}`);
    }
    
    networkParams.priceUSD = Math.max(50000, Math.min(200000, networkParams.priceUSD));
    
    // 保存历史点
    priceHistoryData.push(networkParams.priceUSD);
    if (priceHistoryData.length > 200) priceHistoryData.shift();
}

// ==================== 显示更新 ====================
function updateAllDisplays() {
    const hw = HARDWARE_DB[miningState.hardware.gpu];
    const algo = ALGORITHM_DB[miningState.hardware.algorithm];
    const oc = miningState.overclocking;
    const stats = miningState.stats;
    
    // 有效算力
    const effectiveRate = calculateEffectiveHashRate();
    stats.hashRate = effectiveRate;
    
    let displayRate, rateUnit;
    if (hw.algo === 'sha256') {
        displayRate = effectiveRate / 1000; // TH 转换为 PH
        rateUnit = 'PH/s';
    } else {
        displayRate = effectiveRate;
        rateUnit = 'MH/s';
    }
    
    // 算力显示
    document.getElementById('hashrate').textContent = displayRate.toFixed(2) + ' ' + rateUnit;
    document.getElementById('hashrate-trend').textContent = 
        miningState.running ? `▲ 运行中 - ${algo.name}` : '▼ 已停止';
    
    // 钱包余额
    document.getElementById('wallet-balance').textContent = formatBTC(stats.earnings.btc);
    document.getElementById('wallet-usd').textContent = formatUSD(stats.earnings.usd);
    
    // 份额统计
    document.getElementById('shares-accepted').textContent = stats.sharesAccepted.toLocaleString();
    document.getElementById('shares-rejected').textContent = stats.sharesRejected.toLocaleString();
    
    const totalShares = stats.sharesAccepted + stats.sharesRejected;
    const rejectPct = totalShares > 0 ? ((stats.sharesRejected / totalShares) * 100).toFixed(1) : 0;
    const timeSinceLastShare = (Date.now() - stats.lastShareTime) / 1000;
    const shareRate = timeSinceLastShare > 0 ? (1 / timeSinceLastShare).toFixed(2) : '0.00';
    document.getElementById('share-rate').textContent = `${shareRate} 份/秒`;
    document.getElementById('reject-rate').textContent = `${rejectPct}% 淘汰率`;
    
    // 功耗
    const power = calculatePowerDraw();
    stats.power.gpuWatts = power.gpu;
    stats.power.systemWatts = power.system;
    
    document.getElementById('gpu-power').textContent = power.gpu + ' W';
    document.getElementById('system-power').textContent = power.system + ' W';
    
    // 电费计算
    const electricityRate = parseFloat(document.getElementById('electricity-rate').value) || 0.12;
    const costPerHour = (power.system / 1000) * electricityRate;
    const costPerDay = costPerHour * 24;
    
    document.getElementById('cost-per-hour').textContent = formatUSD(costPerHour);
    document.getElementById('cost-per-day').textContent = formatUSD(costPerDay);
    
    // 效率
    const efficiency = power.gpu / (effectiveRate || 1);
    document.getElementById('efficiency').textContent = efficiency.toFixed(2) + ' J/MH';
    
    // 温度
    const temps = stats.temperatures;
    document.getElementById('gpu-core').textContent = temps.gpuCore.toFixed(1) + '°C';
    document.getElementById('gpu-mem').textContent = temps.gpuMemory.toFixed(1) + '°C';
    document.getElementById('vram-temp').textContent = temps.vram.toFixed(1) + '°C';
    document.getElementById('pcb-temp').textContent = temps.pcb.toFixed(1) + '°C';
    
    // 温度条
    updateTempBar('gpu-core-bar', temps.gpuCore, 105);
    updateTempBar('gpu-mem-bar', temps.gpuMemory, 110);
    updateTempBar('vram-bar', temps.vram, 115);
    updateTempBar('pcb-bar', temps.pcb, 95);
    
    // 网络数据
    document.getElementById('network-hr').textContent = networkParams.networkHashRate.toLocaleString() + ' EH/s';
    document.getElementById('difficulty').textContent = (networkParams.difficulty / 1e12).toFixed(2) + ' T';
    document.getElementById('block-height').textContent = '#' + networkParams.blockHeight.toLocaleString();
    document.getElementById('halving').textContent = '~' + networkParams.nextHalvingBlocks + ' 个区块';
    document.getElementById('block-reward').textContent = networkParams.blockReward + ' BTC';
    document.getElementById('btc-price').textContent = formatUSD(networkParams.priceUSD);
    
    // 预计挖到区块数
    const blockProb = getEstimatedBlockProbability();
    document.getElementById('estimated-blocks').textContent = blockProb.toFixed(10);
    
    // 收益汇总
    document.getElementById('earn-today').textContent = formatBTC(stats.earnings.today);
    document.getElementById('earn-week').textContent = formatBTC(stats.earnings.week);
    document.getElementById('earn-month').textContent = formatBTC(stats.earnings.month);
    document.getElementById('earn-total').textContent = formatBTC(stats.earnings.btc);
    
    document.getElementById('earn-today-usd').textContent = formatUSD(stats.earnings.today_usd || 0);
    document.getElementById('earn-week-usd').textContent = formatUSD(stats.earnings.week_usd || 0);
    document.getElementById('earn-month-usd').textContent = formatUSD(stats.earnings.month_usd || 0);
    document.getElementById('earn-total-usd').textContent = formatUSD(stats.earnings.usd);
    
    // 净利润
    const netProfit = stats.earnings.usd - costPerDay * (miningState.startTime ? 
        (Date.now() - miningState.startTime) / (1000 * 60 * 60 * 24) : 0);
    const profitEl = document.getElementById('net-profit');
    profitEl.textContent = formatUSD(netProfit);
    profitEl.className = 'profit-value ' + (netProfit >= 0 ? 'positive' : 'negative');
    
    // 运行时间
    if (miningState.startTime) {
        const uptime = Date.now() - miningState.startTime;
        const hrs = Math.floor(uptime / 3600000);
        const mins = Math.floor((uptime % 3600000) / 60000);
        const secs = Math.floor((uptime % 60000) / 1000);
        document.getElementById('uptime').textContent = 
            `${hrs.toString().padStart(2, '0')}:${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    }
    
    // 定期更新图表
    updateCharts();
}

function updateTempBar(id, value, max) {
    const bar = document.getElementById(id);
    const pct = Math.min(100, (value / max) * 100);
    bar.style.width = pct + '%';
    
    // 根据温度设置颜色
    bar.className = 'bar-fill';
    if (value < 60) {
        bar.classList.add('temp-low');
    } else if (value < 75) {
        bar.classList.add('temp-mid');
    } else if (value < 85) {
        bar.classList.add('temp-high');
    } else {
        bar.classList.add('temp-critical');
    }
}

function updateCharts() {
    if (!hashRateHistoryData.length || hashRateHistoryData[hashRateHistoryData.length - 1] !== 
        miningState.stats.hashRate) {
        hashRateHistoryData.push(miningState.stats.hashRate);
        if (hashRateHistoryData.length > 200) hashRateHistoryData.shift();
    }
    
    if (!earningsHistoryData.length || earningsHistoryData[earningsHistoryData.length - 1] !== 
        miningState.stats.earnings.usd) {
        earningsHistoryData.push(miningState.stats.earnings.usd);
        if (earningsHistoryData.length > 200) earningsHistoryData.shift();
    }
    
    if (!tempHistoryData.length || tempHistoryData[tempHistoryData.length - 1] !== 
        miningState.stats.temperatures.gpuCore) {
        tempHistoryData.push(miningState.stats.temperatures.gpuCore);
        if (tempHistoryData.length > 200) tempHistoryData.shift();
    }
    
    // 绘制当前图表
    switch(currentChartType) {
        case 'hashrate':
            if (hashrateChart) hashrateChart.setData(hashRateHistoryData);
            break;
        case 'price':
            if (priceChart) priceChart.setData(priceHistoryData);
            break;
        case 'earnings':
            if (earningsChart) earningsChart.setData(earningsHistoryData);
            break;
        case 'temp':
            if (tempChart) tempChart.setData(tempHistoryData);
            break;
    }
}

function getEstimatedBlockProbability() {
    const hashRate = calculateEffectiveHashRate();
    const hw = HARDWARE_DB[miningState.hardware.gpu];
    
    if (hw.algo === 'sha256') {
        const networkHR = networkParams.networkHashRate;
        return hashRate / (networkHR * networkParams.difficulty);
    } else {
        // 山寨币简化计算
        const altCoinHR = networkParams.networkHashRate / 1e6;
        const altCoinDiff = networkParams.difficulty / 1e6;
        return hashRate / (altCoinHR * altCoinDiff);
    }
}

// ==================== 终端与日志 ====================
function addTerminalLine(text, className) {
    const terminal = document.getElementById('terminal');
    const line = document.createElement('div');
    line.className = 'terminal-line ' + (className || '');
    
    if (text === '') {
        line.innerHTML = '&nbsp;';
    } else {
        line.textContent = text;
    }
    
    terminal.appendChild(line);
    
    // 自动滚动
    terminal.scrollTop = terminal.scrollHeight;
    
    // 限制行数
    while (terminal.children.length > 100) {
        terminal.removeChild(terminal.firstChild);
    }
}

function addLog(type, message) {
    const logContainer = document.getElementById('event-log');
    const entry = document.createElement('div');
    entry.className = 'log-entry ' + type;
    entry.innerHTML = `<span class="timestamp">[${getTimestamp()}]</span> ${message}`;
    logContainer.insertBefore(entry, logContainer.firstChild);
    
    // 限制条目数
    while (logContainer.children.length > 50) {
        logContainer.removeChild(logContainer.lastChild);
    }
}

function clearLog() {
    document.getElementById('event-log').innerHTML = 
        '<div class="log-entry info">[SYS] 日志已清空</div>';
}

function addBlockToList(block) {
    const list = document.getElementById('blocks-list');
    
    // 移除空消息
    const emptyItem = list.querySelector('.empty');
    if (emptyItem) emptyItem.remove();
    
    const item = document.createElement('div');
    item.className = 'block-item';
    item.innerHTML = `
        <div><strong>#${block.height}</strong> — ${block.reward.toFixed(8)} BTC</div>
        <div class="block-hash">${block.hash}</div>
        <div style="font-size:9px;color:var(--text-muted);margin-top:4px;">
            交易数: ${block.txs} • ${block.timestamp}
        </div>
    `;
    
    list.insertBefore(item, list.firstChild);
    
    // 限制显示区块数
    while (list.children.length > 10) {
        list.removeChild(list.lastChild);
    }
}

// ==================== 矿池配置 ====================
function getPoolConfig() {
    const pools = {
        binance: { name: '币安矿池', url: 'stratum+tcp://us.pool.btc.com:3333', fee: 0.02 },
        f2pool: { name: '鱼池 F2Pool', url: 'stratum+tcp://btc.f2pool.com:3333', fee: 0.025 },
        antpool: { name: '蚂蚁矿池 AntPool', url: 'stratum+tcp://btc.antpool.com:3333', fee: 0.02 },
        marathon: { name: '马拉松矿池 Marathon', url: 'stratum+tcp://pool.marathondigital.com:3333', fee: 0.015 },
        viabtc: { name: 'ViaBTC', url: 'stratum+tcp://btc.viabtc.com:3333', fee: 0.01 }
    };
    return pools[miningState.hardware.pool] || pools.binance;
}

function getPoolName() {
    return getPoolConfig().name;
}

// ==================== 初始化 ====================
function initMiningEngine() {
    // 基于默认显卡设置初始状态
    const hw = HARDWARE_DB['rtx4090'];
    miningState.hardware.gpu = 'rtx4090';
    miningState.hardware.algorithm = 'ethash';
    miningState.hardware.pool = 'binance';
    
    // 初始化价格历史
    priceHistoryData = priceHistoryData.map(p => p + (Math.random() - 0.5) * 1000);
    
    // 初始显示
    updateAllDisplays();
    
    console.log('挖矿引擎已初始化');
    console.log('显卡:', HARDWARE_DB['rtx4090'].name);
    console.log('算法:', ALGORITHM_DB['ethash'].name);
    console.log('矿池:', getPoolConfig().name);
}

initMiningEngine();
