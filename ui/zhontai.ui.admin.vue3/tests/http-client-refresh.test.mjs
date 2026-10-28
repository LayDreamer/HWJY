import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import vm from 'node:vm'
import axios from 'axios'
import ejs from 'ejs'
import ts from 'typescript'

const source = readFileSync(new URL('../src/api/admin/http-client.ts', import.meta.url), 'utf8')
const template = readFileSync(new URL('../gen/templates/http-client.ejs', import.meta.url), 'utf8')
const generated = ejs.render(template, { it: { config: { unwrapResponseData: true } } })

function deferred() {
  let resolve, reject
  const promise = new Promise((ok, fail) => {
    resolve = ok
    reject = fail
  })
  return { promise, resolve, reject }
}

function token(accessToken = 'old-token', expiresIn = 1000) {
  return { accessToken, refreshToken: accessToken + '-refresh', accessTokenExpiresAt: new Date(Date.now() + expiresIn).toISOString(), accessTokenLifeTime: 3600 }
}

// Run the actual client with Axios's adapter boundary; no browser, server or real credentials are used.
function fixture(clientSource) {
  const state = {
    tokenInfo: token(),
    refreshCalls: 0,
    clearCalls: 0,
    loadingClosed: 0,
    sent: [],
    refresh: () => Promise.reject(new Error('Refresh response not configured')),
  }
  const store = {
    getTokenInfo: () => state.tokenInfo,
    getToken: () => state.tokenInfo?.accessToken,
    setTokenInfo: (value) => { state.tokenInfo = value },
    clear: () => { state.clearCalls++; state.tokenInfo = undefined },
  }
  const dependencies = {
    axios,
    'element-plus': {
      ElMessage: { error() {}, success() {} },
      ElLoading: { service: () => ({ close: () => { state.loadingClosed++ } }) },
    },
    '/@/api/admin/Auth': {
      AuthApi: class {
        refresh(input, options) {
          state.refreshCalls++
          assert.equal(options.login, false)
          assert.equal(input.refreshToken, state.tokenInfo.refreshToken)
          assert.equal(input.token, undefined)
          return state.refresh(input)
        }
      },
    },
    '/@/stores/userInfo': { useUserInfo: () => store },
    '/@/i18n': { t: (value) => value, lang: { value: 'zh-cn' } },
  }
  const compiled = ts.transpileModule(clientSource, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, esModuleInterop: true },
  }).outputText
  const exports = {}
  vm.runInNewContext(compiled, {
    exports,
    require: (name) => {
      assert.ok(name in dependencies, `Unexpected dependency: ${name}`)
      return dependencies[name]
    },
    window: { __ENV_CONFIG__: { VITE_API_URL: '/fixture' }, navigator: { onLine: true } },
    console, Date, Map, Promise, FormData, Blob, File,
  })
  state.client = () => new exports.HttpClient({
    adapter: async (config) => {
      state.sent.push({ url: config.url, authorization: config.headers.Authorization })
      return { data: { success: true }, status: 200, statusText: 'OK', headers: {}, config }
    },
  })
  return state
}

function request(client, path, options = {}) {
  return client.request({ path, method: 'GET', showErrorMessage: false, ...options })
}

const tick = () => new Promise((resolve) => setImmediate(resolve))

