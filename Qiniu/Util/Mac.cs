using System;
using System.IO;
using System.Text;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;

namespace Qiniu.Util
{
    public class Mac
    {
        public string AccessKey { set; get; }
        public string SecretKey { set; get; }
        public Mac(string accessKey, string secretKey)
        {
            this.AccessKey = accessKey;
            this.SecretKey = secretKey;
        }

        private string _sign(byte[] data)
        {
            return Crypto.HmacSha1(data, SecretKey).HexToBase64String().ToUrlSafeBase64String();
        }

        public string Sign(byte[] data)
        {
            return string.Format("{0}:{1}", this.AccessKey, this._sign(data));
        }

        public string SignWithData(byte[] data)
        {
            string encodedData = StringUtils.UrlSafeBase64Encode(data);
            return string.Format("{0}:{1}:{2}", this.AccessKey, this._sign(Encoding.UTF8.GetBytes(encodedData)), encodedData);
        }

        public string SignRequest(string url, byte[] reqBody)
        {
            Uri u = new Uri(url);
            string pathAndQuery = u.PathAndQuery;
            byte[] pathAndQueryBytes = Encoding.UTF8.GetBytes(pathAndQuery);
            using (MemoryStream buffer = new MemoryStream())
            {
                buffer.Write(pathAndQueryBytes, 0, pathAndQueryBytes.Length);
                buffer.WriteByte((byte)'\n');
                if (reqBody != null && reqBody.Length > 0)
                {
                    buffer.Write(reqBody, 0, reqBody.Length);
                }
                string digestBase64 = Crypto.HmacSha1(buffer.ToArray(), SecretKey).HexToBase64String().ToUrlSafeBase64String();
                return string.Format("{0}:{1}", this.AccessKey, digestBase64);
            }
        }
    }
}