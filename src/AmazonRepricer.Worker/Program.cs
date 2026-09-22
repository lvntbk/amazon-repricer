using AmazonRepricer.Worker.Observability;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using AmazonRepricer.Worker.Repricing;
using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Infrastructure;
using AmazonRepricer.Worker;
using AmazonRepricer.Worker.Amazon;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(
    options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.UseUtcTimestamp = true;
    });

var workerOtlpEndpoint =
    builder.Configuration["OpenTelemetry:OtlpEndpoint"];

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(
        resource =>
            resource.AddService(
                "amazon-repricer-worker"))
    .WithMetrics(
        metrics =>
        {
            metrics
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(RepricingMetrics.MeterName);

            if (!string.IsNullOrWhiteSpace(
                    workerOtlpEndpoint))
            {
                if (!Uri.TryCreate(
                        workerOtlpEndpoint,
                        UriKind.Absolute,
                        out var endpoint))
                {
                    throw new InvalidOperationException(
                        "OpenTelemetry:OtlpEndpoint must be a valid absolute URI.");
                }

                metrics.AddOtlpExporter(
                    options =>
                        options.Endpoint =
                            endpoint);
            }
        });

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<RepricingMetrics>();

builder.Services
    .AddOptions<WorkerOptions>()
    .Bind(
        builder.Configuration.GetSection(
            WorkerOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<WorkerOptions>,
    WorkerOptionsValidator>();

builder.Services.AddScoped<IPricingEngine, PricingEngine>();

builder.Services.AddScoped<
    IAutomaticRepricingGuard,
    AutomaticRepricingGuard>();

builder.Services.AddScoped<
    IAutomaticRepricingExecutor,
    AutomaticRepricingExecutor>();

builder.Services.AddScoped<
    IProductRepricingProcessor,
    ProductRepricingProcessor>();

builder.Services.AddScoped<
    IRepricingReconciliationService,
    RepricingReconciliationService>();

builder.Services.AddScoped<RepricingVerificationService>();

var useMockAmazon =
    builder.Configuration.GetValue<bool>(
        $"{AmazonSpApiOptions.SectionName}:UseMock");

if (useMockAmazon)
{
    builder.Services.AddScoped<
        IAmazonPricingProvider,
        MockAmazonPricingProvider>();
}
else
{
    builder.Services.AddScoped<IAmazonPricingProvider>(
        serviceProvider =>
            serviceProvider.GetRequiredService<AmazonSpApiPricingProvider>());
}

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<RepricingReconciliationWorker>();

var host = builder.Build();

host.Run();
