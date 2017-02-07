﻿using System;
using System.Text;
using Qiniu.Common;
using Qiniu.Util;
using Qiniu.Http;
using Qiniu.CDN.Model;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Qiniu.CDN
{
    /// <summary>
    /// 融合CDN加速-功能模块： 缓存刷新、文件预取、流量/带宽查询、日志查询、时间戳防盗链
    /// 另请参阅 http://developer.qiniu.com/article/index.html#fusion-api-handbook
    /// 关于时间戳防盗链可参阅 https://support.qiniu.com/question/195128
    /// </summary>
    public class CdnManager
    {
        private Auth auth;
        private HttpManager httpManager;

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="mac">账号(密钥)</param>
        public CdnManager(Mac mac)
        {
            auth = new Auth(mac);
            httpManager = new HttpManager();
        }

        private string RefreshEntry()
        {
            return string.Format("{0}/v2/tune/refresh", Config.FUSION_API_HOST);
        }

        private string PrefetchEntry()
        {
            return string.Format("{0}/v2/tune/prefetch", Config.FUSION_API_HOST);
        }

        private string BandwidthEntry()
        {
            return string.Format("{0}/v2/tune/bandwidth", Config.FUSION_API_HOST);
        }

        private string FluxEntry()
        {
            return string.Format("{0}/v2/tune/flux", Config.FUSION_API_HOST);
        }

        private string LogListEntry()
        {
            return string.Format("{0}/v2/tune/log/list", Config.FUSION_API_HOST);
        }

        /// <summary>
        /// 缓存刷新-刷新URL和URL目录
        /// </summary>
        /// <param name="request">“缓存刷新”请求，详情请参见该类型的说明</param>
        /// <returns>缓存刷新的结果</returns>
        public async Task<RefreshResult> RefreshUrlsAndDirsAsync(RefreshRequest request)
        {
            RefreshResult result = new RefreshResult();

            try
            {
                string url = RefreshEntry();
                string body = request.ToJsonStr();
                string token = auth.CreateManageToken(url);

                HttpResult hr = await httpManager.PostJsonAsync(url, body, token);
                result.Shadow(hr);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [refresh] Error:  ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
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
        /// 缓存刷新-刷新URL
        /// </summary>
        /// <param name="urls">要刷新的URL列表</param>
        /// <returns>缓存刷新的结果</returns>
        public async Task<RefreshResult> RefreshUrlsAsync(string[] urls)
        {
            RefreshRequest request = new RefreshRequest(urls, null);
            return await RefreshUrlsAndDirsAsync(request);
        }

        /// <summary>
        /// 缓存刷新-刷新URL目录
        /// </summary>
        /// <param name="dirs">要刷新的URL目录列表</param>
        /// <returns>缓存刷新的结果</returns>
        public async Task<RefreshResult> RefreshDirsAsync(string[] dirs)
        {
            RefreshRequest request = new RefreshRequest(null, dirs);
            return await RefreshUrlsAndDirsAsync(request);
        }

        /// <summary>
        /// 缓存刷新-刷新URL和URL目录
        /// </summary>
        /// <param name="urls">要刷新的URL列表</param>
        /// <param name="dirs">要刷新的URL目录列表</param>
        /// <returns>缓存刷新的结果</returns>
        public async Task<RefreshResult> RefreshUrlsAndDirsAsync(string[] urls, string[] dirs)
        {
            RefreshRequest request = new RefreshRequest(urls, dirs);
            return await RefreshUrlsAndDirsAsync(request);
        }

        /// <summary>
        /// 文件预取
        /// </summary>
        /// <param name="request">“文件预取”请求，详情请参阅该类型的说明</param>
        /// <returns>文件预取的结果</returns>
        public async Task<PrefetchResult> PrefetchUrlsAsync(PrefetchRequest request)
        {
            PrefetchResult result = new PrefetchResult();

            try
            {
                string url = PrefetchEntry();
                string body = request.ToJsonStr();
                string token = auth.CreateManageToken(url);

                HttpResult hr = await httpManager.PostJsonAsync(url, body, token);
                result.Shadow(hr);                
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [prefetch] Error:  ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
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
        /// 文件预取
        /// </summary>
        /// <param name="urls">待预取的文件URL列表</param>
        /// <returns>文件预取结果</returns>
        public async Task<PrefetchResult> PrefetchUrlsAsync(string[] urls)
        {
            PrefetchRequest request = new PrefetchRequest(urls);
            return await PrefetchUrlsAsync(request);
        }

        /// <summary>
        /// [异步async]批量查询cdn带宽
        /// </summary>
        /// <param name="request">“带宽查询”请求，详情请参阅该类型的说明</param>
        /// <returns>带宽查询的结果</returns>
        public async Task<BandwidthResult> GetBandwidthDataAsync(BandwidthRequest request)
        {
            BandwidthResult result = new BandwidthResult();

            try
            {
                string url = BandwidthEntry();
                string body = request.ToJsonStr();
                string token = auth.CreateManageToken(url);

                HttpResult hr = await httpManager.PostJsonAsync(url, body, token);
                result.Shadow(hr);                
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [Bandwidth] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
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
        /// [异步async]批量查询cdn带宽
        /// </summary>
        /// <param name="domains">域名列表</param>
        /// <param name="startDate">起始日期，如2017-01-01</param>
        /// <param name="endDate">结束日期，如2017-01-02</param>
        /// <param name="granularity">时间粒度，如day</param>
        /// <returns>带宽查询的结果</returns>
        public async Task<BandwidthResult> GetBandwidthDataAsync(string[] domains,string startDate,string endDate,string granularity)
        {
            BandwidthRequest request = new BandwidthRequest(startDate, endDate, granularity, StringHelper.Join(domains, ";"));
            return await GetBandwidthDataAsync(request);
        }

        /// <summary>
        /// [异步async]批量查询cdn流量
        /// </summary>
        /// <param name="request">“流量查询”请求，详情请参阅该类型的说明</param>
        /// <returns>流量查询的结果</returns>
        public async Task<FluxResult> GetFluxDataAsync(FluxRequest request)
        {
            FluxResult result = new FluxResult();

            try
            {
                string url = FluxEntry();
                string body = request.ToJsonStr();
                string token = auth.CreateManageToken(url);

                HttpResult hr = await httpManager.PostJsonAsync(url, body, token);
                result.Shadow(hr);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder("");
                sb.AppendFormat("[{0}] [Flux] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
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
        /// [异步async]批量查询cdn流量
        /// </summary>
        /// <param name="domains">域名列表</param>
        /// <param name="startDate">起始日期，如2017-01-01</param>
        /// <param name="endDate">结束日期，如2017-01-02</param>
        /// <param name="granularity">时间粒度，如day</param>
        /// <returns>流量查询的结果</returns>
        public async Task<FluxResult> GetFluxDataAsync(string[] domains, string startDate, string endDate, string granularity)
        {
            FluxRequest request = new FluxRequest(startDate, endDate, granularity, StringHelper.Join(domains, ";"));
            return await GetFluxDataAsync(request);
        }

        /// <summary>
        /// [异步async]查询日志列表，获取日志的下载外链
        /// </summary>
        /// <param name="request">“日志查询”请求，详情请参阅该类型的说明</param>
        /// <returns>日志查询的结果</returns>
        public async Task<LogListResult> GetCdnLogListAsync(LogListRequest request)
        {
            LogListResult result = new LogListResult();

            try
            {
                string url = LogListEntry();
                string body = request.ToJsonStr();
                string token = auth.CreateManageToken(url);

                HttpResult hr = await httpManager.PostJsonAsync(url, body, token);
                result.Shadow(hr);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [LogList] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
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
        /// 查询日志列表
        /// </summary>
        /// <param name="domains">域名列表</param>
        /// <param name="date">指定日期，如2017-01-01</param>
        /// <returns>日志列表</returns>
        public async Task<LogListResult> GetCdnLogListAsync(string[] domains,string date)
        {
            LogListRequest request = new LogListRequest(date, StringHelper.Join(domains, ";"));
            return await GetCdnLogListAsync(request);
        }

        /// <summary>
        /// 时间戳防盗链
        /// 另请参阅https://support.qiniu.com/question/195128
        /// </summary>
        /// <param name="request">“时间戳防盗链”请求，详情请参阅该类型的说明</param>
        /// <returns>时间戳防盗链接</returns>
        public string CreateTimestampAntiLeechUrl(TimestampAntiLeechUrlRequest request)
        {
            string RAW = request.RawUrl;

            string key = request.Key;
            string path = Uri.EscapeUriString(request.Path);
            string file = request.File;
            string ts = (int.Parse(request.Timestamp)).ToString("x");
            string SIGN = Hashing.CalcMD5(key + path + file + ts);

            return string.Format("{0}&sign={1}&t={2}", RAW, SIGN, ts);
        }

        /// <summary>
        /// 时间戳防盗链
        /// </summary>
        /// <param name="host">主机，如http://domain.com</param>
        /// <param name="path">路径，如/dir1/dir2/</param>
        /// <param name="fileName">文件名，如1.jpg</param>
        /// <param name="query">请求参数，如?v=1.1</param>
        /// <param name="encryptKey">后台提供的key</param>
        /// <param name="expireInSeconds">链接有效时长</param>
        /// <returns>时间戳防盗链接</returns>
        public string CreateTimestampAntiLeechUrl(string host, string path, string fileName, string query, string encryptKey, int expireInSeconds)
        {
            TimestampAntiLeechUrlRequest request = new TimestampAntiLeechUrlRequest();
            request.Host = host;
            request.Path = path;
            request.File = fileName;
            request.Query = query;
            request.Key = encryptKey;
            request.SetLinkExpire(expireInSeconds);

            return CreateTimestampAntiLeechUrl(request);
        }

    }
}
