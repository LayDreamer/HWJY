import assert from 'node:assert/strict'
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import vm from 'node:vm'
import test from 'node:test'
import { build, createServer } from 'vite'
import { runtimeConfigPlugin } from '../tools/runtime-config.mjs'

const original = 'window.__ENV_CONFIG__ = { VITE_API_URL: "http://retained.invalid" }\n'

async function fixture(context) {
  const root = await mkdtemp(path.join(tmpdir(), 'hwjy-runtime-config-'))
  context.after(() => {
    assert.equal(path.dirname(root), path.resolve(tmpdir()))
    assert.match(path.basename(root), /^hwjy-runtime-config-/)
    return rm(root, { recursive: true, force: true })
  })
  await mkdir(path.join(root, 'public'))
  await writeFile(path.join(root, 'public/env.config.js'), original)
  await writeFile(path.join(root, 'public/retained.txt'), 'retained')
  await writeFile(path.join(root, 'index.html'), '<script src="/env.config.js?v=fixture"></script><div id="app"></div>')
  return root
}

function readConfig(source) {
  const window = {}
  vm.runInNewContext(source, { window })
  return window.__ENV_CONFIG__
}

test('development serves its configured API URL without changing the public file', async (context) => {
  const root = await fixture(context)
  const apiUrl = 'http://localhost:18010/api?quoted="value"&line=\n'
  const server = await createServer({
    root, configFile: false, logLevel: 'silent', plugins: [runtimeConfigPlugin(apiUrl)],
    server: { host: '127.0.0.1', port: 0 },
  })
  try {
    await server.listen()
    const origin = `http://127.0.0.1:${server.httpServer.address().port}`
    const response = await fetch(origin + '/env.config.js?v=fixture')
    assert.equal(response.status, 200)
    assert.match(response.headers.get('content-type'), /javascript/)
    assert.equal(response.headers.get('cache-control'), 'no-store')
    assert.equal(readConfig(await response.text()).VITE_API_URL, apiUrl)
    const head = await fetch(origin + '/env.config.js', { method: 'HEAD' })
    assert.equal(head.status, 200)
    assert.equal(await head.text(), '')
    assert.equal(await (await fetch(origin + '/retained.txt')).text(), 'retained')
    assert.equal(await readFile(path.join(root, 'public/env.config.js'), 'utf8'), original)
  } finally { await server.close() }
})

test('production emits an editable config and preserves the public source across builds', async (context) => {
  const root = await fixture(context)
  for (const apiUrl of ['https://first.invalid/api', 'https://second.invalid/api']) {
    await build({
      root, configFile: false, logLevel: 'silent', plugins: [runtimeConfigPlugin(apiUrl)],
      build: { minify: false },
    })
    const output = path.join(root, 'dist/env.config.js')
    assert.equal(readConfig(await readFile(output, 'utf8')).VITE_API_URL, apiUrl)
    assert.match(await readFile(path.join(root, 'dist/index.html'), 'utf8'), /env\.config\.js\?v=fixture/)
    assert.equal(await readFile(path.join(root, 'dist/retained.txt'), 'utf8'), 'retained')
    assert.equal(await readFile(path.join(root, 'public/env.config.js'), 'utf8'), original)
    await writeFile(output, 'window.__ENV_CONFIG__ = { VITE_API_URL: "/deployed-api" }\n')
    assert.equal(readConfig(await readFile(output, 'utf8')).VITE_API_URL, '/deployed-api')
  }
})
