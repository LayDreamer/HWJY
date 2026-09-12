import { ContentType, HttpClient, RequestParams } from '/@/api/admin/http-client'

export interface MaterialInput {
  id?: number
  materialCode: string
  materialName: string
  specification?: string | null
  materialType: string
  categoryId?: number | null
  unit?: string | null
  status: string
  isPurchase: boolean
  isProduce: boolean
  leadTime: number
  safetyStock: number
  remark?: string | null
}

export interface MaterialDto extends MaterialInput {
  id: number
  tenantId?: number | null
}

export interface PageInput<T> {
  currentPage: number
  pageSize: number
  filter?: T
}

export interface MaterialQueryInput {
  materialCode?: string
  materialName?: string
  materialType?: string
  status?: string
}

export class MaterialApi<SecurityDataType = unknown> extends HttpClient<SecurityDataType> {
  get = (id: number, params: RequestParams = {}) => this.request<any, any>({ path: '/api/master-data/material/get', method: 'GET', query: { id }, secure: true, ...params })
  getPage = (data: PageInput<MaterialQueryInput>, params: RequestParams = {}) => this.request<any, any>({ path: '/api/master-data/material/get-page', method: 'POST', body: data, secure: true, type: ContentType.Json, ...params })
  add = (data: MaterialInput, params: RequestParams = {}) => this.request<any, any>({ path: '/api/master-data/material/add', method: 'POST', body: data, secure: true, type: ContentType.Json, ...params })
  update = (data: MaterialInput, params: RequestParams = {}) => this.request<any, any>({ path: '/api/master-data/material/update', method: 'PUT', body: data, secure: true, type: ContentType.Json, ...params })
  delete = (id: number, params: RequestParams = {}) => this.request<any, any>({ path: '/api/master-data/material/delete', method: 'DELETE', query: { id }, secure: true, ...params })
  setStatus = (id: number, status: string, params: RequestParams = {}) => this.request<any, any>({ path: '/api/master-data/material/set-status', method: 'PUT', query: { id, status }, secure: true, ...params })
}
