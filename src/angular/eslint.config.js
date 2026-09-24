// ESLint (flat config) for Ninlil's Angular admin SPA -- the direct TypeScript-side analogue
// of the backend's own `dotnet csharpier check .` gate, now paired with Prettier for formatting
// (see .prettierrc) and this file for everything Prettier itself does not check (unused
// imports, Angular-specific correctness rules, template accessibility rules, this app's own
// naming conventions). See this workspace's README.md for the
// conventions these enforce.
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');
const prettierConfig = require('eslint-config-prettier');

module.exports = tseslint.config(
  {
    // Generated code -- the OpenAPI-generator-produced API client, and every build/coverage
    // output directory -- is never hand-edited and never linted; regenerating it (`npm run
    // generate:api`) always overwrites whatever is here, so a lint finding against it would be
    // both unfixable in place and immediately reintroduced by the next codegen run.
    ignores: ['dist/**', 'coverage/**', 'src/app/api-client/**', '.angular/**'],
  },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...angular.configs.tsRecommended,
      prettierConfig,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      // Kebab-case selectors with the "app" prefix, matching this workspace's own confirmed
      // component-naming convention (Angular's own default recommendation for an application,
      // as opposed to a published component library).
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'app', style: 'kebab-case' },
      ],
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'app', style: 'camelCase' },
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
    languageOptions: { parser: angular.templateParser },
  },
);
