using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Infrastructure;
using AmazonRepricer.Worker;
using AmazonRepricer.Worker.Amazon;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection(WorkerOptions.SectionName));

builder.Services.Configure<AmazonSpApiOptions>(
    builder.Configuration.GetSection(AmazonSpApiOptions.SectionName));

builder.Services.AddScoped<IPricingEngine, PricingEngine>();

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

var host = builder.Build();

host.Run();
