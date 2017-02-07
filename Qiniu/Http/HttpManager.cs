using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using Qiniu.Util;
using System.Threading.Tasks;

namespace Qiniu.Http
{
    /// <summary>
    /// HttpManager for .NET 4.5+ and for .NET Core
    /// </summary>
    public class HttpManager
    {
        private bool allowAutoRedirect;
        private HttpClient client;
        private string userAgent;

        /// <summary>
        /// HTTP超时间隔默认值(单位：秒)
        /// </summary>
        public int TIMEOUT_DEF_SEC = 30;

        /// <summary>
        /// HTTP超时间隔最大值(单位：秒)
        /// </summary>
        public int TIMEOUT_MAX_SEC = 60;

        /// <summary>
        /// 初始化
        /// </summary>
        public HttpManager(bool allowAutoRedirect = false)
        {
            var handler = new HttpClientHandler() { AllowAutoRedirect = allowAutoRedirect };
            client = new HttpClient(handler);
            userAgent = GetUserAgent();
            this.allowAutoRedirect = allowAutoRedirect;
        }

        /// <summary>
        /// 清理
        /// </summary>
        ~HttpManager()
        {
            client.Dispose();
            client = null;
        }

        /// <summary>
        /// 客户端标识
        /// </summary>
        /// <returns>客户端标识UA</returns>
        public static string GetUserAgent()
        {
            string osDesc = "Windows10";
            return string.Format("{0}-{1}/{2} ({3})", QiniuCSharpSDK.ALIAS, QiniuCSharpSDK.RTFX, QiniuCSharpSDK.VERSION, osDesc);
        }

        /// <summary>
        /// 多部分表单数据(multi-part form-data)的分界(boundary)标识
        /// </summary>
        /// <returns>分界(boundary)标识字符串</returns>
        public static string CreateFormDataBoundary()
        {
            string now = DateTime.UtcNow.Ticks.ToString();
            return string.Format("-------{0}Boundary{1}", QiniuCSharpSDK.ALIAS, Hashing.CalcMD5(now));
        }

        /// <summary>
        /// 设置HTTP超时间隔
        /// </summary>
        /// <param name="seconds">超时间隔，单位为秒</param>
        public void SetTimeout(int seconds)
        {
            if (seconds >= 1 && seconds <= TIMEOUT_MAX_SEC)
            {
                TIMEOUT_DEF_SEC = seconds;
            }

            client.Timeout = new TimeSpan(0, 0, TIMEOUT_DEF_SEC);
        }

