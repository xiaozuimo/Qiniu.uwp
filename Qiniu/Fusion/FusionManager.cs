using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Qiniu.Util;
using Qiniu.Fusion.Model;
using System.Net;
using Qiniu.Http;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Qiniu.Fusion
{
    public class FusionManager
    {
        private Mac mac;
        private HttpManager httpMgr;

        public FusionManager(Mac mac)
        {
            this.mac = mac;
            httpMgr = new HttpManager();
        }

        private string RefreshUrl()
        {
            return string.Format("{0}/v2/tune/refresh", Common.Config.FUSION_API_HOST);
        }

        private string PrefetchUrl()
        {
            return string.Format("{0}/v2/tune/prefetch", Common.Config.FUSION_API_HOST);
        }

        private string BandwidthUrl()
        {
            return string.Format("{0}/v2/tune/bandwidth", Common.Config.FUSION_API_HOST);
        }

        private string FluxUrl()
        {
            return string.Format("{0}/v2/tune/flux", Common.Config.FUSION_API_HOST);
        }

        private string LoglistUrl()
        {
            return string.Format("{0}/v2/tune/log/list", Common.Config.FUSION_API_HOST);
        }

        /// <summary>
        /// 缓存刷新
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<RefreshResult> RefreshAsync(RefreshRequest request)
        {
            RefreshResult result = new RefreshResult();

            string url = RefreshUrl();
            string body = request.ToJsonStr();
            byte[] data = Encoding.UTF8.GetBytes(body);

            string token = Auth.CreateManageToken(url, null, mac);

            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Authorization", token);

            await httpMgr.PostDataAsync(url, headers, data, HttpManager.FORM_MIME_JSON, 
                new CompletionHandler(delegate(ResponseInfo respInfo,string respJson)
                {
                    if(respInfo.StatusCode!=200)
                    {
                        Debug.WriteLine(respInfo);
                    }

                    result = JsonConvert.DeserializeObject<RefreshResult>(respJson);
                }));


            return result;
        }

        /// <summary>
        /// 文件预取
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<PrefetchResult> PrefetchAsync(PrefetchRequest request)
        {
            PrefetchResult result = new PrefetchResult();

            string url = PrefetchUrl();
            string body = request.ToJsonStr();
            byte[] data = Encoding.UTF8.GetBytes(body);

            string token = Auth.CreateManageToken(url, null, mac);

            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Authorization", token);

            await httpMgr.PostDataAsync(url, headers, data, HttpManager.FORM_MIME_JSON,
                new CompletionHandler(delegate (ResponseInfo respInfo, string respJson)
                {
                    if (respInfo.StatusCode != 200)
                    {
                        Debug.WriteLine(respInfo);
                    }

                    result = JsonConvert.DeserializeObject<PrefetchResult>(respJson);
                }));

            return result;
        }

        /// <summary>
        /// 带宽
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<BandwidthResult> BandwidthAsync(BandwidthRequest request)
        {
            BandwidthResult result = new BandwidthResult();

            string url = BandwidthUrl();
            string body = request.ToJsonStr();
            byte[] data = Encoding.UTF8.GetBytes(body);

            string token = Auth.CreateManageToken(url, null, mac);

            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Authorization", token);

            await httpMgr.PostDataAsync(url, headers, data, HttpManager.FORM_MIME_JSON,
                new CompletionHandler(delegate (ResponseInfo respInfo, string respJson)
                {
                    if (respInfo.StatusCode != 200)
                    {
                        Debug.WriteLine(respInfo);
                    }

                    result = JsonConvert.DeserializeObject<BandwidthResult>(respJson);
                }));

            return result;
        }

        /// <summary>
        /// 流量
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<FluxResult> FluxAsync(FluxRequest request)
        {
            FluxResult result = new FluxResult();

            string url = FluxUrl();
            string body = request.ToJsonStr();
            byte[] data = Encoding.UTF8.GetBytes(body);

            string token = Auth.CreateManageToken(url, null, mac);

            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Authorization", token);

            await httpMgr.PostDataAsync(url, headers, data, HttpManager.FORM_MIME_JSON,
                new CompletionHandler(delegate (ResponseInfo respInfo, string respJson)
                {
                    if (respInfo.StatusCode != 200)
                    {
                        Debug.WriteLine(respInfo);
                    }

                    result = JsonConvert.DeserializeObject<FluxResult>(respJson);
                }));

            return result;
        }

        /// <summary>
        /// 日志列表
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<LogListResult> LogListAsync(LogListRequest request)
        {
            LogListResult result = new LogListResult();

            string url = LoglistUrl();
            string body = request.ToJsonStr();
            byte[] data = Encoding.UTF8.GetBytes(body);

            string token = Auth.CreateManageToken(url, null, mac);

            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Authorization", token);

            await httpMgr.PostDataAsync(url, headers, data, HttpManager.FORM_MIME_JSON,
                new CompletionHandler(delegate (ResponseInfo respInfo, string respJson)
                {                  
                    result = JsonConvert.DeserializeObject<LogListResult>(respJson);
                    result.Code = respInfo.StatusCode;
                    if (respInfo.StatusCode != 200)
                    {
                        Debug.WriteLine(respInfo);
                    }
                }));

            return result;
        }

        /// <summary>
        /// 时间戳防盗链
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public string HotLink(HotLinkRequest request)
        {
            string RAW = request.RawUrl;

            string key = request.Key;
            string path = Uri.EscapeUriString(request.Path);
            string file = request.File;
            string ts = (int.Parse(request.Timestamp)).ToString("x");
            string SIGN = StringUtils.Md5Hash(key + path + file + ts);            

            return string.Format("{0}&sign={1}&t={2}", RAW, SIGN, ts);
        }        
    }
}
