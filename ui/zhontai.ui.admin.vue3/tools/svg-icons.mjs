import { readdirSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { optimize } from 'svgo'

const virtualId = 'virtual:svg-icons-register'
const resolvedId = '\0' + virtualId

export function compileIcon(source, symbolId) {
  return optimize(source, {
    plugins: [
      { name: 'preset-default', params: { overrides: { cleanupIds: false } } },
      { name: 'prefixIds', params: { prefix: symbolId } },
      {
        name: 'icon-symbol',
        fn: () => ({ element: { enter(node, parent) {
          if (parent.type !== 'root' || node.name !== 'svg') return
          node.name = 'symbol'
          node.attributes.id = symbolId
          delete node.attributes.width
          delete node.attributes.height
          delete node.attributes.xmlns
        } } }),
      },
    ],
  }).data
}

export function buildSprite(directory) {
  const symbols = []
  function visit(current) {
    for (const entry of readdirSync(current, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      const file = path.join(current, entry.name)
      if (entry.isDirectory()) visit(file)
      else if (entry.name.endsWith('.svg')) {
        const name = path.relative(directory, file).replace(/\\/g, '/').replace(/\.svg$/, '').replaceAll('/', '-')
        symbols.push(compileIcon(readFileSync(file, 'utf8'), 'icon-' + name))
      }
    }
  }
  visit(directory)
  return '<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink">' + symbols.join('') + '</svg>'
}

export function svgIconsPlugin(directory) {
  return {
    name: 'admin-svg-icons',
    resolveId(id) { if (id === virtualId) return resolvedId },
    load(id) {
      if (id !== resolvedId) return
      return `const install = () => {
        if (document.getElementById('__svg__icons__dom__')) return;
        const container = document.createElement('div');
        container.id = '__svg__icons__dom__';
        container.style.cssText = 'position:absolute;width:0;height:0;overflow:hidden';
        container.setAttribute('aria-hidden', 'true');
        container.innerHTML = ${JSON.stringify(buildSprite(directory))};
        document.body.appendChild(container);
      };
      if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', install, { once: true });
      else install();`
    },
    handleHotUpdate(context) {
      if (!context.file.startsWith(directory + path.sep) || !context.file.endsWith('.svg')) return
      const module = context.server.moduleGraph.getModuleById(resolvedId)
      if (module) context.server.moduleGraph.invalidateModule(module)
      context.server.ws.send({ type: 'full-reload' })
      return []
    },
  }
}
