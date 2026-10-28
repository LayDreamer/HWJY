import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import vm from 'node:vm'
import axios from 'axios'
import { debounce, merge } from 'lodash-es'
import ts from 'typescript'

const tick = () => new Promise((resolve) => setImmediate(resolve))
const token = (name) => ({
  accessToken: name,
  refreshToken: `${name}-refresh`,
  accessTokenExpiresAt: new Date(Date.now() + 1000).toISOString(),
  accessTokenLifeTime: 3600,
})

function deferred() {
  let resolve, reject
  const promise = new Promise((ok, fail) => { resolve = ok; reject = fail })
  return { promise, resolve, reject }
}

// Execute production modules; replace only browser/device storage and network boundaries.
function load(path, dependencies, globals = {}) {
  const source = readFileSync(new URL(path, import.meta.url), 'utf8')
  const compiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, esModuleInterop: true },
  }).outputText
  const exports = {}
  vm.runInNewContext(compiled, {
    exports, console, Date, Map, Promise, FormData, Blob, File,
    require(name) {
      assert.ok(name in dependencies, `Unexpected dependency: ${name}`)
      return dependencies[name]
    },
    ...globals,
  })
  return exports
}

function vueFixture(t) {
  const storage = new Map([['admin-token-info', token('original')]])
  const state = { storage, sent: [], logoutInputs: [], refresh: deferred(), logout: deferred() }
  const auth = {
    AuthApi: class {
      refresh() { return state.refresh.promise }
      logout(input, options) {
        assert.equal(options.login, false)
        state.logoutInputs.push(input)
        return state.logout.promise
      }
    },
  }
  const { useUserInfo } = load('../src/stores/userInfo.ts', {
    '/@/api/admin/Auth': auth,
    'lodash-es': { debounce, merge },
    '/@/utils/storage': { Local: { get: (key) => storage.get(key), set: (key, value) => storage.set(key, value), remove: (key) => storage.delete(key) } },
    '/@/stores/themeConfig': {},
    '/@/utils/watermark': {},
    '/@/router': { currentRoute: { value: { path: '/login' } } },
    '/@/globalProperties/modal': { closeLoading() {} },
  }, {
    defineStore: (_id, options) => () => {
      const store = options.state()
      for (const [name, action] of Object.entries(options.actions)) store[name] = action.bind(store)
      t.after(() => options.actions.clear.cancel())
      return store
    },
  })
  state.store = useUserInfo()
  const { HttpClient } = load('../src/api/admin/http-client.ts', {
    axios,
    '/@/api/admin/Auth': auth,
    '/@/stores/userInfo': { useUserInfo: () => state.store },
    '/@/i18n': { t: (value) => value, lang: { value: 'zh-cn' } },
    'element-plus': { ElMessage: { error() {}, success() {} }, ElLoading: { service: () => ({ close() {} }) } },
  }, { window: { __ENV_CONFIG__: { VITE_API_URL: '/fixture' }, navigator: { onLine: true } } })
  state.client = new HttpClient({ adapter: async (config) => {
    state.sent.push(config)
    return { data: { success: true }, status: 200, statusText: 'OK', headers: {}, config }
  } })
  return state
}

test('Vue3: logout clears credentials before its response and rejects an in-flight refresh', async (t) => {
  const state = vueFixture(t)
  const request = state.client.request({ path: '/protected', method: 'GET', showErrorMessage: false })
  await tick()
  const logout = state.store.logout()
  const clearedImmediately = !state.store.getTokenInfo()
  state.refresh.resolve({ success: true, data: token('late') })
  state.logout.resolve({ success: true })
  const [result] = await Promise.allSettled([request, logout])
  assert.equal(clearedImmediately, true)
  assert.equal(result.status, 'rejected')
  assert.equal(state.store.getTokenInfo(), undefined)
  assert.equal(state.logoutInputs[0].refreshToken, 'original-refresh')
  assert.equal(state.sent.length, 0)
})

