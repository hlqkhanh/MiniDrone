// Browser checks against mock APIs, not a substitute for an ESP32 hardware test.
// Run: node tests/portal/wifi-portal.test.cjs (Node 22+, Chrome installed).
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

async function main() {
  const source = fs.readFileSync(path.resolve(__dirname, '../../firmware/DroneMotionMpu6050/WifiPortalPage.h'), 'utf8');
  const html = source.match(/R"PORTAL\(([\s\S]*?)\)PORTAL"/)[1].replace('__SETUP_TOKEN__', 'test-token');
  const unsafeSsid = '<img src=x onerror="window.injected=1">';
  let state = 'idle', submitted = null, connections = 0;
  const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, 'http://localhost');
    const json = (code, value) => { res.writeHead(code, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(value)); };
    if (url.pathname === '/') { res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }); res.end(html); return; }
    if (url.pathname === '/api/status') { json(200, { state, ssid: submitted?.ssid || '', mode: submitted?.mode || 'personal', ip: state === 'connected' ? '192.168.1.105' : '', port: 3333, setupSsid: 'DroneMotion_Setup-TEST', saved: state === 'connected', message: '' }); return; }
    if (url.pathname === '/api/scan') { json(200, url.searchParams.has('start') ? { scanning: true, networks: [] } : { scanning: false, networks: [{ ssid: 'TestNetwork', rssi: -80, mode: 'personal' }, { ssid: unsafeSsid, rssi: -60, mode: 'open' }, { ssid: 'TestNetwork', rssi: -40, mode: 'personal' }] }); return; }
    if (req.method === 'POST') {
      if (req.headers['x-setup-token'] !== 'test-token') { json(403, { error: 'Missing token' }); return; }
      const chunks = []; for await (const chunk of req) chunks.push(chunk);
      if (url.pathname === '/api/connect') {
        submitted = Object.fromEntries(new URLSearchParams(Buffer.concat(chunks).toString()));
        connections++; state = 'connected'; json(202, { ok: true }); return;
      }
      if (url.pathname === '/api/forget') { state = 'idle'; submitted = null; json(200, { ok: true }); return; }
    }
    json(404, { error: 'Unknown endpoint' });
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const address = `http://127.0.0.1:${server.address().port}`;
  const chromePath = process.env.PORTAL_TEST_CHROME || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'drone-portal-browser-'));
  const chrome = spawn(chromePath, ['--headless', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { windowsHide: true, stdio: 'ignore' });
  let socket;
  try {
    const portFile = path.join(profile, 'DevToolsActivePort');
    for (let i = 0; !fs.existsSync(portFile) && i < 100; i++) await pause(100);
    assert.ok(fs.existsSync(portFile), 'Chrome debugging port did not start');
    const debugPort = fs.readFileSync(portFile, 'utf8').split('\n')[0];
    const target = await (await fetch(`http://127.0.0.1:${debugPort}/json/new?${address}`, { method: 'PUT' })).json();
    socket = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    let id = 0; const pending = new Map();
    socket.addEventListener('message', event => { const message = JSON.parse(event.data); if (pending.has(message.id)) { pending.get(message.id)(message); pending.delete(message.id); } });
    const command = (method, params = {}) => new Promise((resolve, reject) => {
      const number = ++id;
      const timer = setTimeout(() => { pending.delete(number); reject(new Error(`CDP timeout: ${method}`)); }, 10000);
      pending.set(number, result => { clearTimeout(timer); result.error ? reject(new Error(JSON.stringify(result.error))) : resolve(result.result); });
      socket.send(JSON.stringify({ id: number, method, params }));
    });
    const evaluate = async expression => {
      const result = await command('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
      if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
      return result.result?.value;
    };
    const until = async expression => {
      for (let i = 0; i < 70; i++) { if (await evaluate(expression)) return; await pause(100); }
      throw new Error(`Browser condition not met: ${expression}`);
    };
    await until(`document.getElementById('setup-name')?.textContent === 'DroneMotion_Setup-TEST'`);
    await command('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
    assert.ok(await evaluate('document.documentElement.scrollWidth <= window.innerWidth'), 'Mobile page must not overflow horizontally');
    // The initial scan runs automatically, without pressing the scan button.
    await until(`document.getElementById('ssid').options.length === 3`);
    assert.equal(await evaluate(`document.getElementById('ssid').tagName`), 'SELECT');
    assert.equal(await evaluate(`document.getElementById('ssid').options[1].value`), 'TestNetwork', 'Strongest network is first and duplicate SSIDs are merged');
    assert.equal(await evaluate(`document.getElementById('ssid').options[2].value`), unsafeSsid);
    assert.equal(await evaluate(`!!window.injected || !!document.querySelector('img')`), false, 'SSID is displayed as text, never HTML');
    await evaluate(`document.getElementById('ssid').value=${JSON.stringify(unsafeSsid)};document.getElementById('ssid').dispatchEvent(new Event('change'))`);
    assert.equal(await evaluate(`document.getElementById('password').disabled`), true, 'Open network needs no password');
    await evaluate(`document.getElementById('ssid').value='TestNetwork';document.getElementById('ssid').dispatchEvent(new Event('change'))`);
    assert.equal(await evaluate(`document.getElementById('mode').value`), 'personal');
    await evaluate(`document.getElementById('scan').click()`);
    await until(`document.getElementById('scan').disabled === false`);
    assert.equal(await evaluate(`document.getElementById('ssid').value`), 'TestNetwork', 'Rescan preserves selected network');
    await evaluate(`document.getElementById('password').focus()`);
    await command('Input.insertText', { text: 'short' });
    await evaluate(`document.getElementById('wifi-form').requestSubmit()`);
    await pause(150);
    assert.equal(connections, 0, 'Invalid short password must not be submitted');
    await evaluate(`document.getElementById('password').value='';document.getElementById('password').focus()`);
    await command('Input.insertText', { text: 'test-password' });
    await evaluate(`document.getElementById('wifi-form').requestSubmit()`);
    await until(`document.getElementById('endpoint').textContent === '192.168.1.105:3333'`);
    assert.equal(connections, 1);
    assert.equal(submitted.ssid, 'TestNetwork');
    assert.equal(submitted.password, 'test-password');
    assert.equal(await evaluate(`document.getElementById('password').value`), '', 'Password is cleared after submission');
    await evaluate(`window.confirm=()=>true;document.getElementById('forget').click()`);
    await until(`document.getElementById('result').hidden && document.getElementById('ssid').value === ''`);
    assert.equal(state, 'idle');
    await evaluate(`document.getElementById('manual').click();document.getElementById('hidden-ssid').value='HiddenNetwork';document.getElementById('password').value='hidden-password';document.getElementById('wifi-form').requestSubmit()`);
    await until(`document.getElementById('password').value === ''`);
    assert.equal(submitted.ssid, 'HiddenNetwork', 'Manual SSID is submitted instead of the disabled network selector');
    console.log('PASS: mobile layout, automatic scan, sorted/deduplicated selection, rescan, network security selection, SSID escaping, password validation, authenticated submission, IP display, forget and hidden network flows.');
  } finally {
    socket?.close();
    chrome.kill();
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
