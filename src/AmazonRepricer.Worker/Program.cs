using AmazonRepricer.Worker.Repricing;
using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Infrastructure;
using AmazonRepricer.Worker;
using AmazonRepricer.Worker.Amazon;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

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
