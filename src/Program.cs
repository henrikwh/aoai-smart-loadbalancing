using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Transforms;

namespace openai_loadbalancer;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var backendConfiguration = BackendConfig.LoadConfig(builder.Configuration);
        var yarpConfiguration = new YarpConfiguration(backendConfiguration);
        string? proxyKey = Environment.GetEnvironmentVariable("proxy_api_key");
        if (proxyKey == null)
            throw new ArgumentException("proxy_key not set");
        builder.Services.AddSingleton<IPassiveHealthCheckPolicy, ThrottlingHealthPolicy>();
        builder.Services.AddReverseProxy().AddTransforms(m =>
        {
            m.AddRequestTransform(yarpConfiguration.TransformRequest());
            m.AddResponseTransform(yarpConfiguration.TransformResponse());
        }).LoadFromMemory(yarpConfiguration.GetRoutes(), yarpConfiguration.GetClusters());

        builder.Services.AddHealthChecks();
        var app = builder.Build();

        app.MapHealthChecks("/healthz");
        app.MapReverseProxy(m =>
        {
            m.UseMiddleware<RetryMiddleware>(backendConfiguration);
            m.UsePassiveHealthChecks();
            m.Use( (context, next) =>
            {
                if (context.Request.Headers.TryGetValue("proxy-api-key", out StringValues s))
                {
                    var key = s.First();
                    if (proxyKey == key )
                        return next();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return context.Response.WriteAsync("Not authorized for proxy");
                }
                else {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return context.Response.WriteAsync("Proxy API key not provided");

                };
                
            });
        });

        app.Run();
    }
}
