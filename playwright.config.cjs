const { defineConfig } = require('@playwright/test');
module.exports = defineConfig({
  testDir: './tests/e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 45000,
  reporter: [['list'], ['html', {open:'never'}]],
  use: {baseURL:'http://127.0.0.1:5189', viewport:{width:390, height:844}, timezoneId:'Europe/Prague', trace:'retain-on-failure'},
  webServer: {command:'node tools/test-server.mjs', url:'http://127.0.0.1:5189/api/health', reuseExistingServer:false, timeout:60000},
  projects: [{name:'chromium', use:{browserName:'chromium'}}]
});