for (const failed of [false, true]) {
  test(`Vue3: delayed logout ${failed ? 'failure' : 'success'} preserves a new login`, async (t) => {
    const state = vueFixture(t)
    const logout = state.store.logout()
    const result = Promise.allSettled([logout])
    const clearedImmediately = !state.store.getTokenInfo()
    state.store.setTokenInfo(token('new-login'))
    if (failed) state.logout.reject(new Error('Controlled logout failure'))
    else state.logout.resolve({ success: true })
    await result
    assert.equal(clearedImmediately, true)
    assert.equal(state.store.getTokenInfo().accessToken, 'new-login')
  })
}

test('Vue3: a second logout clears credentials even inside the clear debounce interval', async (t) => {
  const state = vueFixture(t)
  state.logout.resolve({ success: true })
  await state.store.logout()
  state.store.setTokenInfo(token('second-login'))
  await state.store.logout()
  assert.equal(state.store.getTokenInfo(), undefined)
  assert.equal(state.store.userInfos.token, '')
})

function uniFixture() {
  const storage = new Map([['app_token', token('original')]])
  const state = { storage, calls: [], toasts: [], pending: [] }
  const uni = {
    getStorageSync: (key) => storage.get(key),
    setStorageSync: (key, value) => storage.set(key, value),
    removeStorageSync: (key) => storage.delete(key),
    $u: { deepMerge: (defaults, options) => ({ ...defaults, ...options }), page: () => '/fixture' },
    navigateTo() { throw new Error('Unexpected navigation') },
  }
  const store = { state: {} }
  const { api } = load('../../zhontai.ui.admin.uniapp.vue2/api/api-account.js', {
    store,
    '../libs/config/http-api.js': { post(path, input, config) {
      state.calls.push({ path, input, config })
      const next = state.pending.shift()
      assert.ok(next, 'Network response must be configured')
      if (next instanceof Error) throw next
      return next.promise
    } },
    '../libs/utils/util.js': { showToast: (...args) => state.toasts.push(args) },
  }, { uni })
  const module = load('../../zhontai.ui.admin.uniapp.vue2/store/modules/account.js', {
    '@/api/api-account.js': { api },
  }, { uni }).default
  store.state.account = module.state
  store.commit = (name, data) => module.mutations[name.replace('account/', '')](module.state, data)
  state.api = api
  state.store = store
  state.logout = () => module.actions.logout({ state: module.state, commit: store.commit })
  state.login = (name) => store.commit('setAuth', token(name))
  const configHttp = load('../../zhontai.ui.admin.uniapp.vue2/libs/config/config-http.js', {
    store,
    '@/api/api-account.js': { api },
    '../../libs/utils/util.js': { showToast: (...args) => state.toasts.push(args) },
  }, { uni })
  state.retryCalls = 0
  load('../../zhontai.ui.admin.uniapp.vue2/libs/config/http-api.js', {
    store,
    'uview-ui/libs/luch-request': class {
      interceptors = { request: { use() {} }, response: { use(handler) { state.respond = handler } } }
      setConfig(configure) { configure({}) }
      middleware() { state.retryCalls++; return Promise.reject(new Error('Unexpected retry')) }
    },
    './config.js': { apiUrl: '/fixture' },
    './config-http.js': configHttp,
    '@/api/api-account.js': { api },
    '../../libs/utils/util.js': { showToast: (...args) => state.toasts.push(args) },
  }, { uni })
  return state
}

test('UniApp: concurrent refreshes share one request and persist rotated credentials', async () => {
  const state = uniFixture()
  const refresh = deferred()
  state.pending.push(refresh)
  const first = state.api.refreshToken({ refreshToken: 'original-refresh' })
  const second = state.api.refreshToken({ refreshToken: 'original-refresh' })
  await tick()
  assert.equal(state.calls.length, 1)
  refresh.resolve({ success: true, data: token('rotated') })
  const results = await Promise.all([first, second])
  assert.ok(results.every((value) => value.accessToken === 'rotated'))
  assert.equal(state.storage.get('app_token').refreshToken, 'rotated-refresh')
})

