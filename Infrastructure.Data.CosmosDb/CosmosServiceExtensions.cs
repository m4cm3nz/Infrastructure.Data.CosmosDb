using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;

namespace Infrastructure.Data.CosmosDb
{
    /// <summary>
    /// Extension methods for registering Cosmos DB services in the DI container.
    /// </summary>
    public static class CosmosServiceExtensions
    {
        /// <summary>
        /// Registers a <see cref="CosmosClient"/> singleton using the endpoint and key from
        /// <see cref="Settings"/> and <see cref="CosmosStjSerializer"/> as the serializer.
        /// Uses <c>System.Text.Json</c> with camelCase naming. Supports <c>[JsonPropertyName]</c> attributes.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional delegate to customize <see cref="CosmosClientOptions"/>.</param>
        /// <returns>The service collection.</returns>
        public static IServiceCollection AddCosmosClient(
            this IServiceCollection services,
            Action<CosmosClientOptions> configure = null)
        {
            services.AddSingleton<CosmosClient>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<Settings>>().Value;
                var options = new CosmosClientOptions
                {
                    Serializer = new CosmosStjSerializer()
                };
                configure?.Invoke(options);
                return new CosmosClient(settings.Endpoint, settings.Key, options);
            });

            return services;
        }

        /// <summary>
        /// Registers a <see cref="CosmosClient"/> singleton using the endpoint and key from
        /// <see cref="Settings"/> and the Cosmos SDK built-in Newtonsoft.Json serializer with
        /// camelCase naming. Supports <c>[Newtonsoft.Json.JsonProperty]</c> attributes.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional delegate to customize <see cref="CosmosClientOptions"/>.</param>
        /// <returns>The service collection.</returns>
        public static IServiceCollection AddCosmosClientWithNewtonsoft(
            this IServiceCollection services,
            Action<CosmosClientOptions> configure = null)
        {
            services.AddSingleton<CosmosClient>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<Settings>>().Value;
                var options = new CosmosClientOptions
                {
                    SerializerOptions = new CosmosSerializationOptions
                    {
                        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                    }
                };
                configure?.Invoke(options);
                return new CosmosClient(settings.Endpoint, settings.Key, options);
            });

            return services;
        }
    }
}
