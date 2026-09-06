using System.Text.Json;

namespace Vanadium.Utils
{
	public static class StringUtils
	{
		public static string Sanitize(this string str)
		{
			return str;
		}
		public static bool IsPure(this string str)
		{
			return true;
		}
		public static T? FromJson<T>(this string json)
		{
			return JsonSerializer.Deserialize<T>(json);
		}
		public static string ToJson(this object val)
		{
			return JsonSerializer.Serialize(val);
		}
		public static string ToBase64(this string str)
		{
			var bytes = System.Text.Encoding.UTF8.GetBytes(str);
			var b64 = Convert.ToBase64String(bytes);
			return b64;
		}
	}
}
