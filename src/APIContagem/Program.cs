using APIContagem;
using APIContagem.Models;
using APIContagem.Tracing;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

OpenTelemetryExtensions.Initialize(builder.Configuration);
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName: OpenTelemetryExtensions.ServiceName!,
        serviceVersion: OpenTelemetryExtensions.ServiceVersion!);
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(OpenTelemetryExtensions.ServiceName!)
        .AddSource("Microsoft.AspNetCore")
        .SetResourceBuilder(resourceBuilder)
        .AddOtlpExporter(cfg =>
        {
            cfg.Endpoint = new Uri(builder.Configuration["OtlpExporter:Endpoint"]!);
        }));

builder.Services.AddSingleton<Contador>();

builder.Services.AddOpenApi();
builder.Services.AddCors();

var app = builder.Build();

app.MapOpenApi();

app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

app.UseHttpsRedirection();

Lock ContagemLock = new();
var simularFalhas = Convert.ToBoolean(app.Configuration["SimularFalhas"]);

app.MapGet("/contador", (Contador contador) =>
{
    using var activity1 = OpenTelemetryExtensions.ActivitySource!
        .StartActivity("GerarValorContagem")!;

    int valorAtualContador;
    using (ContagemLock.EnterScope())
    {
        contador.Incrementar();
        valorAtualContador = contador.ValorAtual;
    }
    activity1.SetTag("valorAtual", valorAtualContador);
    app.Logger.LogInformation($"Contador - Valor atual: {valorAtualContador}");

    if (valorAtualContador % 4 == 0 && simularFalhas)
    {
        app.Logger.LogWarning("Simulacao de falha: contador atingiu um multiplo de 4!");
        throw new Exception("Contador atingiu um valor invalido!");
    }

    activity1.Stop();

    using var activity2 = OpenTelemetryExtensions.ActivitySource
        .StartActivity("RetornarValorContagem")!;

    var resultadoContador = new ResultadoContador()
    {
        ValorAtual = contador.ValorAtual,
        Local = contador.Local,
        Kernel = contador.Kernel,
        Framework = contador.Framework,
        Mensagem = app.Configuration["Saudacao"]
    };

    activity2.SetTag("valorAtual", valorAtualContador);
    activity2.SetTag("horario", $"{DateTime.UtcNow.AddHours(-3):HH:mm:ss}");

    return Results.Ok(resultadoContador);
})
.Produces<ResultadoContador>();

app.MapGet("/contador/decremento", (Contador contador) =>
{
    using var activity1 = OpenTelemetryExtensions.ActivitySource!
        .StartActivity("GerarValorContagemDecrescente")!;

    int valorAtualContador;
    using (ContagemLock.EnterScope())
    {
        contador.Decrementar();
        valorAtualContador = contador.ValorAtual;
    }
    activity1.SetTag("valorAtual", valorAtualContador);
    app.Logger.LogInformation($"Contador - Valor atual: {valorAtualContador}");

    activity1.Stop();

    using var activity2 = OpenTelemetryExtensions.ActivitySource
        .StartActivity("RetornarValorContagem")!;

    var resultadoContador = new ResultadoContador()
    {
        ValorAtual = valorAtualContador,
        Local = contador.Local,
        Kernel = contador.Kernel,
        Framework = contador.Framework,
        Mensagem = app.Configuration["Saudacao"]
    };

    activity2.SetTag("valorAtual", valorAtualContador);
    activity2.SetTag("horario", $"{DateTime.UtcNow.AddHours(-3):HH:mm:ss}");

    return Results.Ok(resultadoContador);
})
.WithSummary("Decrementa o contador")
.WithDescription("Decrementa o valor atual do contador e retorna os dados do ambiente.")
.Produces<ResultadoContador>();

app.Run();