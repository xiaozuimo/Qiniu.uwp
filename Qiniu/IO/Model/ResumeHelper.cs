using System;
using System.IO;
using Newtonsoft.Json;
using Qiniu.Util;
using Windows.Storage;
using System.Threading.Tasks;

namespace Qiniu.IO.Model
{
    /// <summary>
    /// 断点续上传辅助函数Load/Save
    /// </summary>
    public class ResumeHelper
    {
        /// <summary>
        /// 尝试从从文件载入断点信息
        /// </summary>
        /// <param name="recordFile">断点记录文件</param>
        /// <returns>断点信息</returns>
        public static async Task<ResumeInfo> LoadAsync(StorageFile recordFile)
        {
            ResumeInfo resumeInfo = null;

            try
            {
                JsonConvert.DeserializeObject<ResumeInfo>(await FileIO.ReadTextAsync(recordFile));
            }
            catch(Exception)
            {
                resumeInfo = null;
            }

            return resumeInfo;
        }

        /// <summary>
        /// 保存断点信息到文件
        /// </summary>
        /// <param name="resumeInfo">断点信息</param>
        /// <param name="recordFile">断点记录文件</param>
        public static async Task SaveAsync(ResumeInfo resumeInfo, StorageFile recordFile)
        {
            string jsonStr = string.Format("{{\"fileSize\":{0}, \"blockIndex\":{1}, \"blockCount\":{2}, \"contexts\":[{3}]}}",
                resumeInfo.FileSize, resumeInfo.BlockIndex, resumeInfo.BlockCount, StringHelper.JsonJoin(resumeInfo.Contexts));

            await FileIO.WriteTextAsync(recordFile, jsonStr);
        }
    }
}