test('UniApp: logout invalidates a pending refresh before the logout response', async () => {
  const state = uniFixture()
  const refresh = deferred(), logoutResponse = deferred()
  state.pending.push(refresh, logoutResponse)
  const refreshResult = Promise.allSettled([state.api.refreshToken({ refreshToken: 'original-refresh' })])
  await tick()
  const logout = state.logout()
  const clearedImmediately = state.store.state.account.token === ''
  refresh.resolve({ success: true, data: token('late') })
  logoutResponse.resolve({ success: true })
  const [result] = await refreshResult
  await logout
  assert.equal(clearedImmediately, true)
  assert.equal(result.status, 'rejected')
  assert.equal(state.store.state.account.token, '')
  assert.equal(state.storage.has('app_token'), false)
  assert.equal(state.calls[1].input.refreshToken, 'original-refresh')
  assert.equal(state.calls[1].config.custom.auth, false)
})

for (const failed of [false, true]) {
  test(`UniApp: delayed logout ${failed ? 'failure' : 'success'} preserves a new login`, async () => {
    const state = uniFixture()
    const response = deferred()
    state.pending.push(response)
    const logout = state.logout()
    const result = Promise.allSettled([logout])
    state.login('new-login')
    if (failed) response.reject(new Error('Controlled logout failure'))
    else response.resolve({ success: true })
    await result
    assert.equal(state.store.state.account.token, 'new-login')
    assert.equal(state.storage.get('app_token').accessToken, 'new-login')
  })
}

for (const oldResult of ['success', 'invalid', 'network failure']) {
  test(`UniApp: old refresh ${oldResult} cannot overwrite or release a new session refresh`, async () => {
    const state = uniFixture()
    const old = deferred(), current = deferred()
    state.pending.push(old, current)
    const oldRequest = Promise.allSettled([state.api.refreshToken({ refreshToken: 'original-refresh' })])
    await tick()
    state.login('new-login')
    const next = state.api.refreshToken({ refreshToken: 'new-login-refresh' })
    await tick()
    if (oldResult === 'network failure') old.reject(new Error('Controlled refresh failure'))
    else old.resolve(oldResult === 'success' ? { success: true, data: token('stale') } : { success: false })
    const [result] = await oldRequest
    const another = state.api.refreshToken({ refreshToken: 'new-login-refresh' })
    await tick()
    current.resolve({ success: true, data: token('current') })
    const results = await Promise.all([next, another])
    assert.equal(result.status, 'rejected')
    assert.equal(state.calls.length, 2)
    assert.ok(results.every((value) => value.accessToken === 'current'))
    assert.equal(state.store.state.account.token, 'current')
    assert.equal(state.toasts.length, 0)
  })
}

for (const response of [{ success: false }, { success: true, data: {} }, new Error('Synchronous network failure')]) {
  test(`UniApp: invalid or failed refresh settles every waiter and permits retry (${response.message || JSON.stringify(response)})`, async () => {
    const state = uniFixture()
    const first = deferred(), retry = deferred()
    state.pending.push(response instanceof Error ? response : first, retry)
    const results = Promise.allSettled([
      state.api.refreshToken({ refreshToken: 'original-refresh' }),
      state.api.refreshToken({ refreshToken: 'original-refresh' }),
    ])
    first.resolve(response)
    assert.ok((await results).every((value) => value.status === 'rejected'))
    const next = state.api.refreshToken({ refreshToken: 'original-refresh' })
    retry.resolve({ success: true, data: token('retry') })
    await next
    assert.equal(state.calls.length, 2)
    assert.equal(state.store.state.account.token, 'retry')
  })
}

for (const catchError of [false, true]) {
  test(`UniApp: a public authentication 401 cannot enter the refresh retry loop (catch=${catchError})`, async () => {
    const state = uniFixture()
    const result = state.respond({
      statusCode: 401,
      data: { success: false },
      config: { header: {}, custom: { auth: false, catch: catchError } },
    })
    if (catchError) await assert.rejects(result)
    else assert.equal((await result).success, false)
    assert.equal(state.retryCalls, 0)
    assert.equal(state.calls.length, 0)
    assert.equal(state.store.state.account.token, 'original')
  })
}

test('UniApp: a delayed expired-session notification cannot redirect a new login', async () => {
  const state = uniFixture()
  const response = deferred()
  state.pending.push(response)
  const result = assert.rejects(state.api.refreshToken({ refreshToken: 'original-refresh' }))
  response.resolve({ success: false })
  await result
  assert.equal(state.toasts.length, 1)
  state.login('new-login')
  state.toasts[0][1]()
  assert.equal(state.store.state.account.token, 'new-login')
})
