import Cookies from 'js-cookie'

function parseStorageValue(json: string | null) {
  if (!json || json === 'undefined') return null

  try {
    return JSON.parse(json)
  } catch {
    return null
  }
}

/**
 * window.localStorage 浏览器永久缓存
 * @method set 设置永久缓存
 * @method get 获取永久缓存
 * @method remove 移除永久缓存
 * @method clear 移除全部永久缓存
 */
export const Local = {
  getKey(key: string) {
    // @ts-ignore
    return `${__NEXT_NAME__}:${key}`
  },
  // 设置永久缓存
  set(key: string, val: any) {
    window.localStorage.setItem(Local.getKey(key), JSON.stringify(val))
  },
  // 获取永久缓存
  get(key: string) {
    return parseStorageValue(window.localStorage.getItem(Local.getKey(key)))
  },
  // 移除永久缓存
  remove(key: string) {
    window.localStorage.removeItem(Local.getKey(key))
  },
  // 移除全部永久缓存
  clear() {
    window.localStorage.clear()
  },
}

/**
 * window.sessionStorage 浏览器临时缓存
 * @method set 设置临时缓存
 * @method get 获取临时缓存
 * @method remove 移除临时缓存
 * @method clear 移除全部临时缓存
 */
export const Session = {
  // 设置临时缓存
  set(key: string, val: any) {
    if (key === 'token') return Cookies.set(key, val)
    window.sessionStorage.setItem(Local.getKey(key), JSON.stringify(val))
  },
  // 获取临时缓存
  get(key: string) {
    if (key === 'token') return Cookies.get(key)
    return parseStorageValue(window.sessionStorage.getItem(Local.getKey(key)))
  },
  // 移除临时缓存
  remove(key: string) {
    if (key === 'token') return Cookies.remove(key)
    window.sessionStorage.removeItem(Local.getKey(key))
  },
  // 移除全部临时缓存
  clear() {
    Cookies.remove('token')
    window.sessionStorage.clear()
  },
}
