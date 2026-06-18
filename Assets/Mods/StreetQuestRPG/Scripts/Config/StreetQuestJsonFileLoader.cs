using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace StreetQuestRPG
{
    internal static class StreetQuestJsonFileLoader
    {
        public static T Load<T>(string path) where T : class
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path)));
                return serializer.ReadObject(stream) as T;
            }
            catch (Exception exception)
            {
                StreetQuestShared.LogConfigLoadFailure(typeof(T).Name, path, exception);
                throw;
            }
        }
    }
}
