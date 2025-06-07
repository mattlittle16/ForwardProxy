using System.Net;
using System.Text.Json.Serialization;
using ForwardProxy.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLogging();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.JsonSerializerOptions.WriteIndented = true;
});

builder.Services.AddScoped<IForwardService, ForwardService>(); // Register the ForwardService
builder.Services.AddHttpClient("ignore-ssl")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    });

var app = builder.Build();
app.UseHttpsRedirection();
app.UseCors(policy => 
{
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader();
});

app.MapControllers();
 app.Use((context, next) =>
            {
                context.Request.EnableBuffering();
                return next();
            });

app.Run();