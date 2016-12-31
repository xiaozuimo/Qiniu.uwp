using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Qiniu.Util;
using System.Diagnostics;

namespace Qiniu.Fusion.Model
{
    public class HotLinkRequest
    {
        public string RawUrl
        {
            get
            {
                return Host + Path + File + Query;
            }
        }

        public string Host { get; set; }

        public string Path { get; set; }

        public string File { get; set; }

        public string Query { get; set; }

        public string Key { get; set; }

        public string Timestamp { get; set; }

        public HotLinkRequest()
        {
            Host = "";
            Path = "";
            File = "";
            Query = "";
            Key = "";
            Timestamp = "";
        }

        public HotLinkRequest(string url, string key, int expire)
        {
            string host, path, file, query;
            UrlHelper.UrlSplit(url, out host, out path, out file, out query);

            Host = host;
            Path = path;
            File = file;
            Query = query;
            Key = key;

            SetLinkExpire(expire);
        }

        public void SetLinkExpire(int seconds)
        {
            Timestamp = (GetSeconds(DateTime.Now) + seconds).ToString();
            Debug.WriteLine(Timestamp);
        }

        public void SetLinkExpire(DateTime stopAt)
        {
            Timestamp = (GetSeconds(stopAt)).ToString();
        }

        public static long GetSeconds(DateTime time)
        {
            return (time.ToUniversalTime().Ticks - 621355968000000000L) / 10000L / 1000L;
        }
    }
}
