﻿using System.Text;

namespace Qiniu.CDN.Model
{
    /// <summary>
    /// 查询日志-请求
    /// </summary>
    public class LogListRequest
    {
        /// <summary>
        /// 日期，例如 2016-09-01
        /// </summary>
        public string Day { get; set; }

        /// <summary>
        /// 域名列表，以西文半角分号分割
        /// </summary>
        public string Domains { get; set; }

        /// <summary>
        /// 初始化(所有成员为空，需要后续赋值)
        /// </summary>
        public LogListRequest()
        {
            Day = "";
            Domains = "";
        }

        /// <summary>
        /// 初始化所有成员
        /// </summary>
        /// <param name="day">日期</param>
        /// <param name="domains">域名列表</param>
        public LogListRequest(string day,string domains)
        {
            Day = day;
            Domains = domains;
        }

        /// <summary>
        /// 转换到JSON字符串
        /// </summary>
        /// <returns>请求内容的JSON字符串</returns>
        public string ToJsonStr()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{ ");
            sb.AppendFormat("\"day\":\"{0}\", ", Day);
            sb.AppendFormat("\"domains\":\"{0}\"", Domains);
            sb.Append(" }");

            return sb.ToString();
        }
    }
}
