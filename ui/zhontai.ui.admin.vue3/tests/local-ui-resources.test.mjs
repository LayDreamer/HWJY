import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import vm from 'node:vm'
import ts from 'typescript'

test('bundled icon fonts reference existing local WOFF2 files without CDN fallbacks', () => {
  for (const file of ['iconfont/iconfont.css', 'font-awesome/font-awesome.css']) {
    const cssUrl = new URL(`../public/fonts/${file}`, import.meta.url)
    const css = readFileSync(cssUrl, 'utf8')
    const fonts = [...css.matchAll(/url\(['"]?([^'"\)]+)['"]?\)/g)].map((match) => match[1])
    assert.ok(fonts.length > 0)
    for (const font of fonts) {
      assert.ok(font.startsWith('./') && font.endsWith('.woff2'), `Unexpected font URL: ${font}`)
      assert.equal(readFileSync(new URL(font, cssUrl)).subarray(0, 4).toString(), 'wOF2')
    }
  }
})

test('cached theme migrates legacy branding while preserving custom names and layout', () => {
  const source = readFileSync(new URL('../src/stores/themeConfig.ts', import.meta.url), 'utf8')
  const output = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
  const exports = {}
  vm.runInNewContext(output, { exports, defineStore: (_name, options) => options })
  const store = exports.useThemeConfig
  const state = store.state()
  const original = { ...state.themeConfig, globalTitle: '中台Admin', globalViceTitle: 'Zhontai Admin', watermarkText: '中台Admin', globalViceTitleMsg: '后台权限管理框架', layout: 'classic' }
  store.actions.setThemeConfig.call(state, { themeConfig: original })
  assert.equal(state.themeConfig.globalTitle, '后台管理系统')
  assert.equal(state.themeConfig.globalViceTitle, '后台管理系统')
  assert.equal(state.themeConfig.watermarkText, '后台管理系统')
  assert.equal(state.themeConfig.globalViceTitleMsg, '账号与权限管理')
  assert.equal(state.themeConfig.layout, 'classic')
  assert.equal(original.globalTitle, '中台Admin')
  const custom = { ...state.themeConfig, globalTitle: '客户系统', globalViceTitle: '自定义副标题', watermarkText: '自定义水印', globalViceTitleMsg: '自定义说明' }
  store.actions.setThemeConfig.call(state, { themeConfig: custom })
  assert.equal(JSON.stringify(state.themeConfig), JSON.stringify(custom))
})
