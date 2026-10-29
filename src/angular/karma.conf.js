// Karma configuration for the Ninlil admin console. The workspace tests with Jasmine and Karma
// (see README.md for why), through the "@angular/build:karma" builder in angular.json.
// Chrome runs headless and unsandboxed so the tests work in CI and container builds.
// CHROME_BIN wins when set; otherwise the first browser at the usual Linux paths is used
// (Chromium first, since that is what the CI runners install).
// jasmine-core stays on 5.x in package.json: from 6.0 it warns on every run that Karma loads it
// as an ES module, which the Angular karma builder does and karma-jasmine 5.1 cannot change.
process.env.CHROME_BIN =
  process.env.CHROME_BIN ||
  ['/usr/bin/chromium', '/usr/bin/chromium-browser', '/usr/bin/google-chrome-stable'].find(
    (candidate) => require('fs').existsSync(candidate),
  ) ||
  '/usr/bin/google-chrome-stable';

module.exports = function (config) {
  config.set({
    basePath: '',
    // A port the operating system reports as free, on loopback only, rather than Karma's fixed
    // default of 9876 on every interface: the CI runners host their own services, and more than
    // one test run can share a runner.
    port: 0,
    listenAddress: '127.0.0.1',
    hostname: '127.0.0.1',
    // Only "jasmine" is listed here -- the newer, non-deprecated "@angular/build:karma" builder
    // (used over the deprecated "@angular-devkit/build-angular:karma" one) wires the
    // application bundle into Karma itself; a legacy
    // "@angular-devkit/build-angular" framework entry here is silently ignored by it as
    // incompatible, so it is deliberately left out rather than kept as dead configuration.
    frameworks: ['jasmine'],
    plugins: [
      require('karma-jasmine'),
      require('karma-chrome-launcher'),
      require('karma-jasmine-html-reporter'),
      require('karma-coverage'),
    ],
    client: {
      jasmine: {},
      clearContext: false,
    },
    jasmineHtmlReporter: {
      suppressAll: true,
    },
    coverageReporter: {
      dir: require('path').join(__dirname, './coverage/angular'),
      subdir: '.',
      reporters: [{ type: 'html' }, { type: 'text-summary' }],
    },
    reporters: ['progress', 'kjhtml'],
    browsers: ['ChromeHeadlessNoSandbox'],
    customLaunchers: {
      ChromeHeadlessNoSandbox: {
        base: 'ChromeHeadless',
        flags: ['--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage'],
      },
    },
    restartOnFileChange: true,
    singleRun: true,
  });
};
