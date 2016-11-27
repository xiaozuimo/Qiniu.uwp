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


        public static string ToHexString(this string source)
        {
            return CryptographicBuffer.EncodeToHexString(CryptographicBuffer.ConvertStringToBinary(source, BinaryStringEncoding.Utf8));
        }


        public static string HexToBase64String(this string source)
        {
            return CryptographicBuffer.EncodeToBase64String(CryptographicBuffer.DecodeFromHexString(source));
        }


        public static string ToUrlSafeBase64String(this string source)
        {
            return source.Replace('+', '-').Replace('/', '_');
        }
    }
}
