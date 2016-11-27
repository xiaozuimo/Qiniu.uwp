using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
using Qiniu.Storage;
using Qiniu.Storage.Persistent;
using Qiniu.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace Qiniu.UnitTests
{
    /// <summary>
    /// Test class of ResumeUploader
    /// </summary>
    [TestClass]
    public class ResumeUploaderTest
    {
        /// <summary>
        /// get/set
        /// </summary>
        public TestContext Instance
        {
            get;
            set;
        }

        /// <summary>
        /// Test method of BucketManager
        /// </summary>
        [TestMethod]
        public async Task resumeUploadTest()
        {
            //Settings.load();
            await Settings.LoadFromFile();
            Mac mac = new Mac(Settings.AccessKey, Settings.SecretKey);

            PutPolicy putPolicy = new PutPolicy();
            putPolicy.Scope = Settings.Bucket;
            putPolicy.SetExpires(3600);
            putPolicy.DeleteAfterDays = 1;
            string token = Auth.createUploadToken(putPolicy, mac);

            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/WhatsNewinCSharp6_high.mp4"));

            var folder = await KnownFolders.MusicLibrary.CreateFolderAsync("q.Upload", CreationCollisionOption.OpenIfExists);

            ResumeRecorder recorder = new ResumeRecorder(folder);
            ResumeUploader target = new ResumeUploader(recorder, "big.rec", file, "WhatsNewinCSharp6_high.mp4", token, null, null);
            await target.uploadFile();
        }
    }
}
