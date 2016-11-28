using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;

namespace Qiniu.Util
{
    /// <summary>
    /// 字符串处理工具
    /// </summary>
    public class StringUtils
    {
        /// <summary>
        /// 字符串连接
        /// </summary>
        /// <param name="array">字符串数组</param>
        /// <param name="sep">连接符</param>
        /// <returns>连接后字符串</returns>
        public static string Join(string[] array, string sep)
        {
            if (array == null || sep == null)
            {
                return null;
            }
            StringBuilder joined = new StringBuilder();
            int arrayLength = array.Length;
            for (int i = 0; i < arrayLength; i++)
            {
                joined.Append(array[i]);
                if (i < arrayLength - 1)
                {
                    joined.Append(sep);
                }
            }
            return joined.ToString();
        }

        /// <summary>
        /// 以json格式连接字符串
        /// </summary>
        /// <param name="array">字符串数组</param>
        /// <returns>连接后字符串</returns>
        public static string JsonJoin(string[] array)
        {
            if (array == null)
            {
                return null;
            }
            StringBuilder joined = new StringBuilder();
            int arrayLength = array.Length;
            for (int i = 0; i < arrayLength; i++)
            {
                joined.Append("\"").Append(array[i]).Append("\"");
                if (i < arrayLength - 1)
                {
                    joined.Append(",");
                }
            }
            return joined.ToString();
        }

        /// <summary>
        /// 获取字符串Url安全Base64编码值
        /// </summary>
        /// <param name="from">源字符串</param>
        /// <returns>已编码字符串</returns>
        public static string UrlSafeBase64Encode(string from)
        {
            return UrlSafeBase64Encode(Encoding.UTF8.GetBytes(from));
        }

        public static string UrlSafeBase64Encode(byte[] from)
        {
            return Convert.ToBase64String(from).Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// 解码Url安全的Base64编码值
        /// </summary>
        /// <param name="from">编码字符串</param>
        /// <returns>已解码字符串</returns>
        public static byte[] UrlsafeBase64Decode(string from)
        {
            return Convert.FromBase64String(from.Replace('-', '+').Replace('_', '/'));
        }

        public static string JsonEncode(object obj)
        {
            JsonSerializerSettings setting = new JsonSerializerSettings();
            setting.NullValueHandling = NullValueHandling.Ignore;
            return JsonConvert.SerializeObject(obj, setting);
        }

        public static T JsonDecode<T>(string jsonData)
        {
            return JsonConvert.DeserializeObject<T>(jsonData);
        }

        public static string EncodedEntry(string bucket, string key)
        {
            if (key == null)
            {
                return UrlSafeBase64Encode(bucket);
            }
            else
            {
                return UrlSafeBase64Encode(bucket + ":" + key);
            }
        }

        public static byte[] Sha1(byte[] data)
        {
            var algorithm = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);
            // 原文的二进制数据
            IBuffer vector = CryptographicBuffer.CreateFromByteArray(data);

            // 哈希二进制数据
            IBuffer digest = algorithm.HashData(vector);
            byte[] sha1 = new byte[digest.Length];
            CryptographicBuffer.CopyToByteArray(digest, out sha1);
            return sha1;
        }

        public static string Urlencode(string from)
        {
            return Uri.EscapeDataString(from);
        }

        public static string UrlValuesEncode(Dictionary<string, string[]> urlValues)
        {
            StringBuilder urlValuesBuilder = new StringBuilder();
           
            foreach (KeyValuePair<string, string[]> kvp in urlValues)
            {
                foreach (string urlVal in kvp.Value)
                {
                    urlValuesBuilder.AppendFormat("{0}={1}&", Uri.EscapeDataString(kvp.Key), Uri.EscapeDataString(urlVal));
                }
            }
            string encodedStr=urlValuesBuilder.ToString();
            return encodedStr.Substring(0, encodedStr.Length - 1);
        }

        public static string Md5Hash(string plainText)
        {
            var algorithm = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5);
            // 原文的二进制数据
            IBuffer vector = CryptographicBuffer.ConvertStringToBinary(plainText, BinaryStringEncoding.Utf8);

            // 哈希二进制数据
            IBuffer digest = algorithm.HashData(vector);

            return CryptographicBuffer.EncodeToHexString(digest);
        }
    }
}