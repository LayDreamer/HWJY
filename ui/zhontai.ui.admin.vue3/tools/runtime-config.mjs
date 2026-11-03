// Keep the browser contract and deploy-time configuration without writing to public/.
export function runtimeConfigPlugin(apiUrl) {
  const source = `window.__ENV_CONFIG__ = ${JSON.stringify({ VITE_API_URL: apiUrl }, null, 2)}\n`
  return {
    name: 'admin-runtime-config',
    configureServer(server) {
      server.middlewares.use((request, response, next) => {
        if (request.url?.split('?')[0] !== '/env.config.js' || !['GET', 'HEAD'].includes(request.method)) {
          next()
          return
        }
        response.setHeader('Content-Type', 'application/javascript; charset=utf-8')
        response.setHeader('Cache-Control', 'no-store')
        response.end(request.method === 'HEAD' ? undefined : source)
      })
    },
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: 'env.config.js', source })
    },
  }
}
