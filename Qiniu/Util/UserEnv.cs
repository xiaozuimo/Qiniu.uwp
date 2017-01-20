using System;
using System.Threading.Tasks;
using Windows.Storage;

namespace Qiniu.Util
{
    /// <summary>
    /// 环境变量-用户路径
    /// </summary>
    public class UserEnv
    {
        /// <summary>
        /// 获取home路径
        /// </summary>
        /// <returns>HOME路径</returns>
        public static async Task<StorageFolder> GetHomeFolderAsync()
        {
            return await ApplicationData.Current.LocalFolder.CreateFolderAsync("YunFan.Qiniu", CreationCollisionOption.OpenIfExists);
        }
    }
}
