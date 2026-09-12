using ZhonTai.Admin.Core;
using ZhonTai.Admin.Core.Startup;
using Autofac;
using Company.MasterData.Repositories;

new HostApp(new HostAppOptions
{
    ConfigureAutofacContainer = (builder, _) =>
        builder.RegisterGeneric(typeof(MasterDataRepositoryBase<>)).InstancePerLifetimeScope().PropertiesAutowired()
}).Run(args, typeof(Program).Assembly);

public partial class Program;
