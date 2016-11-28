using Qiniu.Common;
using Qiniu.Http;
using Qiniu.Util;
using System.Threading.Tasks;

namespace Qiniu.Processing
{
    public class Prefop
    {
        private HttpManager mHttpManager;
        public string PersistentId { set; get; }
        public Prefop(string persistentId)
        {
            this.mHttpManager = new HttpManager();
            this.PersistentId = persistentId;
        }

        public async Task<PrefopResult> PrefopAsync()
        {
            PrefopResult prefopResult = null;

            CompletionHandler prefopCompletionHandler = new CompletionHandler(delegate(ResponseInfo respInfo, string response)
            {
                if (respInfo.IsOk())
                {
                    prefopResult = StringUtils.JsonDecode<PrefopResult>(response);
                }
                else
                {
                    prefopResult = new PrefopResult();
                }
                prefopResult.ResponseInfo = respInfo;
                prefopResult.Response = response;
            });
            string qUrl = string.Format(Config.ZONE.ApiHost + "/status/get/prefop?id={0}", this.PersistentId);
            await this.mHttpManager.GetAsync(qUrl, null, prefopCompletionHandler);
            return prefopResult;
        }
    }
}
