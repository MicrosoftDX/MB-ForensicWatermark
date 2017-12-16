using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider
{
    public static class HttpRequestMessageExtensions
    {
        public static T GetBodyData<T>(this string payload) where T : class, new()
        {
            T result = default(T);

            if (payload != null)
            {
                result = JsonConvert.DeserializeObject<T>(payload,
                    new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver()
                    });
            }


            return result;
        }
    }
}
