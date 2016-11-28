using Newtonsoft.Json;
using System;
using System.Runtime.InteropServices;

namespace Qiniu.Util
{
    [JsonObject(MemberSerialization.OptIn)]
    public class PutPolicy : ICloneable
    {
        [JsonProperty("scope")]
        public string Scope { set; get; }
        [JsonProperty("deadline")]
        public int Deadline { set; get; }
        [JsonProperty("insertOnly")]
        public int? InsertOnly { set; get; }
        [JsonProperty("saveKey")]
        public string SaveKey { set; get; }
        [JsonProperty("endUser")]
        public string EndUser { set; get; }

        [JsonProperty("returnUrl")]
        public string ReturnUrl { set; get; }
        [JsonProperty("returnBody")]
        public string ReturnBody { set; get; }

        [JsonProperty("callbackUrl")]
        public string CallbackUrl { set; get; }
        [JsonProperty("callbackBody")]
        public string CallbackBody { set; get; }
        [JsonProperty("callbackBodyType")]
        public string CallbackBodyType { set; get; }
        [JsonProperty("callbackHost")]
        public string CallbackHost { set; get; }
        [JsonProperty("callbackFetchKey")]
        public int? CallbackFetchKey { set; get; }

        [JsonProperty("persistentOps")]
        public string PersistentOps { set; get; }
        [JsonProperty("persistentNotifyUrl")]
        public string PersistentNotifyUrl { set; get; }
        [JsonProperty("persistentPipeline")]
        public string PersistentPipeline { set; get; }

        [JsonProperty("fsizeLimit")]
        public int? FsizeLimit { set; get; }

        [JsonProperty("fsizeMin")]
        public int? FsizeMin { set; get; }
        [JsonProperty("detectMime")]
        public int? DetectMime { set; get; }
        [JsonProperty("mimeLimit")]
        public string MimeLimit { set; get; }

        [JsonProperty("deleteAfterDays")]
        public int? DeleteAfterDays { set; get; }

        public void SetExpires(int expireInSeconds)
        {
            TimeSpan ts = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0));
            this.Deadline = (int)ts.TotalSeconds + expireInSeconds;
        }
        /// <summary>
        /// 获取此上传策略的Json字符串。
        /// </summary>
        /// <returns>此上传策略的Json字符串。</returns>
        public override string ToString()
        {
            return StringUtils.JsonEncode(this);
        }
        /// <summary>
        /// 获取此上传策略的副本。
        /// </summary>
        /// <returns>上传策略的副本。</returns>
        object ICloneable.Clone()
        {
            return this.Clone();
        }
        /// <summary>
        /// 获取此上传策略的副本。
        /// </summary>
        /// <returns>上传策略的副本。</returns>
        public PutPolicy Clone()
        {
            return this.MemberwiseClone() as PutPolicy;
        }
    }


    //
    // Summary:
    //     Supports cloning, which creates a new instance of a class with the same value
    //     as an existing instance.
    [ComVisible(true)]
    public interface ICloneable
    {
        //
        // Summary:
        //     Creates a new object that is a copy of the current instance.
        //
        // Returns:
        //     A new object that is a copy of this instance.
        object Clone();
    }
}
