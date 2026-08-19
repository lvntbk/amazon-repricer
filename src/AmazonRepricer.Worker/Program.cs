using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Infrastructure;
using AmazonRepricer.Worker;
using AmazonRepricer.Worker.Amazon;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddScoped<IPricingEngine, PricingEngine>();

builder.Services.AddScoped<
    IAmazonPricingProvider,
    MockAmazonPricingProvider>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();
