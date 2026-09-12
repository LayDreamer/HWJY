<template>
  <MyLayout>
    <el-card class="my-query-box mt8" shadow="never">
      <el-form :inline="true" @submit.prevent="query">
        <el-form-item label="物料编码"><el-input v-model="state.filter.materialCode" clearable /></el-form-item>
        <el-form-item label="物料名称"><el-input v-model="state.filter.materialName" clearable /></el-form-item>
        <el-form-item label="物料类型"><el-input v-model="state.filter.materialType" clearable /></el-form-item>
        <el-button type="primary" icon="ele-Search" @click="query">查询</el-button>
        <el-button v-auth="perms.create" type="primary" icon="ele-Plus" @click="openForm()">新增</el-button>
      </el-form>
    </el-card>
    <el-card class="my-fill mt8" shadow="never">
      <el-table v-loading="state.loading" :data="state.items" border>
        <el-table-column prop="materialCode" label="物料编码" min-width="140" />
        <el-table-column prop="materialName" label="物料名称" min-width="160" />
        <el-table-column prop="specification" label="规格" min-width="140" />
        <el-table-column prop="materialType" label="类型" min-width="100" />
        <el-table-column prop="unit" label="单位" width="80" />
        <el-table-column prop="status" label="状态" width="90" />
        <el-table-column label="操作" width="210" fixed="right">
          <template #default="{ row }">
            <el-button v-auth="perms.update" text type="primary" @click="openForm(row)">编辑</el-button>
            <el-button v-auth="perms.update" text type="primary" @click="toggleStatus(row)">{{ row.status === 'Enabled' ? '停用' : '启用' }}</el-button>
            <el-button v-auth="perms.delete" text type="danger" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div class="my-flex my-flex-end mt10"><el-pagination v-model:current-page="state.page.currentPage" v-model:page-size="state.page.pageSize" :total="state.total" layout="total, prev, pager, next" @current-change="query" /></div>
    </el-card>
    <el-dialog v-model="state.formVisible" :title="state.form.id ? '编辑物料' : '新增物料'" width="600px">
      <el-form ref="formRef" :model="state.form" label-width="90px">
        <el-form-item label="物料编码" prop="materialCode" :rules="[{ required: true, message: '请输入物料编码' }]"><el-input v-model="state.form.materialCode" /></el-form-item>
        <el-form-item label="物料名称" prop="materialName" :rules="[{ required: true, message: '请输入物料名称' }]"><el-input v-model="state.form.materialName" /></el-form-item>
        <el-form-item label="规格"><el-input v-model="state.form.specification" /></el-form-item>
        <el-form-item label="物料类型" prop="materialType" :rules="[{ required: true, message: '请输入物料类型' }]"><el-input v-model="state.form.materialType" /></el-form-item>
        <el-form-item label="单位"><el-input v-model="state.form.unit" /></el-form-item>
        <el-form-item label="采购/生产"><el-checkbox v-model="state.form.isPurchase">采购</el-checkbox><el-checkbox v-model="state.form.isProduce">生产</el-checkbox></el-form-item>
        <el-form-item label="提前期"><el-input-number v-model="state.form.leadTime" :min="0" /></el-form-item>
        <el-form-item label="安全库存"><el-input-number v-model="state.form.safetyStock" :min="0" /></el-form-item>
        <el-form-item label="备注"><el-input v-model="state.form.remark" type="textarea" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="state.formVisible = false">取消</el-button><el-button type="primary" @click="save">保存</el-button></template>
    </el-dialog>
  </MyLayout>
</template>

<script setup lang="ts" name="company/master-data/material">
import { MaterialApi, MaterialDto, MaterialInput, MaterialQueryInput } from '/@/api/master-data/material'

const api = new MaterialApi()
const formRef = ref()
const perms = { create: 'Material.Create', update: 'Material.Update', delete: 'Material.Delete' }
const blank = (): MaterialInput => ({ materialCode: '', materialName: '', materialType: '', status: 'Enabled', isPurchase: false, isProduce: false, leadTime: 0, safetyStock: 0 })
const state = reactive({ loading: false, total: 0, items: [] as MaterialDto[], filter: {} as MaterialQueryInput, page: { currentPage: 1, pageSize: 20 }, formVisible: false, form: blank() as MaterialInput })

const query = async () => { state.loading = true; const result = await api.getPage({ ...state.page, filter: state.filter }).finally(() => (state.loading = false)); state.items = result.data?.list ?? []; state.total = result.data?.total ?? 0 }
const openForm = async (row?: MaterialDto) => { state.form = row ? { ...(await api.get(row.id)).data } : blank(); state.formVisible = true }
const save = async () => { await formRef.value.validate(); state.form.id ? await api.update(state.form, { showSuccessMessage: true }) : await api.add(state.form, { showSuccessMessage: true }); state.formVisible = false; query() }
const remove = async (row: MaterialDto) => { await api.delete(row.id, { showSuccessMessage: true }); query() }
const toggleStatus = async (row: MaterialDto) => { await api.setStatus(row.id, row.status === 'Enabled' ? 'Disabled' : 'Enabled', { showSuccessMessage: true }); query() }
onMounted(query)
</script>
