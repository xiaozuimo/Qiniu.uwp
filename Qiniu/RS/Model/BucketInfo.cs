using Newtonsoft.Json;

namespace Qiniu.RS.Model
{
    /// <summary>
    /// bucket info
    /// </summary>
    public class BucketInfo
    {
        /// <summary>
        /// bucket name
        /// </summary>
        [JsonProperty("tbl")]
        public string Tbl { get; set; }

        /// <summary>
        /// itbl
        /// </summary>
        [JsonProperty("itbl")]
        public long Itbl { get; set; }

        /// <summary>
        /// deprecated
        /// </summary>
        [JsonProperty("phy")]
        public string Phy {get;set;}

        /// <summary>
        /// id
        /// </summary>
        [JsonProperty("uid")]
        public long Uid { get; set; }

        /// <summary>
        /// zone
        /// </summary>
        [JsonProperty("zone")]
        public string Zone { get; set; }

        /// <summary>
        /// region
        /// </summary>
        [JsonProperty("region")]
        public string Region { get; set; }

        /// <summary>
        /// isGlobal
        /// </summary>
        [JsonProperty("global")]
        public bool Global { get; set; }

        /// <summary>
        /// isLineStorage
        /// </summary>
        [JsonProperty("line")]
        public bool Line { get; set; }

        /// <summary>
        /// creationTime
        /// </summary>
        [JsonProperty("ctime")]
        public long Ctime { get; set; }
    }
}
