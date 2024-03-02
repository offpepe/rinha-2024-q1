using System.Text.Json.Serialization;
using Npgsql;
using Rinha2024.Dotnet;
using Rinha2024.Dotnet.Extensions;

var builder = WebApplication.CreateSlimBuilder(args);

builder.WebHost.UseKestrel(opt =>
{
    opt.Limits.MaxConcurrentConnections = int.TryParse(Environment.GetEnvironmentVariable("MAX_CONCURRENT_CONNECTIONS"), out var maxConcurrentConnections) ? maxConcurrentConnections : 500;
    opt.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(60);
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});
builder.Services.AddSingleton<NpgsqlDataSource>(_ => new NpgsqlSlimDataSourceBuilder(builder.Configuration.GetConnectionString("DB")!).Build());
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<ExceptionMiddleware>();
builder.Services.AddLogging(l => l.AddSimpleConsole()); 
var app = builder.Build();
app.SetControllers();
await app.Services.GetRequiredService<Database>().Stretching();
app.Run();

[JsonSerializable(typeof(SaldoDto))]
[JsonSerializable(typeof(TransactionDto))]
[JsonSerializable(typeof(CreateTransactionDto))]
[JsonSerializable(typeof(ExtractDto))]
[JsonSerializable(typeof(ValidateTransactionDto))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
    //empty
}
