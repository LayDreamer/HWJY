import js from '@eslint/js'
import globals from 'globals'
import vue from 'eslint-plugin-vue'
import vueParser from 'vue-eslint-parser'
import tsParser from '@typescript-eslint/parser'
import tsPlugin from '@typescript-eslint/eslint-plugin'

export default [
  {
    ignores: ['node_modules/**', 'dist/**', 'public/**', 'gen/**', 'src/assets/**', 'src/auto-imports.d.ts',
      'src/api/admin/*.ts', '!src/api/admin/http-client.ts', 'src/api/admin.extend/Api.ts'],
  },
  js.configs.recommended,
  ...vue.configs['flat/essential'],
  {
    files: ['**/*.{js,mjs,ts,tsx,vue}'],
    languageOptions: { globals: { ...globals.browser, ...globals.node } },
    rules: {
      'no-console': ['error', { allow: ['warn', 'error'] }],
      'no-useless-escape': 'off',
      'no-prototype-builtins': 'off',
      'no-case-declarations': 'off',
      'vue/multi-word-component-names': 'off',
    },
  },
  {
    files: ['**/*.{ts,tsx,vue}'],
    languageOptions: { parser: tsParser },
    plugins: { '@typescript-eslint': tsPlugin },
    rules: {
      'no-undef': 'off',
      'no-redeclare': 'off',
      'no-unused-vars': 'off',
      '@typescript-eslint/no-redeclare': 'error',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
    },
  },
  { files: ['**/*.vue'], languageOptions: { parser: vueParser, parserOptions: { parser: tsParser } } },
]
