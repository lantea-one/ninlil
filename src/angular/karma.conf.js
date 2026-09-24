// Karma configuration for Ninlil's Angular admin SPA. This project's own confirmed testing
// convention (Jasmine/Karma, not the CLI's newer vitest-based default) -- see this workspace's
// README.md for why. The one real environment wrinkle this file works around: Chrome must run
// headless and unsandboxed in CI and container builds.
process.env.CHROME_BIN = process.env.CHROME_BIN || '/usr/bin/google-chrome-stable';

module.exports = function (config) {
  config.set({
    basePath: '',
    // Only "jasmine" is listed here -- the newer, non-deprecated "@angular/build:karma" builder
    // (used over the deprecated "@angular-devkit/build-angular:karma" one, see this project's
    // own Phase 5a notes) wires the application bundle into Karma itself; a legacy
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
