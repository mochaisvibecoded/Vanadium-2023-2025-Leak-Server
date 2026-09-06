using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace Vanadium.Utils
{
    public class Utils
    {
        public static ContentResult ErrorRoom(string errorId, string error)
        {
            return new ContentResult()
            {
                Content = JsonConvert.SerializeObject(new
                {
                    Success = false,
                    Value = (string?)null,
                    ErrorId = errorId,
                    Error = error,
                }),
                ContentType = "application/json",
                StatusCode = 200
            };
        }
    }
}
