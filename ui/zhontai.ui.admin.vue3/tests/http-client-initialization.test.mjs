import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { basename, dirname, join, resolve } from 'node:path'
import test from 'node:test'
import ejs from 'ejs'
import ts from 'typescript'

const source = readFileSync(new URL('../src/api/admin/http-client.ts', import.meta.url), 'utf8')
const template = readFileSync(new URL('../gen/templates/http-client.ejs', import.meta.url), 'utf8')
const generated = ejs.render(template, { it: { config: { unwrapResponseData: true } } })

// Use real ES modules: the existing CommonJS fixtures cannot expose temporal-dead-zone
// failures caused by a generated API subclass being evaluated before HttpClient.
function createModuleGraph(t, clientSource) {
  const directory = mkdtempSync(join(tmpdir(), 'hwjy-http-initialization-'))
  t.after(() => {
    assert.equal(dirname(resolve(directory)), resolve(tmpdir()))
    assert.ok(basename(directory).startsWith('hwjy-http-initialization-'))
    rmSync(directory, { recursive: true, force: true })
  })
  const imports = {
    axios: import.meta.resolve('axios'),
    'lodash-es': import.meta.resolve('lodash-es'),
    'element-plus': './element-plus.mjs',
    './http-client': './http-client.mjs',
    '/@/api/admin/Auth': './Auth.mjs',
    '/@/stores/userInfo': './userInfo.mjs',
    '/@/utils/storage': './storage.mjs',
    '/@/stores/themeConfig': './theme.mjs',
    '/@/utils/watermark': './watermark.mjs',
    '/@/router': './router.mjs',
    '/@/globalProperties/modal': './modal.mjs',
    '/@/i18n': './i18n.mjs',
  }
  function writeProductionModule(name, content) {
    const javascript = ts.transpileModule(content, {
      compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
    }).outputText.replace(/(['"])([^'"\r\n]+)\1/g, (literal, _quote, specifier) =>
      imports[specifier] ? JSON.stringify(imports[specifier]) : literal)
    writeFileSync(join(directory, name + '.mjs'), javascript)
  }
  writeProductionModule('http-client', clientSource)
  for (const name of ['Auth', 'Role'])
    writeProductionModule(name, readFileSync(new URL(`../src/api/admin/${name}.ts`, import.meta.url), 'utf8'))
  writeProductionModule('userInfo', readFileSync(new URL('../src/stores/userInfo.ts', import.meta.url), 'utf8'))

  // Only UI, routing, storage and HTTP adapter boundaries are replaced. The client,
  // generated APIs, Pinia store and their import graph execute production code.
  const boundaries = {
    'element-plus': 'export const ElMessage={error(){},success(){}}; export const ElLoading={service:()=>({close(){}})};',
    storage: 'const values=new Map(); export const Local={get:key=>values.get(key),set:(key,value)=>values.set(key,value),remove:key=>values.delete(key)};',
    theme: 'export const useThemeConfig=()=>({themeConfig:{isWatermark:false}});',
    watermark: 'export default {set(){},del(){}};',
    router: 'export default {currentRoute:{value:{path:"/login"}},replace(){}};',
    modal: 'export default {closeLoading(){}};',
    i18n: 'export const t=value=>value; export const lang={value:"zh-cn"};',
  }
  for (const [name, content] of Object.entries(boundaries)) writeFileSync(join(directory, name + '.mjs'), content)
  return directory
}

for (const [name, clientSource] of [['current client', source], ['generated template', generated]]) {
  for (const entry of ['http-client', 'Auth', 'Role', 'userInfo']) {
    test(`${name}: ES module entry ${entry} initializes and refreshes successfully`, { timeout: 15000 }, (t) => {
      const directory = createModuleGraph(t, clientSource)
      const runner = `
        import assert from 'node:assert/strict';
        import axios from ${JSON.stringify(import.meta.resolve('axios'))};
        import {createPinia, defineStore, setActivePinia} from ${JSON.stringify(import.meta.resolve('pinia'))};
        globalThis.defineStore=defineStore;
        globalThis.window={__ENV_CONFIG__:{VITE_API_URL:'http://fixture.invalid'},navigator:{onLine:true}};
        setActivePinia(createPinia());
        await import('./${entry}.mjs');
        const {HttpClient}=await import('./http-client.mjs');
        const {AuthApi}=await import('./Auth.mjs');
        const {RoleApi}=await import('./Role.mjs');
        const {useUserInfo}=await import('./userInfo.mjs');
        assert.ok(new AuthApi() instanceof HttpClient);
        assert.ok(new RoleApi() instanceof HttpClient);
        const store=useUserInfo();
        store.setTokenInfo({accessToken:'old-token',refreshToken:'old-refresh',accessTokenExpiresAt:new Date(Date.now()+1000).toISOString(),accessTokenLifeTime:3600});
        let refreshCalls=0;
        const sent=[];
        axios.defaults.adapter=async config=>{
          sent.push({url:config.url,authorization:config.headers.Authorization});
          let data={success:true};
          if(config.url==='/api/admin/auth/refresh'){
            refreshCalls++;
            assert.equal(JSON.parse(config.data).refreshToken,'old-refresh');
            data={success:true,data:{accessToken:'new-token',refreshToken:'new-refresh',accessTokenExpiresAt:new Date(Date.now()+3600000).toISOString(),accessTokenLifeTime:3600}};
          }
          return {data,status:200,statusText:'OK',headers:{},config};
        };
        await new HttpClient().request({path:'/protected',method:'GET',showErrorMessage:false});
        assert.equal(refreshCalls,1);
        assert.equal(sent.find(item=>item.url==='/protected').authorization,'Bearer new-token');
        assert.equal(store.getTokenInfo().accessToken,'new-token');
      `
      writeFileSync(join(directory, 'run.mjs'), runner)
      const result = spawnSync(process.execPath, [join(directory, 'run.mjs')], { encoding: 'utf8', timeout: 12000 })
      assert.ifError(result.error)
      assert.equal(result.status, 0, result.stderr || result.stdout)
    })
  }
}
