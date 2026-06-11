using Microsoft.Azure.Cosmos;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Data.CosmosDb
{
    /// <summary>
    /// A <see cref="CosmosLinqSerializer"/> based on <c>System.Text.Json</c> with camelCase naming.
    /// Maps <c>Id</c> → <c>id</c>, <c>CustomerName</c> → <c>customerName</c>, etc.
    /// <c>[JsonPropertyName]</c> attributes take precedence over the naming policy.
    /// Pass an instance to <see cref="CosmosClientOptions.Serializer"/> when registering the
    /// <see cref="CosmosClient"/> singleton.
    /// </summary>
    public sealed class CosmosStjSerializer : CosmosLinqSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        /// <inheritdoc/>
        public override T FromStream<T>(Stream stream)
        {
            using (stream)
            {
                if (typeof(Stream).IsAssignableFrom(typeof(T)))
                    return (T)(object)stream;
                return JsonSerializer.Deserialize<T>(stream, Options);
            }
        }

        /// <inheritdoc/>
        public override Stream ToStream<T>(T input)
        {
            var ms = new MemoryStream();
            JsonSerializer.Serialize(ms, input, Options);
            ms.Position = 0;
            return ms;
        }

        /// <inheritdoc/>
        public override string SerializeMemberName(MemberInfo memberInfo)
        {
            var attr = memberInfo.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (attr != null)
                return attr.Name;
            return JsonNamingPolicy.CamelCase.ConvertName(memberInfo.Name);
        }
    }
}
