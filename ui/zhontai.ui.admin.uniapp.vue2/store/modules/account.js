import { api } from '@/api/api-account.js'

const auth = uni.getStorageSync("app_token")
export default {
	namespaced: true,
	state: {
		token: auth?.accessToken || auth?.token || '',
		refreshToken: auth?.refreshToken || '',
		expireTime: auth?.accessTokenExpiresAt || auth?.expireTime || 0,
		openId: auth?.openId || '',
		userInfo: uni.getStorageSync("app_user_info") || null,
	},
	getters: {
		isLogin(state){
			return state.token ? true : false
		},
		nickName(state){
			return state.userInfo?.nickName || ''
		},
		userName(state){
			return state.userInfo?.userName || ''
		},
		userId(state){
			return state.userInfo?.userId || ''
		},
		mobile(state){
			let mobile = ''
			const userName = state.userInfo?.userName
			if(userName && uni.$u.test.mobile(userName)){
				mobile = userName
			}else{
				const phoneNumber = auth?.phoneNumber
				if(phoneNumber && uni.$u.test.mobile(phoneNumber))
				{
					mobile = phoneNumber
				}
			}
			return mobile
		},
		avatarUrl(state){
			return state.userInfo?.avatarUrl || ''
		}
	},
	mutations: {
		setAuth(state, data){
			state.token = data?.accessToken || data?.token || ''
			state.refreshToken = data?.refreshToken || ''
			state.expireTime = data?.accessTokenExpiresAt || data?.expireTime || 0
			uni.setStorageSync('app_token', data)
		},
		updateAuth(state, data){
			state.token = data?.accessToken || data?.token || ''
			state.refreshToken = data?.refreshToken || ''
			state.expireTime = data?.accessTokenExpiresAt || data?.expireTime || 0
			const currentAuth = uni.getStorageSync('app_token') || {}
			uni.setStorageSync('app_token', { ...currentAuth, ...data })
		},
		setUserInfo(state, data){
			state.userInfo = data
			uni.setStorageSync('app_user_info', data)
		},
		logout(state){
			uni.removeStorageSync('app_token')
			uni.removeStorageSync('app_mobile')
			uni.removeStorageSync('app_user_info')
			state.token = ''
			state.refreshToken = ''
			state.expireTime = 0
			state.mobile = ''
			state.userInfo = null
		}
	},
	actions: {
		async login({ commit }, data){
			const res = await api.login(data)
			if(res?.success){
				commit('setAuth', res.data)
				commit('setUserInfo', {
					userName: data.mobile || '',
					nickName: data.mobile || ''
				})
			}
			return res
		},
		async logout({ commit, state }){
			const refreshToken = state.refreshToken
			// Clear before awaiting the server so late refreshes cannot restore this login.
			commit('logout')
			if (refreshToken) {
				await api.logout({ refreshToken })
			}
		}
	}
}
