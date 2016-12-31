using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace Qiniu.Util
{
    internal class StorageUtils
    {
        public static async Task<StorageFolder> GetDefulatResumeRecorderFolder()
        {
            return await ApplicationData.Current.LocalFolder.CreateFolderAsync("YunFan.Qiniu", CreationCollisionOption.OpenIfExists);
        }
    }
}
