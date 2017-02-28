using System.Text;
using Windows.Security.Cryptography.Core;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace Qiniu.Util
{
    /// <summary>
    /// 计算hash值
    /// 特别注意，不同平台使用的Cryptography可能略有不同，使用中如有遇到问题，请反馈
    /// 提交您的issue到 https://github.com/qiniu/csharp-sdk
    /// </summary>
    public class Hashing
    {
        /// <summary>
        /// 计算SHA1
        /// </summary>
        /// <param name="data">字节数据</param>
        /// <returns>SHA1</returns>
        public static byte[] CalcSHA1(byte[] data)
        {
            var algorithm = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);
            // 原文的二进制数据
            IBuffer vector = CryptographicBuffer.CreateFromByteArray(data);

            // 哈希二进制数据
            IBuffer digest = algorithm.HashData(vector);

            var bytes = new byte[digest.Length];
            CryptographicBuffer.CopyToByteArray(digest, out bytes);

            return bytes;
        }

        /// <summary>
        /// md5 hash in hex string
        /// </summary>
        /// <param name="str">待计算的字符串</param>
        /// <returns>MD5结果</returns>
        public static string CalcMD5(string str)
        {
            var algorithm = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5);
            // 原文的二进制数据
            IBuffer vector = CryptographicBuffer.ConvertStringToBinary(str, BinaryStringEncoding.Utf8);

            // 哈希二进制数据
            IBuffer digest = algorithm.HashData(vector);

            return CryptographicBuffer.EncodeToHexString(digest);
        }


        /// <summary>
        /// 计算MD5哈希(第三方实现)
        /// </summary>
        /// <param name="str">待计算的字符串,避免FIPS-Exception</param>
        /// <returns>MD5结果</returns>
        public static string CalcMD5X(string str)
        {
            byte[] data = Encoding.UTF8.GetBytes(str);
            LabMD5 md5 = new LabMD5();
            return md5.ComputeHash(data);
        }
    }
}
