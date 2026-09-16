import { RouteRecordRaw } from 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    id?: number
    title?: string
    isLink?: string
    isHide?: boolean
    isKeepAlive?: boolean
    isAffix?: boolean
    isIframe?: boolean
    isPublic?: boolean
    roles?: string[]
    icon?: string
  }
}

export const commonRoutes = [
  {
    path: '/personal',
    name: 'admin/personal',
    component: () => import('/@/views/admin/personal/index.vue'),
    meta: {
      title: '个人中心',
      isLink: '',
      isHide: true,
      isKeepAlive: true,
      isAffix: false,
      isIframe: false,
      roles: ['admin'],
      icon: 'ele-Message',
    },
  },
]

export const exampleRoutes: Array<RouteRecordRaw> = []

export const dynamicRoutes: Array<RouteRecordRaw> = [
  {
    path: '/',
    name: '/',
    component: () => import('/@/layout/index.vue'),
    meta: {
      isKeepAlive: true,
    },
    children: [],
  },
]

export const notFoundAndNoPower = [
  {
    path: '/:path(.*)*',
    name: 'notFound',
    component: () => import('/@/views/error/404.vue'),
    meta: {
      title: '找不到此页面',
      isHide: true,
    },
  },
  {
    path: '/401',
    name: 'noPower',
    component: () => import('/@/views/error/401.vue'),
    meta: {
      title: '没有权限',
      isHide: true,
    },
  },
]

export const staticRoutes: Array<RouteRecordRaw> = [
  {
    path: '/login',
    name: 'login',
    component: () => import('/@/views/admin/login/index.vue'),
    meta: {
      title: '登录',
      isPublic: true,
    },
  },
]
