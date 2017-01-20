using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;

namespace Qiniu.Util
{
    /// <summary>
    /// From Gallery.Utils
    /// </summary>
    public static class Crypto
    {
        /// <summary>
        /// HmacSha1
        /// </summary>
        /// <param name="data"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string HmacSha1(byte[] data, string key)
        {
            var hmacAlgorithm = MacAlgorithmProvider.OpenAlgorithm(MacAlgorithmNames.HmacSha1);
            IBuffer encryptKey = CryptographicBuffer.ConvertStringToBinary(key, BinaryStringEncoding.Utf8);

            CryptographicKey hmacKey = hmacAlgorithm.CreateKey(encryptKey);
            IBuffer signature = CryptographicEngine.Sign(
                hmacKey,
                CryptographicBuffer.CreateFromByteArray(data)
            );

            return CryptographicBuffer.EncodeToHexString(signature);
        }

        /// <summary>
        /// Convert To HexString
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static string ToHexString(this string source)
        {
            return CryptographicBuffer.EncodeToHexString(CryptographicBuffer.ConvertStringToBinary(source, BinaryStringEncoding.Utf8));
        }

        /// <summary>
        /// Convert HexString to Base64String
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static string HexToBase64String(this string source)
        {
            return CryptographicBuffer.EncodeToBase64String(CryptographicBuffer.DecodeFromHexString(source));
        }

        /// <summary>
        /// Convert to UrlSafeBase64String
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static string ToUrlSafeBase64String(this string source)
        {
            return source.Replace('+', '-').Replace('/', '_');
        }
    }
}
