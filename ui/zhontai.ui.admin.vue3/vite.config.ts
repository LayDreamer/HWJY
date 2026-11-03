import vue from '@vitejs/plugin-vue'
import { resolve } from 'path'
import { defineConfig, ConfigEnv, UserConfig } from 'vite'
import compression from 'vite-plugin-compression'
import vueSetupExtend from 'vite-plugin-vue-setup-extend'
import { loadEnv } from './src/utils/vite'
import { svgIconsPlugin } from './tools/svg-icons.mjs'
import { runtimeConfigPlugin } from './tools/runtime-config.mjs'
import AutoImport from 'unplugin-auto-import/vite'

const pathResolve = (dir: string): any => {
  return resolve(__dirname, '.', dir)
}

const alias: Record<string, string> = {
  '/@': pathResolve('./src/'),
  'vue-i18n': 'vue-i18n/dist/vue-i18n.cjs.js',
}

const viteConfig = defineConfig(({ mode, command }: ConfigEnv) => {
  const env = loadEnv(mode)

  return {
    plugins: [
      vue(),
      runtimeConfigPlugin(env.VITE_API_URL),
      vueSetupExtend(),
      compression({
        threshold: 5121,
        disable: !env.VITE_COMPRESSION,
        deleteOriginFile: false,
      }),
      svgIconsPlugin(pathResolve('src/assets/icons')),
      AutoImport({
        imports: ['vue', 'vue-router', 'pinia', 'vue-i18n'],
        vueTemplate: true,
        dts: './src/auto-imports.d.ts',
        exclude: ['**/node_modules/**', '**/dist/**', '**/src/auto-imports.d.ts'],
      }),
      {
        name: 'html-version',
        transformIndexHtml(html: string) {
          return html.replace(/<script src="(\/env\.config\.js)"><\/script>/, `<script src="$1?v=${process.env.npm_package_version}"></script>`)
        },
      },
    ],
    root: process.cwd(),
    resolve: { alias },
    base: command === 'serve' ? './' : env.VITE_PUBLIC_PATH,
    hmr: true,
    optimizeDeps: { exclude: ['vue-demi'] },
    server: {
      host: '0.0.0.0',
      port: env.VITE_PORT,
      open: env.VITE_OPEN,
      proxy: {
        '/gitee': {
          target: 'https://gitee.com',
          ws: true,
          changeOrigin: true,
          rewrite: (path: string) => path.replace(/^\/gitee/, ''),
        },
      },
    },
    build: {
      outDir: 'dist',
      emptyOutDir: true,
      chunkSizeWarningLimit: 1500,
      sourcemap: false,
      rollupOptions: {
        output: {
          chunkFileNames: 'assets/js/[name]-[hash].js',
          entryFileNames: 'assets/js/[name]-[hash].js',
          assetFileNames: 'assets/[ext]/[name]-[hash].[ext]',
          manualChunks(id: string) {
            if (id.includes('node_modules')) {
              return id.toString().match(/\/node_modules\/(?!.pnpm)(?<moduleName>[^\/]*)\//)?.groups!.moduleName ?? 'vender'
            }
          },
        },
      },
    },
    //https://sass-lang.com/documentation/breaking-changes/legacy-js-api
    css: { preprocessorOptions: { css: { charset: false }, scss: { api: 'modern' } } },
    define: {
      __VUE_I18N_LEGACY_API__: JSON.stringify(false),
      __VUE_I18N_FULL_INSTALL__: JSON.stringify(false),
      __INTLIFY_PROD_DEVTOOLS__: JSON.stringify(false),
      __NEXT_VERSION__: JSON.stringify(process.env.npm_package_version),
      __NEXT_NAME__: JSON.stringify(process.env.npm_package_name),
    },
  } as UserConfig
})

export default viteConfig
