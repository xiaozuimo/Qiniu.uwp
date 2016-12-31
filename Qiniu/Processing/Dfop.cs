using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Qiniu.Util;
using Qiniu.Http;
using System.Threading.Tasks;

namespace Qiniu.Processing
{
    public class Dfop
    {
        private HttpManager mHttpManager;
        public Mac mac { set; get; }

        private const string API_HOST = "http://api.qiniu.com";

        public Dfop(Mac mac)
        {
            this.mHttpManager = new HttpManager();
            this.mac = mac;
        }

        public async Task<DfopResult> DfopAsync(string fop,string url)
        {
            DfopResult dfopResult = new DfopResult();

            string encodedUrl = StringUtils.Urlencode(url);

            string dfopUrl = string.Format("{0}/dfop?fop={1}&url={2}", API_HOST, fop, encodedUrl);
            string token = Auth.CreateManageToken(dfopUrl, null, mac);

            Dictionary<string, string> dfopHeaders = new Dictionary<string, string>();
            dfopHeaders.Add("Authorization", token);

            RecvDataHandler dfopRecvDataHandler = new RecvDataHandler(delegate (ResponseInfo respInfo, byte[] respData)
            {
                dfopResult.ResponseInfo = respInfo;
                dfopResult.ResponseData = respData;
            });

            await mHttpManager.PostFormRawAsync(dfopUrl, dfopHeaders, dfopRecvDataHandler);

            return dfopResult;
        }

        public async Task<DfopResult> DfopAsync(string fop, byte[] data)
        {
            DfopResult dfopResult = new DfopResult();
            HttpFormFile dfopData = HttpFormFile.NewFileFromBytes("netFx.png", "application/octet-stream", data);

            string dfopUrl = string.Format("{0}/dfop?fop={1}", API_HOST, fop);
            string token = Auth.CreateManageToken(dfopUrl, null, mac);

            Dictionary<string, string> dfopHeaders = new Dictionary<string, string>();
            dfopHeaders.Add("Authorization", token);            

            RecvDataHandler dfopRecvDataHandler = new RecvDataHandler(delegate (ResponseInfo respInfo, byte[] respData)
            {
                dfopResult.ResponseInfo = respInfo;
                dfopResult.ResponseData = respData;
            });

            await mHttpManager.PostMultipartDataRawAsync(dfopUrl, dfopHeaders, dfopData, null, dfopRecvDataHandler);

            return dfopResult;
        }

    }
}
