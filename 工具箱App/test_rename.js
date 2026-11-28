/**
 * 测试改名脚本：生成脚本 → 真跑一遍 → 验证文件确实改名了。
 */
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const RS = require('./desktop/js/rename-script.js');

const ROOT = process.env.RTEST_DIR || 'C:/Users/13783/AppData/Local/Temp/rtest2';

function setup() {
  fs.rmSync(ROOT, { recursive: true, force: true });
  fs.mkdirSync(path.join(ROOT, '子目录A'), { recursive: true });
  fs.writeFileSync(path.join(ROOT, '照片 01.jpg'), 'a');
  fs.writeFileSync(path.join(ROOT, '照片 02.jpg'), 'b');
  fs.writeFileSync(path.join(ROOT, '笔记.txt'), 'c');
  fs.writeFileSync(path.join(ROOT, '子目录A', '深层 文件.jpg'), 'd');
}

const changed = [
  { from: { name: '照片 01.jpg', path: '照片 01.jpg' }, to: '旅行_01.jpg' },
  { from: { name: '照片 02.jpg', path: '照片 02.jpg' }, to: '旅行_02.jpg' },
  { from: { name: '笔记.txt', path: '笔记.txt' }, to: '旅行_03.txt' },
  { from: { name: '深层 文件.jpg', path: '子目录A/深层 文件.jpg' }, to: '旅行_04.jpg' }
];

(async () => {
  setup();

  const rules = { mode: 'prefix', prefix: '旅行_', suffix: '', start: 1, pad: 2 };
  const files = RS.buildFiles(changed, rules);
  console.log('生成文件：', files.map(f => f.name).join(', '));

  /* 用假的目录句柄写到磁盘 */
  const fakeRoot = {
    async getFileHandle(name) {
      return {
        async createWritable() {
          return {
            async write(bytes) { fs.writeFileSync(path.join(ROOT, name), Buffer.from(bytes)); },
            async close() { }
          };
        }
      };
    },
    async getDirectoryHandle(name) {
      const d = path.join(ROOT, name);
      fs.mkdirSync(d, { recursive: true });
      return {
        async getFileHandle(fname) {
          return {
            async createWritable() {
              return {
                async write(bytes) { fs.writeFileSync(path.join(d, fname), Buffer.from(bytes)); },
                async close() { }
              };
            }
          };
        }
      };
    }
  };
  await RS.writeFiles(fakeRoot, files);

  console.log('\n--- 生成后 ---');
  console.log(fs.readdirSync(ROOT).join('  '));

  /* 真跑脚本 */
  console.log('\n--- 执行改名脚本 ---');
  const ps = path.join(ROOT, '改名脚本.cmd');
  const psContent = fs.readFileSync(ps, 'utf8');
  console.log('cmd 内容：\n' + psContent);

  /* cmd 里有 pause，改为直接跑 ps1 */
  const { execSync } = require('child_process');
  try {
    execSync(`powershell -NoProfile -ExecutionPolicy Bypass -File "${path.join(ROOT, '_rename_0.ps1')}"`, {
      encoding: 'utf8', shell: 'cmd.exe', cwd: ROOT
    });
    execSync(`powershell -NoProfile -ExecutionPolicy Bypass -File "${path.join(ROOT, '_rename_1.ps1')}"`, {
      encoding: 'utf8', shell: 'cmd.exe', cwd: ROOT
    });
    execSync(`powershell -NoProfile -ExecutionPolicy Bypass -File "${path.join(ROOT, '_merge.ps1')}"`, {
      encoding: 'utf8', shell: 'cmd.exe', cwd: ROOT
    });
  } catch (e) {
    console.log('执行输出:', (e.stdout || '') + (e.stderr || ''));
  }

  console.log('\n--- 改名后 ---');
  console.log('根目录:', fs.readdirSync(ROOT).join('  '));
  console.log('子目录A:', fs.readdirSync(path.join(ROOT, '子目录A')).join('  '));

  const expect = ['旅行_01.jpg', '旅行_02.jpg', '旅行_03.txt', '改名脚本.cmd', '改名预览.csv', '改名备份.csv'];
  const actual = fs.readdirSync(ROOT);
  const ok = expect.every(e => actual.includes(e));
  const okSub = fs.readdirSync(path.join(ROOT, '子目录A')).includes('旅行_04.jpg');
  const backup = fs.existsSync(path.join(ROOT, '改名备份.csv'));

  console.log('\n结果：');
  console.log((ok ? '✓' : '✗') + ' 根目录文件已改名');
  console.log((okSub ? '✓' : '✗') + ' 子目录文件已改名');
  console.log((backup ? '✓' : '✗') + ' 备份清单已生成');
  if (backup) console.log('\n备份内容：\n' + fs.readFileSync(path.join(ROOT, '改名备份.csv'), 'utf8'));

  process.exit(ok && okSub && backup ? 0 : 1);
})();
