import { chromium } from '@playwright/test';
import lighthouse from 'lighthouse';
import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const output = resolve(root, process.env.RIFFROSTER_BASELINE_OUTPUT || 'docs/baseline');
const origin = 'http://127.0.0.1:5189';
try {
  await fetch(origin + '/api/health');
  throw new Error('Port 5189 is occupied. Stop the other local test server first.');
} catch (error) { if (error.message.includes('occupied')) throw error; }
await mkdir(output, {recursive:true});
const server = spawn(process.execPath, ['tools/test-server.mjs'], {cwd:root, stdio:'ignore', windowsHide:true});
let browser;
try {
  let ready = false;
  for (let attempt = 0; attempt < 100; attempt++) {
    try { ready = (await fetch(origin + '/api/health')).ok; } catch { /* Starting isolated host. */ }
    if (ready) break;
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  if (!ready) throw new Error('Isolated test host did not start');
  const profile = await mkdtemp(join(tmpdir(), 'riffroster-lighthouse-'));
  browser = await chromium.launchPersistentContext(profile, {
    headless:true, viewport:{width:390,height:844}, timezoneId:'Europe/Prague',
    args:['--remote-debugging-port=9237']
  });
  const page = browser.pages()[0];
  await page.goto(origin);
  await page.locator('#login-name').fill('baseline-fixture');
  await page.locator('#display-name').fill('Baseline Fixture');
  await page.locator('#account-password').fill('fixture-password-only');
  await page.locator('#access-code').fill('friends-local');
  await page.locator('#login-form button[type="submit"]').click();
  await page.locator('#app').waitFor({state:'visible'});
  await page.locator('.pick-button').first().click();
  await page.waitForFunction(() => document.querySelector('.pick-button')?.getAttribute('aria-pressed') === 'true');
  for (const [width, height] of [[390,844],[768,1024],[1440,900]]) {
    await page.setViewportSize({width,height});
    await page.screenshot({path:join(output, `schedule-${width}x${height}.png`), fullPage:false});
  }
  const result = await lighthouse(origin, {
    port:9237, output:['json','html'], logLevel:'error', disableStorageReset:true,
    onlyCategories:['performance','accessibility','best-practices','seo']
  });
  if (!result || result.lhr.runtimeError) throw new Error(result?.lhr.runtimeError?.message || 'No Lighthouse result');
  await writeFile(join(output, 'lighthouse-mobile.json'), result.report[0]);
  await writeFile(join(output, 'lighthouse-mobile.html'), result.report[1]);
  const scores = Object.fromEntries(Object.entries(result.lhr.categories).map(([key,value]) => [key,Math.round(value.score * 100)]));
  await writeFile(join(output, 'scores.json'), JSON.stringify({
    capturedAt:new Date().toISOString(), lighthouseVersion:result.lhr.lighthouseVersion,
    browser:browser.browser()?.version(), scope:'Authenticated seeded schedule, synthetic account, local Development host; mobile simulated throttling; one measurement; no production latency.',
    scores, warnings:result.lhr.runWarnings,
    pwa:'Lighthouse PWA category is deprecated; installability and real-device offline/push need separate validation.'
  }, null, 2) + '\n');
  console.log(JSON.stringify(scores));
} finally {
  await browser?.close();
  if (process.platform === 'win32') {
    // Terminate only the owned test-server process tree; no unrelated app processes.
    const kill = spawn('taskkill', ['/PID', String(server.pid), '/T', '/F'], {stdio:'ignore',windowsHide:true});
    await new Promise(resolve => kill.on('exit', resolve));
  } else { server.kill('SIGTERM'); }
}
