using Newtonsoft.Json;
using Qiniu.UnitTests.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace Qiniu.UnitTests
{
    class Settings
    {
        //see ak sk from https://portal.qiniu.com/user/key
        public static string AccessKey;
        public static string SecretKey;
        public static string Bucket;
        private static bool loaded = false;
        private static Uri AuthFile = new Uri("ms-appx:///Assets/Auth/qiniu.json", UriKind.Absolute);

        public static void load()
        {
            if (!loaded)
            {
                AccessKey = "<Your Access Key>";
                SecretKey = "<Your Secret Key>";
                Bucket = "<Your Bucket>";

                loaded = true;
            }
        }

        /// <summary>
        /// 仅在测试时使用
        /// </summary>
        public static async Task LoadFromFile()
        {
            if (!loaded)
            {
                var file = await StorageFile.GetFileFromApplicationUriAsync(AuthFile);
                var str = await FileIO.ReadTextAsync(file);
                var auth = JsonConvert.DeserializeObject<QAuth>(str);
                AccessKey = auth.AccessKey;
                SecretKey = auth.SecretKey;
                Bucket = auth.Bucket;

                loaded = true;
            }
        }
    }
}