for (const [name, clientSource] of [['current client', source], ['generated template', generated]]) {
  test(`${name}: concurrent requests share refresh and use the new token`, { timeout: 2000 }, async () => {
    const state = fixture(clientSource)
    const refresh = deferred()
    state.refresh = () => refresh.promise
    const first = request(state.client(), '/first')
    const second = request(state.client(), '/second')
    await tick()
    assert.equal(state.refreshCalls, 1)
    assert.equal(state.sent.length, 0)
    refresh.resolve({ success: true, data: token('new-token', 3600000) })
    await Promise.all([first, second])
    assert.equal(state.sent.length, 2)
    assert.ok(state.sent.every((item) => item.authorization === 'Bearer new-token'))
  })

  test(`${name}: network failure rejects every waiter and releases loading`, { timeout: 2000 }, async () => {
    const state = fixture(clientSource)
    const refresh = deferred()
    state.refresh = () => refresh.promise
    const results = Promise.allSettled([
      request(state.client(), '/first', { loading: true }),
      request(state.client(), '/second', { loading: true }),
    ])
    await tick()
    refresh.reject(new Error('Controlled network failure'))
    const settled = await results
    assert.ok(settled.every((item) => item.status === 'rejected'))
    assert.equal(state.refreshCalls, 1)
    assert.equal(state.clearCalls, 1)
    assert.equal(state.loadingClosed, 1)
    assert.equal(state.sent.length, 0)
  })

  for (const response of [{ success: false }, { success: true, data: {} }]) {
    test(`${name}: invalid refresh response ${JSON.stringify(response)} rejects waiters`, { timeout: 2000 }, async () => {
      const state = fixture(clientSource)
      state.refresh = async () => response
      const settled = await Promise.allSettled([request(state.client(), '/first'), request(state.client(), '/second')])
      assert.ok(settled.every((item) => item.status === 'rejected'))
      assert.equal(state.sent.length, 0)
      assert.equal(state.clearCalls, 1)
    })
  }

  test(`${name}: synchronous refresh failure rejects and permits a later retry`, { timeout: 2000 }, async () => {
    const state = fixture(clientSource)
    const client = state.client()
    state.refresh = () => { throw new Error('Controlled synchronous failure') }
    await assert.rejects(request(client, '/first'))
    state.tokenInfo = token('next-session')
    state.refresh = async () => ({ success: true, data: token('next-token', 3600000) })
    await request(client, '/retry')
    assert.equal(state.refreshCalls, 2)
    assert.equal(state.sent.at(-1).authorization, 'Bearer next-token')
  })

  test(`${name}: a late refresh cannot restore a logged-out session`, { timeout: 2000 }, async () => {
    const state = fixture(clientSource)
    const refresh = deferred()
    state.refresh = () => refresh.promise
    const result = assert.rejects(request(state.client(), '/first'))
    await tick()
    state.tokenInfo = undefined
    refresh.resolve({ success: true, data: token('late-token', 3600000) })
    await result
    assert.equal(state.tokenInfo, undefined)
    assert.equal(state.clearCalls, 0)
    assert.equal(state.sent.length, 0)
  })

  test(`${name}: failure from an old session does not clear a new login`, { timeout: 2000 }, async () => {
    const state = fixture(clientSource)
    const refresh = deferred()
    state.refresh = () => refresh.promise
    const result = assert.rejects(request(state.client(), '/first'))
    await tick()
    state.tokenInfo = token('new-session', 3600000)
    refresh.reject(new Error('Old session refresh failure'))
    await result
    assert.equal(state.tokenInfo.accessToken, 'new-session')
    assert.equal(state.clearCalls, 0)
  })

  test(`${name}: an old refresh cannot release a newer session's refresh`, { timeout: 2000 }, async () => {
    const state = fixture(clientSource)
    const oldRefresh = deferred()
    const newRefresh = deferred()
    state.refresh = ({ refreshToken: value }) => value === 'old-token-refresh' ? oldRefresh.promise : newRefresh.promise
    const oldResult = assert.rejects(request(state.client(), '/old-session'))
    await tick()
    state.tokenInfo = token('new-session')
    const next = request(state.client(), '/new-session')
    await tick()
    oldRefresh.resolve({ success: true, data: token('stale-token', 3600000) })
    await oldResult
    const another = request(state.client(), '/another')
    await tick()
    assert.equal(state.refreshCalls, 2)
    newRefresh.resolve({ success: true, data: token('current-token', 3600000) })
    await Promise.all([next, another])
    assert.equal(state.tokenInfo.accessToken, 'current-token')
    assert.ok(state.sent.every((item) => item.authorization === 'Bearer current-token'))
  })

  test(`${name}: public requests do not refresh or attach login credentials`, { timeout: 2000 }, async () => {
    const state = fixture(clientSource)
    await request(state.client(), '/public', { login: false })
    assert.equal(state.refreshCalls, 0)
    assert.equal(state.sent[0].authorization, undefined)
  })

  test(`${name}: reused clients release interceptors before a public request`, { timeout: 2000 }, async () => {
    const state = fixture(clientSource)
    const client = state.client()
    state.tokenInfo = token('valid-token', 3600000)
    await request(client, '/authenticated')
    state.tokenInfo = token()
    await request(client, '/public', { login: false })
    assert.equal(state.refreshCalls, 0)
    assert.equal(state.sent[1].authorization, undefined)
  })
}
