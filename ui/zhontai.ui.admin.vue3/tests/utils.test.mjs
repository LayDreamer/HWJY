import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import vm from 'node:vm'
import ts from 'typescript'

function load(name) {
  const source = readFileSync(new URL(`../src/utils/${name}.ts`, import.meta.url), 'utf8')
  const output = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
  const exports = {}
  vm.runInNewContext(output, { exports, Object, Date, require: () => ({ t: (text, values) => text.replace('{s}', values.s) }) })
  return exports
}

test('relative dates keep the translator callable for strings and Date objects', () => {
  const { formatPast } = load('formatTime')
  assert.equal(formatPast(new Date(Date.now() - 120000)), '2分钟前')
  assert.equal(formatPast(new Date(Date.now() - 120000).toISOString()), '2分钟前')
})

test('enum descriptions are matched against values', () => {
  const { getDescByValue } = load('enum')
  const values = { enabled: { value: 1, desc: 'Enabled' }, disabled: { value: 0, desc: 'Disabled' } }
  assert.equal(getDescByValue(values, 0), 'Disabled')
  assert.equal(getDescByValue(values, 9), '')
})

test('object comparison supports null and recursively compares properties', () => {
  const { isObjectValueEqual } = load('arrayOperation')
  const input = { a: { route: { id: 1 } }, b: { route: { id: 1 } }, c: { route: 1 } }
  assert.equal(isObjectValueEqual(null, input.a), false)
  assert.equal(isObjectValueEqual(input.a, input.b), true)
  assert.equal(isObjectValueEqual(input.a, input.c), false)
})
