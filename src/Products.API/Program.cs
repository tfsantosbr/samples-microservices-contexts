using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder => tracerProviderBuilder
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Products.API"))
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://otel-collector:4317");
            options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        }))
    .WithMetrics(meterProviderBuilder => meterProviderBuilder
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Products.API"))
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://otel-collector:4317");
            options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        }));

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Products.API"));
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.ParseStateValues = true;
    options.AddOtlpExporter(otlpOptions =>
    {
        otlpOptions.Endpoint = new Uri("http://otel-collector:4317");
        otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
    });
});

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Product Endpoints

app.MapGet("/products", () => Results.Ok(new[] { new { Name = "Product 1" }, new { Name = "Product 2" } }));
app.MapGet("/products/{id}", (int id) => Results.Ok(new { Name = $"Product {id}" }));
app.MapPost("/products", () => Results.Created("/products/1", new { Name = "Product 1" }));
app.MapPut("/products/{id}", (int id) => Results.NoContent());
app.MapDelete("/products/{id}", (int id) => Results.NoContent());

// Categories Endpoint

app.MapGet("/products/categories", () => Results.Ok(new[]
{
    new { Name = "Electronics" },
    new { Name = "Books" },
    new { Name = "Clothing" },
    new { Name = "Food" }
}));

// healthcheck Endpoints

app.MapHealthChecks("/health");

// Add correlation id middleware
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    if (!context.Request.Headers.TryGetValue("CorrelationId", out var correlationId))
    {
        correlationId = Guid.NewGuid().ToString();
        context.Request.Headers.Append("CorrelationId", correlationId);
    }

    context.Response.Headers.Append("CorrelationId", correlationId);

    var correlationIdLogScope = new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId
    };

    using (logger.BeginScope(correlationIdLogScope))
    {
        await next(context);
    }
});

// Middleware de logging
app.Use(async (context, next) =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Handling request: {Method} {Path}", context.Request.Method, context.Request.Path);
    await next.Invoke();
    logger.LogInformation("Finished handling request.");
});

app.Run();
