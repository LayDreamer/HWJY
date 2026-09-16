using ZhonTai.Admin.Core;
using ZhonTai.Admin.Core.Startup;
using Autofac;
using Company.MasterData.Repositories;

if (!args.Contains("--smoke-fixture", StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Company.MasterData.Host is a smoke fixture. Start it explicitly with --smoke-fixture.");
    return;
}

new HostApp(new HostAppOptions
{
    ConfigureAutofacContainer = (builder, _) =>
        builder.RegisterGeneric(typeof(MasterDataRepositoryBase<>)).InstancePerLifetimeScope().PropertiesAutowired()
}).Run(args, typeof(Program).Assembly);

public partial class Program;