        /// <summary>
        /// HTTP-GET方法
        /// </summary>
        /// <param name="url">请求目标URL</param>
        /// <param name="token">令牌(凭证)[可选->设置为null]</param>
        /// <param name="binaryMode">是否以二进制模式读取响应内容(默认:否，即表示以文本方式读取)</param>
        /// <returns>HTTP-GET的响应结果</returns>
        public async Task<HttpResult> GetAsync(string url, string token, bool binaryMode = false)
        {
            HttpResult result = new HttpResult();

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", userAgent);
                if (!string.IsNullOrEmpty(token))
                {
                    req.Headers.Add("Authorization", token);
                }

                var msg = await client.SendAsync(req);
                result.Code = (int)msg.StatusCode;
                result.RefCode = (int)msg.StatusCode;

                if (binaryMode)
                {
                    result.Data = await msg.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    result.Text = await msg.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [HTTP-GET] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// HTTP-POST方法(不包含body数据)
        /// </summary>
        /// <param name="url">请求目标URL</param>
        /// <param name="token">令牌(凭证)[可选]</param>
        /// <param name="binaryMode">是否以二进制模式读取响应内容(默认:否，即表示以文本方式读取)</param>
        /// <returns>HTTP-POST的响应结果</returns>
        public async Task<HttpResult> PostAsync(string url, string token, bool binaryMode = false)
        {
            HttpResult result = new HttpResult();

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("User-Agent", userAgent);
                if (!string.IsNullOrEmpty(token))
                {
                    req.Headers.Add("Authorization", token);
                }

                var msg = await client.SendAsync(req);
                result.Code = (int)msg.StatusCode;
                result.RefCode = (int)msg.StatusCode;

                GetHeaders(ref result, msg);

                if (binaryMode)
                {
                    result.Data = await msg.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    result.Text = await msg.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [HTTP-POST] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// HTTP-POST方法(包含body数据)
        /// </summary>
        /// <param name="url">请求目标URL</param>
        /// <param name="data">主体数据(字节数据)</param>
        /// <param name="token">令牌(凭证)[可选->设置为null]</param>
        /// <param name="binaryMode">是否以二进制模式读取响应内容(默认:否，即表示以文本方式读取)</param>
        /// <returns>HTTP-POST的响应结果</returns>
        public async Task<HttpResult> PostDataAsync(string url, byte[] data, string token, bool binaryMode = false)
        {
            HttpResult result = new HttpResult();

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("User-Agent", userAgent);
                if (!string.IsNullOrEmpty(token))
                {
                    req.Headers.Add("Authorization", token);
                }

                var content = new ByteArrayContent(data);
                req.Content = content;
				req.Content.Headers.ContentType = new MediaTypeHeaderValue(ContentType.APPLICATION_OCTET_STREAM);

                var msg = await client.SendAsync(req);
                result.Code = (int)msg.StatusCode;
                result.RefCode = (int)msg.StatusCode;

                GetHeaders(ref result, msg);

                if (binaryMode)
                {
                    result.Data = await msg.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    result.Text = await msg.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [HTTP-POST] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();
                

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }


        /// <summary>
        /// HTTP-POST方法(包含body数据)
        /// </summary>
        /// <param name="url">请求目标URL</param>
        /// <param name="data">主体数据(字节数据)</param>
        /// <param name="mimeType">主体数据内容类型</param>
        /// <param name="token">令牌(凭证)[可选]</param>
        /// <param name="binaryMode">是否以二进制模式读取响应内容(默认:否，即表示以文本方式读取)</param>
        /// <returns>HTTP-POST的响应结果</returns>
        public async Task<HttpResult> PostDataAsync(string url, byte[] data, string mimeType, string token, bool binaryMode = false)
        {
            HttpResult result = new HttpResult();

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("User-Agent", userAgent);
                if (!string.IsNullOrEmpty(token))
                {
                    req.Headers.Add("Authorization", token);
                }

                var content = new ByteArrayContent(data);
                req.Content = content;
                req.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

                var msg = await client.SendAsync(req);
                result.Code = (int)msg.StatusCode;
                result.RefCode = (int)msg.StatusCode;

                GetHeaders(ref result, msg);

                if (binaryMode)
                {
                    result.Data = await msg.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    result.Text = await msg.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] Post-data Error:  ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// HTTP-POST方法(包含JSON文本的body数据)
        /// </summary>
        /// <param name="url">请求目标URL</param>
        /// <param name="data">主体数据(JSON文本)</param>
        /// <param name="token">令牌(凭证)[可选]</param>
        /// <param name="binaryMode">是否以二进制模式读取响应内容(默认:否，即表示以文本方式读取)</param>
        /// <returns>HTTP-POST的响应结果</returns>
        public async Task<HttpResult> PostJsonAsync(string url, string data, string token, bool binaryMode = false)
        {
            HttpResult result = new HttpResult();

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("User-Agent", userAgent);
                if (!string.IsNullOrEmpty(token))
                {
                    req.Headers.Add("Authorization", token);
                }

                var content = new StringContent(data);
                req.Content = content;
                req.Content.Headers.ContentType = new MediaTypeHeaderValue(ContentType.APPLICATION_JSON);

                var msg = await client.SendAsync(req);
                result.Code = (int)msg.StatusCode;
                result.RefCode = (int)msg.StatusCode;

                GetHeaders(ref result, msg);

                if (binaryMode)
                {
                    result.Data = await msg.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    result.Text = await msg.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [HTTP-POST] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }


        /// <summary>
        /// HTTP-POST方法(包含普通文本的body数据)
        /// </summary>
        /// <param name="url">请求目标URL</param>
        /// <param name="data">主体数据(普通文本)</param>
        /// <param name="token">令牌(凭证)[可选->设置为null]</param>
        /// <param name="binaryMode">是否以二进制模式读取响应内容(默认:否，即表示以文本方式读取)</param>
        /// <returns>HTTP-POST的响应结果</returns>
        public async Task<HttpResult> PostTextAsync(string url, string data, string token, bool binaryMode = false)
        {
            HttpResult result = new HttpResult();

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("User-Agent", userAgent);
                if (!string.IsNullOrEmpty(token))
                {
                    req.Headers.Add("Authorization", token);
                }

                var content = new StringContent(data);
                req.Content = content;
                req.Content.Headers.ContentType = new MediaTypeHeaderValue(ContentType.TEXT_PLAIN);

                var msg = await client.SendAsync(req);
                result.Code = (int)msg.StatusCode;
                result.RefCode = (int)msg.StatusCode;

                GetHeaders(ref result, msg);

                if (binaryMode)
                {
                    result.Data = await msg.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    result.Text = await msg.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] Post-json Error:  ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// HTTP-POST方法(包含表单数据)
        /// </summary>
        /// <param name="url">请求目标URL</param>
        /// <param name="kvData">键值对数据</param>
        /// <param name="token">令牌(凭证)[可选->设置为null]</param>
        /// <param name="binaryMode">是否以二进制模式读取响应内容(默认:否，即表示以文本方式读取)</param>
        /// <returns>HTTP-POST的响应结果</returns>
        public async Task<HttpResult> PostFormAsync(string url, Dictionary<string, string> kvData, string token, bool binaryMode = false)
        {
            HttpResult result = new HttpResult();

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("User-Agent", userAgent);
                if (!string.IsNullOrEmpty(token))
                {
                    req.Headers.Add("Authorization", token);
                }

                var content = new FormUrlEncodedContent(kvData);
                req.Content = content;
				req.Content.Headers.ContentType = new MediaTypeHeaderValue(ContentType.WWW_FORM_URLENC);

                var msg = await client.SendAsync(req);
                result.Code = (int)msg.StatusCode;
                result.RefCode = (int)msg.StatusCode;

                GetHeaders(ref result, msg);

                if (binaryMode)
                {
                    result.Data = await msg.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    result.Text = await msg.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] Post-form Error:  ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// HTTP-POST方法(包含表单数据)
        /// </summary>
        /// <param name="url">请求目标URL</param>
        /// <param name="data">表单数据</param>
        /// <param name="token">令牌(凭证)[可选->设置为null]</param>
        /// <param name="binaryMode">是否以二进制模式读取响应内容(默认:否，即表示以文本方式读取)</param>
        /// <returns>HTTP-POST的响应结果</returns>
        public async Task<HttpResult> PostFormAsync(string url, string data, string token, bool binaryMode = false)
        {
            HttpResult result = new HttpResult();

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("User-Agent", userAgent);
                if (!string.IsNullOrEmpty(token))
                {
                    req.Headers.Add("Authorization", token);
                }

                var content = new StringContent(data);
                req.Content = content;
                req.Content.Headers.ContentType = new MediaTypeHeaderValue(ContentType.WWW_FORM_URLENC);

                var msg = await client.SendAsync(req);
                result.Code = (int)msg.StatusCode;
                result.RefCode = (int)msg.StatusCode;

                GetHeaders(ref result, msg);

                if (binaryMode)
                {
                    result.Data = await msg.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    result.Text = await msg.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [HTTP-POST] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// HTTP-POST方法(包含表单数据)
        /// </summary>
        /// <param name="url">请求目标URL</param>
        /// <param name="data">表单数据</param>
        /// <param name="token">令牌(凭证)[可选->设置为null]</param>
        /// <param name="binaryMode">是否以二进制模式读取响应内容(默认:否，即表示以文本方式读取)</param>
        /// <returns>HTTP-POST的响应结果</returns>
        public async Task<HttpResult> PostFormAsync(string url, byte[] data, string token, bool binaryMode = false)
        {
            HttpResult result = new HttpResult();

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("User-Agent", userAgent);
                if (!string.IsNullOrEmpty(token))
                {
                    req.Headers.Add("Authorization", token);
                }

                var content = new ByteArrayContent(data);
                req.Content = content;
				req.Content.Headers.ContentType = new MediaTypeHeaderValue(ContentType.WWW_FORM_URLENC);				
				
                var msg = await client.SendAsync(req);
                result.Code = (int)msg.StatusCode;
                result.RefCode = (int)msg.StatusCode;

                GetHeaders(ref result, msg);

                if (binaryMode)
                {
                    result.Data = await msg.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    result.Text = await msg.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [HTTP-POST] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// HTTP-POST方法(包含多分部数据,multipart/form-data)
        /// </summary>
        /// <param name="url">请求目标URL</param>
        /// <param name="data">主体数据</param>
        /// <param name="boundary">分界标志</param>
        /// <param name="token">令牌(凭证)[可选->设置为null]</param>
        /// <param name="binaryMode">是否以二进制模式读取响应内容(默认:否，即表示以文本方式读取)</param>
        /// <returns>HTTP-POST的响应结果</returns>
        public async Task<HttpResult> PostMultipartAsync(string url, byte[] data, string boundary, string token, bool binaryMode = false)
        {
            HttpResult result = new HttpResult();

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);

                if (!string.IsNullOrEmpty(token))
                {
                    req.Headers.Add("Authorization", token);
                }
                req.Headers.Add("User-Agent", userAgent);

                var content = new ByteArrayContent(data);
                req.Content = content;
				string ct = string.Format("{0}; boundary={1}", ContentType.MULTIPART_FORM_DATA, boundary);
                req.Content.Headers.Add("Content-Type", ct);
                
                var msg = await client.SendAsync(req);
                result.Code = (int)msg.StatusCode;
                result.RefCode = (int)msg.StatusCode;

                GetHeaders(ref result, msg);

                if (binaryMode)
                {
                    result.Data = await msg.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    result.Text = await msg.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [HTTP-POST] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// 获取返回信息头
        /// </summary>
        /// <param name="hr">即将被HTTP请求封装函数返回的HttpResult变量</param>
        /// <param name="msg">正在被读取的HTTP响应</param>
        private void GetHeaders(ref HttpResult hr, HttpResponseMessage msg)
        {
            if (msg != null)
            {
                var ch = msg.Content.Headers;
                if (ch != null)
                {
                    if (hr.RefInfo == null)
                    {
                        hr.RefInfo = new Dictionary<string, string>();
                    }

                    foreach (var d in ch)
                    {
                        string key = d.Key;
                        StringBuilder val = new StringBuilder();
                        foreach (var v in d.Value)
                        {
                            if (!string.IsNullOrEmpty(v))
                            {
                                val.AppendFormat("{0}; ", v);
                            }
                        }
                        string vs = val.ToString().TrimEnd(';', ' ');
                        if (!string.IsNullOrEmpty(vs))
                        {
                            hr.RefInfo.Add(key, vs);
                        }
                    }
                }

                var hh = msg.Headers;
                if (hh != null)
                {
                    if (hr.RefInfo == null)
                    {
                        hr.RefInfo = new Dictionary<string, string>();
                    }

                    foreach (var d in hh)
                    {
                        string key = d.Key;
                        StringBuilder val = new StringBuilder();
                        foreach (var v in d.Value)
                        {
                            if (!string.IsNullOrEmpty(v))
                            {
                                val.AppendFormat("{0}; ", v);
                            }
                        }
                        string vs = val.ToString().TrimEnd(';', ' ');
                        if (!string.IsNullOrEmpty(vs))
                        {
                            hr.RefInfo.Add(key, vs);
                        }
                    }
                }                
            }
        }

    }
}