<template>
  <div class="home-container layout-pd">
    <el-card shadow="never">
      <template #header>{{ t('后台管理系统') }}</template>
      <p class="home-welcome">{{ t('请从左侧菜单选择功能') }}</p>
    </el-card>
  </div>
</template>

<script setup lang="ts" name="admin/workbench">
import { t } from '/@/i18n'
</script>

<style scoped>
.home-welcome {
  margin: 0;
  color: var(--el-text-color-regular);
}
</style>
