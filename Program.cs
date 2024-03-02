using System.Text.Json.Serialization;
using Rinha2024.Dotnet;
using Rinha2024.Dotnet.Extensions;

var builder = WebApplication.CreateSlimBuilder(args);

builder.WebHost.UseKestrel(opt =>
{
    opt.Limits.MaxConcurrentConnections = int.TryParse(Environment.GetEnvironmentVariable("MAX_CONCURRENT_CONNECTIONS"), out var maxConcurrentConnections) ? maxConcurrentConnections : 3000;
    opt.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(60);
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});
builder.Services.AddSingleton<VirtualService>();
builder.Services.AddSingleton<ExceptionMiddleware>();
builder.Services.AddLogging(l => l.AddSimpleConsole());
var app = builder.Build();
app.SetControllers();   
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
