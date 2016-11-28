using Newtonsoft.Json;
using Qiniu.Common;
using Qiniu.Http;
using Qiniu.Storage.Model;
using Qiniu.Util;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Qiniu.Storage
{
    public class BucketManager
    {
        private Mac mac;
        private HttpManager mHttpManager;

        public BucketManager(Mac mac)
        {
            this.mac = mac;
            this.mHttpManager = new HttpManager();
        }

        public async Task<StatResult> StatAsync(string bucket, string key)
        {
            StatResult statResult = null;
            string statUrl = string.Format("{0}{1}", Config.ZONE.RsHost, StatOp(bucket, key));
            string accessToken = Auth.CreateManageToken(statUrl, null, this.mac);

            Dictionary<string, string> statHeaders = new Dictionary<string, string>();
            statHeaders.Add("Authorization", accessToken);
            CompletionHandler statCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                if (respInfo.IsOk())
                {
                    statResult = StringUtils.JsonDecode<StatResult>(response);
                }
                else
                {
                    statResult = new StatResult();
                }
                statResult.Response = response;
                statResult.ResponseInfo = respInfo;
            });

            await this.mHttpManager.GetAsync(statUrl, statHeaders, statCompletionHandler);
            return statResult;
        }

        public async Task<HttpResult> DeleteAsync(string bucket, string key)
        {
            HttpResult deleteResult = null;
            string deleteUrl = string.Format("{0}{1}", Config.ZONE.RsHost, DeleteOp(bucket, key));
            string accessToken = Auth.CreateManageToken(deleteUrl, null, this.mac);
            Dictionary<string, string> deleteHeaders = new Dictionary<string, string>();
            deleteHeaders.Add("Authorization", accessToken);
            CompletionHandler deleteCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                deleteResult = new HttpResult();
                deleteResult.Response = response;
                deleteResult.ResponseInfo = respInfo;
            });

            await this.mHttpManager.PostFormAsync(deleteUrl, deleteHeaders, null, deleteCompletionHandler);
            return deleteResult;
        }

        public async Task<HttpResult> CopyAsync(string srcBucket, string srcKey, string destBucket, string destKey)
        {
            HttpResult copyResult = null;
            string copyUrl = string.Format("{0}{1}", Config.ZONE.RsHost, CopyOp(srcBucket, srcKey, destBucket, destKey));
            string accessToken = Auth.CreateManageToken(copyUrl, null, this.mac);
            Dictionary<string, string> copyHeaders = new Dictionary<string, string>();
            copyHeaders.Add("Authorization", accessToken);
            CompletionHandler copyCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                copyResult = new HttpResult();
                copyResult.Response = response;
                copyResult.ResponseInfo = respInfo;
            });

            await this.mHttpManager.PostFormAsync(copyUrl, copyHeaders, null, copyCompletionHandler);
            return copyResult;
        }

        // ADD 'force' param
        // 2016-08-22 14:58 @fengyh
        public async Task<HttpResult> CopyAsync(string srcBucket, string srcKey, string destBucket, string destKey, bool force)
        {
            HttpResult copyResult = null;
            string copyUrl = string.Format("{0}{1}", Config.ZONE.RsHost, CopyOp(srcBucket, srcKey, destBucket, destKey, force));
            string accessToken = Auth.CreateManageToken(copyUrl, null, this.mac);
            Dictionary<string, string> copyHeaders = new Dictionary<string, string>();
            copyHeaders.Add("Authorization", accessToken);
            CompletionHandler copyCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                copyResult = new HttpResult();
                copyResult.Response = response;
                copyResult.ResponseInfo = respInfo;
            });

            await this.mHttpManager.PostFormAsync(copyUrl, copyHeaders, null, copyCompletionHandler);
            return copyResult;
        }

        public async Task<HttpResult> MoveAsync(string srcBucket, string srcKey, string destBucket, string destKey)
        {
            HttpResult moveResult = null;
            string moveUrl = string.Format("{0}{1}", Config.ZONE.RsHost, MoveOp(srcBucket, srcKey, destBucket, destKey));
            string accessToken = Auth.CreateManageToken(moveUrl, null, this.mac);
            Dictionary<string, string> moveHeaders = new Dictionary<string, string>();
            moveHeaders.Add("Authorization", accessToken);
            CompletionHandler moveCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                moveResult = new HttpResult();
                moveResult.Response = response;
                moveResult.ResponseInfo = respInfo;
            });

            await this.mHttpManager.PostFormAsync(moveUrl, moveHeaders, null, moveCompletionHandler);
            return moveResult;
        }

        // ADD 'force' param
        // 2016-08-22 14:58 @fengyh
        public async Task<HttpResult> MoveAsync(string srcBucket, string srcKey, string destBucket, string destKey, bool force)
        {
            HttpResult moveResult = null;
            string moveUrl = string.Format("{0}{1}", Config.ZONE.RsHost, MoveOp(srcBucket, srcKey, destBucket, destKey, force));
            string accessToken = Auth.CreateManageToken(moveUrl, null, this.mac);
            Dictionary<string, string> moveHeaders = new Dictionary<string, string>();
            moveHeaders.Add("Authorization", accessToken);
            CompletionHandler moveCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                moveResult = new HttpResult();
                moveResult.Response = response;
                moveResult.ResponseInfo = respInfo;
            });

            await this.mHttpManager.PostFormAsync(moveUrl, moveHeaders, null, moveCompletionHandler);
            return moveResult;
        }

        public async Task<HttpResult> ChgmAsync(string bucket, string key, string mimeType)
        {
            HttpResult chgmResult = null;
            string chgmUrl = string.Format("{0}{1}", Config.ZONE.RsHost, ChgmOp(bucket, key, mimeType));
            string accessToken = Auth.CreateManageToken(chgmUrl, null, this.mac);
            Dictionary<string, string> chgmHeaders = new Dictionary<string, string>();
            chgmHeaders.Add("Authorization", accessToken);
            CompletionHandler chgmCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                chgmResult = new HttpResult();
                chgmResult.Response = response;
                chgmResult.ResponseInfo = respInfo;
            });

            await this.mHttpManager.PostFormAsync(chgmUrl, chgmHeaders, null, chgmCompletionHandler);
            return chgmResult;
        }

        public async Task<HttpResult> BatchAsync(string ops)
        {
            HttpResult batchResult = null;
            string batchUrl = string.Format("{0}{1}", Config.ZONE.RsHost, "/batch");
            string accessToken = Auth.CreateManageToken(batchUrl, Encoding.UTF8.GetBytes(ops), this.mac);
            Dictionary<string, string> batchHeaders = new Dictionary<string, string>();
            batchHeaders.Add("Authorization", accessToken);
            CompletionHandler batchCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                batchResult = new FetchResult();
                batchResult.Response = response;
                batchResult.ResponseInfo = respInfo;
            });


            await this.mHttpManager.PostDataAsync(batchUrl, batchHeaders, Encoding.UTF8.GetBytes(ops),
                HttpManager.FORM_MIME_URLENCODED, batchCompletionHandler);
            return batchResult;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="ops"></param>
        /// <returns></returns>
        public async Task<HttpResult> BatchAsync(string[] ops)
        {
            HttpResult batchResult = null;
            string batchUrl = string.Format("{0}{1}", Config.ZONE.RsHost, "/batch");

            StringBuilder opsBuilder = new StringBuilder();
            for (int i = 0; i < ops.Length; i++)
            {
                opsBuilder.AppendFormat("op={0}", ops[i]);
                if (i < ops.Length - 1)
                {
                    opsBuilder.Append("&");
                }
            }
            string accessToken = Auth.CreateManageToken(batchUrl, Encoding.UTF8.GetBytes(opsBuilder.ToString()), this.mac);
            Dictionary<string, string> batchHeaders = new Dictionary<string, string>();
            batchHeaders.Add("Authorization", accessToken);
            CompletionHandler batchCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                batchResult = new FetchResult();
                batchResult.Response = response;
                batchResult.ResponseInfo = respInfo;
            });

            Dictionary<string, string[]> batchParams = new Dictionary<string, string[]>();
            batchParams.Add("op", ops);
            await this.mHttpManager.PostFormAsync(batchUrl, batchHeaders, batchParams, batchCompletionHandler);
            return batchResult;
        }

        public async Task<FetchResult> FetchAsync(string remoteResUrl, string bucket, string key)
        {
            FetchResult fetchResult = null;
            string fetchUrl = string.Format("{0}{1}", Config.ZONE.IovipHost, FetchOp(remoteResUrl, bucket, key));
            string accessToken = Auth.CreateManageToken(fetchUrl, null, this.mac);
            Dictionary<string, string> fetchHeaders = new Dictionary<string, string>();
            fetchHeaders.Add("Authorization", accessToken);
            CompletionHandler fetchCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                if (respInfo.IsOk())
                {
                    fetchResult = StringUtils.JsonDecode<FetchResult>(response);
                }
                else
                {
                    fetchResult = new FetchResult();
                }
                fetchResult.Response = response;
                fetchResult.ResponseInfo = respInfo;
            });

            await this.mHttpManager.PostFormAsync(fetchUrl, fetchHeaders, null, fetchCompletionHandler);
            return fetchResult;
        }

        public async Task<HttpResult> PrefetchAsync(string bucket, string key)
        {
            HttpResult prefetchResult = null;
            string prefetchUrl = string.Format("{0}{1}", Config.ZONE.IovipHost, PrefetchOp(bucket, key));
            string accessToken = Auth.CreateManageToken(prefetchUrl, null, this.mac);
            Dictionary<string, string> prefetchHeaders = new Dictionary<string, string>();
            prefetchHeaders.Add("Authorization", accessToken);
            CompletionHandler prefetchCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                prefetchResult = new FetchResult();
                prefetchResult.Response = response;
                prefetchResult.ResponseInfo = respInfo;
            });

            await this.mHttpManager.PostFormAsync(prefetchUrl, prefetchHeaders, null, prefetchCompletionHandler);
            return prefetchResult;
        }

        public async Task<BucketsResult> ListBucketsAsync()
        {
            BucketsResult bucketsResult = null;
            List<string> buckets = new List<string>();
            string bucketsUrl = string.Format("{0}/buckets", Config.ZONE.RsHost);
            string accessToken = Auth.CreateManageToken(bucketsUrl, null, this.mac);
            Dictionary<string, string> bucketsHeaders = new Dictionary<string, string>();
            bucketsHeaders.Add("Authorization", accessToken);
            CompletionHandler bucketsCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                bucketsResult = new BucketsResult();
                bucketsResult.Response = response;
                bucketsResult.ResponseInfo = respInfo;
                if (respInfo.IsOk())
                {
                    buckets = JsonConvert.DeserializeObject<List<string>>(response);
                    bucketsResult.Buckets = buckets;
                }
            });

            await this.mHttpManager.GetAsync(bucketsUrl, bucketsHeaders, bucketsCompletionHandler);
            return bucketsResult;
        }

        public async Task<DomainsResult> ListDomainsAsync(string bucket)
        {
            DomainsResult domainsResult = null;
            List<string> domains = new List<string>();
            string domainsUrl = string.Format("{0}/v6/domain/list", Config.ZONE.ApiHost);
            string postBody = string.Format("tbl={0}", bucket);
            string accessToken = Auth.CreateManageToken(domainsUrl, Encoding.UTF8.GetBytes(postBody), this.mac);

            Dictionary<string, string> domainsHeaders = new Dictionary<string, string>();
            domainsHeaders.Add("Authorization", accessToken);

            CompletionHandler domainsCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                domainsResult = new DomainsResult();
                domainsResult.Response = response;
                domainsResult.ResponseInfo = respInfo;
                if (respInfo.IsOk())
                {
                    domains = JsonConvert.DeserializeObject<List<string>>(response);
                    domainsResult.Domains = domains;
                }
            });

            Dictionary<string, string[]> postParams = new Dictionary<string, string[]>();
            postParams.Add("tbl", new string[] { bucket });
            await this.mHttpManager.PostFormAsync(domainsUrl, domainsHeaders, postParams, domainsCompletionHandler);
            return domainsResult;
        }

        public async Task<CdnRefreshResult> RefreshCdnAsync(List<string> urls, List<string> dirs)
        {
            CdnRefreshResult result = null;
            if (urls == null && dirs == null)
            {
                return result;
            }

            int urlsCount = 0;
            int dirsCount = 0;
            if (urls != null)
            {
                urlsCount = urls.Count;
            }
            if (dirs != null)
            {
                dirsCount = dirs.Count;
            }

            if (urlsCount == 0 && dirsCount == 0)
            {
                return result;
            }

            CdnRefreshRequest req = new CdnRefreshRequest();
            if (urls != null && urls.Count > 0)
            {
                req.Urls = urls;
            }
            if (dirs != null && dirs.Count > 0)
            {
                req.Dirs = dirs;
            }

            string postData = JsonConvert.SerializeObject(req);
            string refreshUrl = string.Format("{0}/refresh", Config.FUSION_API_HOST);
            string accessToken = Auth.CreateManageToken(refreshUrl, null, this.mac);
            Dictionary<string, string> refreshHeaders = new Dictionary<string, string>();
            refreshHeaders.Add("Authorization", accessToken);

            CompletionHandler refreshCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                result = new CdnRefreshResult();
                result.Response = response;
                result.ResponseInfo = respInfo;
                if (respInfo.IsOk())
                {
                    result = JsonConvert.DeserializeObject<CdnRefreshResult>(response);
                }
            });

            await this.mHttpManager.PostDataAsync(refreshUrl, refreshHeaders, Encoding.UTF8.GetBytes(postData),
                HttpManager.FORM_MIME_JSON, refreshCompletionHandler);
            return result;
        }

        /// <summary>
        /// 
        /// 获取空间文件列表 
        /// listFiles(bucket, prefix, marker, limit, delimiter)
        /// 
        /// bucket:    目标空间名称
        /// 
        /// prefix:    返回指定文件名前缀的文件列表(prefix可设为null)
        /// 
        /// marker:    考虑到设置limit后返回的文件列表可能不全(需要重复执行listFiles操作)
        ///            执行listFiles操作时使用marker标记来追加新的结果
        ///            特别注意首次执行listFiles操作时marker为null
        ///            
        /// limit:     每次返回结果所包含的文件总数限制(limit<=1000，建议值100)
        /// 
        /// delimiter: 分隔符，比如-或者/等等，可以模拟作为目录结构(参考下述示例)
        ///            假设指定空间中有2个文件 fakepath/1.txt fakepath/2.txt
        ///            现设置分隔符delimiter = / 得到返回结果items =[]，commonPrefixes = [fakepath/]
        ///            然后调整prefix = fakepath/ delimiter = null 得到所需结果items = [1.txt,2.txt]
        ///            于是可以在本地先创建一个目录fakepath,然后在该目录下写入items中的文件
        ///            
        /// </summary>
        public async Task<ListFilesResult> ListFilesAsync(string bucket,string prefix,string marker,int limit,string delimiter)
        {
            ListFilesResult result = null;

            StringBuilder sb = new StringBuilder("bucket=" + bucket);
            
            if (!string.IsNullOrEmpty(marker))
            {
                sb.Append("&marker=" + marker);
            }

            if(!string.IsNullOrEmpty(prefix))
            {
                sb.Append("&prefix=" + prefix);
            }

            if(!string.IsNullOrEmpty(delimiter))
            {
                sb.Append("&delimiter=" + delimiter);
            }

            if(limit>1000||limit<1)
            {
                sb.Append("&limit=1000");
            }
            else
            {
                sb.Append("&limit=" + limit);
            }

            string listFilesUrl = Config.ZONE.RsfHost + "/list?" + sb.ToString();
            string accessToken = Auth.CreateManageToken(listFilesUrl, null, mac);

            Dictionary<string, string> listFilesHeaders = new Dictionary<string, string>();
            listFilesHeaders.Add("Authorization", accessToken);

            CompletionHandler listFilesCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                result = new ListFilesResult();
                result.Response = response;
                result.ResponseInfo = respInfo;
                if (respInfo.IsOk())
                {
                    ListFilesResponse resp = JsonConvert.DeserializeObject<ListFilesResponse>(response);

                    result.Marker = resp.Marker;
                    result.Items = resp.Items;
                    result.CommonPrefixes = resp.CommonPrefixes;
                }
            });

            await this.mHttpManager.PostFormAsync(listFilesUrl, listFilesHeaders, null, listFilesCompletionHandler);

            return result;
        }

        public string StatOp(string bucket, string key)
        {
            return string.Format("/stat/{0}", StringUtils.EncodedEntry(bucket, key));
        }

        public string DeleteOp(string bucket, string key)
        {
            return string.Format("/delete/{0}", StringUtils.EncodedEntry(bucket, key));
        }

        public string CopyOp(string srcBucket, string srcKey, string destBucket, string destKey)
        {
            return string.Format("/copy/{0}/{1}", StringUtils.EncodedEntry(srcBucket, srcKey),
                StringUtils.EncodedEntry(destBucket, destKey));
        }

        // ADD 'force' param
        // 2016-08-22 14:58 @fengyh
        public string CopyOp(string srcBucket, string srcKey, string destBucket, string destKey, bool force)
        {
            string fx = force ? "force/true" : "force/false";
            return string.Format("/copy/{0}/{1}/{2}", StringUtils.EncodedEntry(srcBucket, srcKey),
                StringUtils.EncodedEntry(destBucket, destKey), fx);
        }

        public string MoveOp(string srcBucket, string srcKey, string destBucket, string destKey)
        {
            return string.Format("/move/{0}/{1}", StringUtils.EncodedEntry(srcBucket, srcKey),
                StringUtils.EncodedEntry(destBucket, destKey));
        }

        // ADD 'force' param
        // 2016-08-22 14:58 @fengyh
        public string MoveOp(string srcBucket, string srcKey, string destBucket, string destKey, bool force)
        {
            string fx = force ? "force/true" : "force/false";
            return string.Format("/move/{0}/{1}/{2}", StringUtils.EncodedEntry(srcBucket, srcKey),
                StringUtils.EncodedEntry(destBucket, destKey), fx);
        }
        public string ChgmOp(string bucket, string key, string mimeType)
        {
            return string.Format("/chgm/{0}/mime/{1}", StringUtils.EncodedEntry(bucket, key),
                StringUtils.UrlSafeBase64Encode(mimeType));
        }

        public string FetchOp(string url, string bucket, string key)
        {
            return string.Format("/fetch/{0}/to/{1}", StringUtils.UrlSafeBase64Encode(url),
                StringUtils.EncodedEntry(bucket, key));
        }

        public string PrefetchOp(string bucket, string key)
        {
            return string.Format("/prefetch/{0}", StringUtils.EncodedEntry(bucket, key));
        }
    }
}
