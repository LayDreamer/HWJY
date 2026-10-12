import type { Plugin } from 'vite'
export function compileIcon(source: string, symbolId: string): string
export function buildSprite(directory: string): string
export function svgIconsPlugin(directory: string): Plugin
