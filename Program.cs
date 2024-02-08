using Rinha2024.Dotnet;
using Rinha2024.Dotnet.DTOs;
using Rinha2024.Dotnet.Extensions;
using System.Text.Json.Serialization;

var dbHost = Environment.GetEnvironmentVariable("DB_HOSTNAME");
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseKestrel();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});
builder.Services.AddSingleton<Service>(_ => new Service(builder.Configuration
    .GetConnectionString("DB")!.Replace("@host", dbHost)));
builder.Services.AddLogging(l => l.AddSimpleConsole());
var app = builder.Build();
app.SetControllers();
app.Run();

[JsonSerializable(typeof(SaldoDto))]
[JsonSerializable(typeof(TransactionDto))]
[JsonSerializable(typeof(TransactionDto[]))]
[JsonSerializable(typeof(CreateTransactionDto))]
[JsonSerializable(typeof(ExtractDto))]
[JsonSerializable(typeof(ValidateTransactionDto))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}