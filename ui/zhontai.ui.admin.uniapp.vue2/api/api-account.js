import store from 'store'
import httpApi from '../libs/config/http-api.js'
import { showToast } from '../libs/utils/util.js'

let tokenRefresh

export const api = {
	//登录
	login(params, config = {}) {
		config = uni.$u.deepMerge({
			custom: { 
				auth: false,
				loading: {
					enable: true,
					msg: '登录中'
				},
				error: {
					enable: true
				}
			} 
		}, config)
		
		if(params.isPwd){
			return httpApi.post('/api/admin/auth/login', {
				"userName": params.mobile,
				"password": params.password
			}, config)
		} else {
			return httpApi.post('/api/admin/auth/mobile-login', {
				"mobile": params.mobile,
				"code": params.smsCode
			}, config)
		}
	},
	//退出登录并撤销刷新会话
	logout(params, config = {}) {
		config = uni.$u.deepMerge({
			custom: {
				auth: false,
				loading: { enable: false },
				error: { enable: false }
			}
		}, config)
		return httpApi.post('/api/admin/auth/logout', { refreshToken: params?.refreshToken || '' }, config)
	},
	//获得短信验证码
	getSmsCode(params, config = {}) {
		config = uni.$u.deepMerge({
			custom: { 
				auth: false,
				loading: {
					enable: true,
					msg: '正在获取验证码'
				},
				ok: {
					enable: true,
					msg: '验证码已发送'
				},
				error: {
					enable: true
				}
			} 
		}, config)
		
		const data = {
			mobile: params.mobile,
			codeId: params.code,
			captchaId: params.code
		}
		return httpApi.post('/api/admin/captcha/send-sms-code', data, config)
	},
	//修改密码-发送短信验证码
	sendSmsCodeForChangePassword(params, config = {}) {
		config = uni.$u.deepMerge({
			custom: {
				loading: {
					enable: true,
					msg: '正在发送验证码'
				},
				ok: {
					enable: true,
					msg: '验证码已发送'
				},
				error: {
					enable: true
				}
			},
		}, config)
		config.params = params
		
		return httpApi.post('/api/admin/captcha/send-sms-code', null, config)
	},
	//修改密码
	changePassword(params, config = {}) {
		config = uni.$u.deepMerge({
			custom: {
				loading: {
					enable: true,
					msg: '保存中'
				},
				error: {
					enable: true
				}
			},
		}, config)
		
		if(params.isPwd){
			return httpApi.put('api/admin/user/change-password', {
				oldPassword: params.currentPassword,
				newPassword: params.newPassword,
				confirmPassword: params.newPassword
			}, config)
		}else{
			return httpApi.put('api/admin/user/change-password', {
				oldPassword: params.currentPassword,
				newPassword: params.newPassword,
				confirmPassword: params.newPassword
			}, config)
		}
	},
	//获得手机号
	getPhoneNumber(params, config = {}) {
		config = uni.$u.deepMerge({
			custom: {
				auth: false,
				error: {
					enable: false
				}
			},
		}, config)
		
		return httpApi.post('', params, config)
	},
	//刷新token
	refreshToken(params, config = {}) {
		const accessToken = store.state.account.token
		const refreshToken = params?.refreshToken
		const isCurrentSession = () => store.state.account.token === accessToken &&
			store.state.account.refreshToken === refreshToken
		if (!accessToken || !refreshToken || !isCurrentSession()) {
			return Promise.reject(new Error('登录状态已变化，请重新发起请求'))
		}
		// Share only this credential's refresh; an old request cannot own a new login's waiters.
		if (!tokenRefresh || tokenRefresh.token !== refreshToken) {
			const refreshPromise = Promise.resolve().then(() => {
				if (!isCurrentSession()) throw new Error('登录状态已变化，请重新发起请求')
				config = uni.$u.deepMerge({
					custom: {
						auth: false,
						catch: false,
						error: {
							enable: false
						}
					},
				}, config)
				delete config.params
				return httpApi.post('/api/admin/auth/refresh', { refreshToken }, config)
			}).then(res => {
				if (!isCurrentSession()) throw new Error('登录状态已变化，请重新发起请求')
				if (!res?.success || !res.data?.accessToken || !res.data?.refreshToken) {
					const lastPage = uni.$u.page()
					showToast('您还没有登录', function(){
						const currentPage = uni.$u.page()
						const loginPage = '/pages/account/login'
						if(isCurrentSession() && lastPage === currentPage && currentPage !== loginPage){
							uni.navigateTo({ url: loginPage })
						}
					},1000)
					throw new Error('登录已失效，请重新登录')
				}
				store.commit('account/updateAuth', res.data, { root: true })
				return res.data
			}).finally(() => {
				if (tokenRefresh?.promise === refreshPromise) tokenRefresh = undefined
			})
			tokenRefresh = { token: refreshToken, promise: refreshPromise }
		}
		return tokenRefresh.promise
	},
}
