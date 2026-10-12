import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { readdirSync } from 'node:fs'
import test from 'node:test'
import { generateApi } from 'swagger-typescript-api'
import { compileIcon, buildSprite } from '../tools/svg-icons.mjs'

test('SVG sprite preserves icon names, viewBox and isolated paint references', () => {
  const svg = '<svg viewBox="0 0 20 20"><defs><linearGradient id="paint"><stop stop-color="red"/></linearGradient></defs><path fill="url(#paint)" d="M0 0h20v20H0z"/></svg>'
  const first = compileIcon(svg, 'icon-one')
  const second = compileIcon(svg, 'icon-two')
  assert.match(first, /<symbol[^>]+id="icon-one"/)
  assert.match(first, /viewBox="0 0 20 20"/)
  assert.match(first, /url\(#icon-one__paint\)/)
  assert.match(second, /url\(#icon-two__paint\)/)
  const directory = new URL('../src/assets/icons', import.meta.url)
  const sprite = buildSprite(fileURLToPath(directory))
  const expectedCount = readdirSync(directory, { recursive: true }).filter(name => name.endsWith('.svg')).length
  assert.equal((sprite.match(/<symbol\b/g) || []).length, expectedCount)
  assert.match(sprite, /id="icon-export"/)
  assert.match(sprite, /id="icon-hiprint-image"/)
})

test('hiprint resolves patched jsPDF and its PDF image/page APIs remain compatible', () => {
  const requireFromHiprint = createRequire(import.meta.resolve('vue-plugin-hiprint'))
  const { jsPDF } = requireFromHiprint('jspdf')
  assert.equal(jsPDF.version, '4.2.1')
  const pdf = new jsPDF({ orientation: 'portrait', unit: 'pt', format: 'a4' })
  const png = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACklEQVR4nGMAAQAABQABDQottAAAAABJRU5ErkJggg=='
  pdf.addImage(png, 'PNG', 0, 0, 20, 20)
  pdf.addPage()
  pdf.text('Print compatibility', 10, 10)
  assert.equal(pdf.getNumberOfPages(), 2)
  const bytes = new Uint8Array(pdf.output('arraybuffer'))
  assert.equal(new TextDecoder().decode(bytes.subarray(0, 5)), '%PDF-')
  assert(pdf.output('blob').size > 100)
})

test('patched API generator renders the existing refresh template', async () => {
  const result = await generateApi({
    spec: { openapi: '3.0.0', info: { title: 'Compatibility', version: '1' }, paths: {} },
    templates: fileURLToPath(new URL('../gen/templates', import.meta.url)),
    httpClientType: 'axios', modular: true, unwrapResponseData: true,
  })
  const client = result.files.find(file => file.fileName === 'http-client')?.fileContent
  assert.match(client, /export class HttpClient/)
  assert.match(client, /let tokenRefresh/)
  assert.match(client, /interceptors\.request\.eject/)
})
