using NBomber.Contracts.Stats;
using NBomber.CSharp; //utilizando o NBombet

using var httpClient = new HttpClient(); //criando client

var scenario = Scenario.Create("Meu Primeiro Teste Carga", async context =>
{
    var response = await httpClient.GetAsync("http://localhost:9999/ping");

    return response.IsSuccessStatusCode
        ? Response.Ok()
        : Response.Fail();
})
    .WithWarmUpDuration(TimeSpan.FromSeconds(10))
    .WithLoadSimulations(
        Simulation.RampingConstant(copies: 20, during: TimeSpan.FromMinutes(1))   
    );

NBomberRunner
  .RegisterScenarios(scenario)
  .WithReportFileName("NBomber")
  .WithReportFolder("NBomber")
  .WithReportFormats(ReportFormat.Txt, ReportFormat.Csv, ReportFormat.Html, ReportFormat.Md)
  .Run();