# 对象存储配置

默认 `ossconfig.json` 中所有对象存储供应商均为 `Enable: false`，上传继续使用本地目录，不连接对象存储。仅启用实际使用的供应商；同一供应商配置一项，对应一个业务桶。

启用 Minio 前准备 Endpoint、BucketName、Region、AccessKey 和 SecretKey，按服务端实际协议设置 `IsEnableHttps`。真实凭据通过不入库的部署配置提供，例如用 `ADMINCORE_CONFIGCENTER_PATH` 指向独立配置目录；不要写入仓库配置或文档。账号需要检查/创建桶及读取、更新桶策略的权限。

## 启动与桶权限

- 服务注册阶段不连接对象存储；宿主启动时异步检查启用的桶并创建缺失桶。初始化失败会使宿主启动失败，不会忽略权限错误继续提供服务。Minio 请求使用启动取消信号，初始化客户端使用后释放。
- Minio 的 `PublicRead` 默认为 `false`。启动会撤销桶策略中匿名 `Allow` 授权，保留指定账号授权和显式 `Deny`。首次上线前应检查已有策略，确认业务不再依赖旧的匿名链接。
- 只有独立公共资源桶才设置 `PublicRead: true`。启动会将匿名授权收敛为 `s3:GetObject`，资源为该桶的 `/*`，包括无扩展名对象；不会新增匿名列举或写入权限。指定账号授权和显式 `Deny` 继续保留，因此显式拒绝仍可能阻止公共读取。
- `PublicRead` 仅管理 Minio 桶策略，不修改其他云供应商的访问策略，也不修改对象 ACL、用户身份策略或外部代理配置。其他供应商的访问范围须在对应服务端确认。

私有桶的直链不保证可匿名访问。现有文件服务仍返回配置的 URL，当前改动不提供受权下载接口或自动签名链接；业务附件应通过已配置的受控访问方式读取，不应为了使直链可用而将业务桶设为公共读。

## 回归验证

在仓库根目录执行：

```powershell
dotnet restore src/ZhonTai.sln --disable-parallel
dotnet test src/modules/admin/ZhonTai.Admin.Tests/ZhonTai.Admin.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~OSSInitializationTests
```

该套件已标记 `P0Gate=Required`，会随现有质量门禁运行，不新增工作流或依赖。测试通过本机随机回环端口的 S3 协议服务器调用真实 Minio SDK，覆盖注册阶段无网络、禁用项过滤、新建私有桶、旧匿名授权撤销、指定账号和拒绝规则保留、显式公共读、重复启动、启动取消及权限失败。

这些协议回归不等于真实 Minio 或其他云存储验收，也不验证生产 TLS 证书。启用外部存储前，还需在专用测试桶检查实际上传、受控读取、匿名读写范围、HTTPS 及清理；不要把尚未执行的服务端验收标记为通过。
