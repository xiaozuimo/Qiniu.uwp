using System;
using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
using Qiniu.Util;
using Qiniu.Storage;
using Qiniu.Storage.Model;
using System.Threading.Tasks;

namespace Qiniu.UnitTests
{
    /// <summary>
    /// Test class of BucketManager
    /// </summary>
    [TestClass]
    public class BucketManagerTest
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
        public async Task bktMgrTest()
        {
            //Settings.load();
            await Settings.LoadFromFile();
            string testResUrl = "http://test.fengyh.cn/qiniu/files/hello.txt";
            Mac mac = new Mac(Settings.AccessKey, Settings.SecretKey);
            BucketManager target = new BucketManager(mac);

            await target.fetch(testResUrl, Settings.Bucket, "test_BucketManager.txt");

            await target.stat(Settings.Bucket, "test_BucketManager.txt");

            await target.copy(Settings.Bucket, "test_BucketManager.txt", Settings.Bucket, "copy_BucketManager.txt", true);

            await target.move(Settings.Bucket, "copy_BucketManager.txt", Settings.Bucket, "move_BucketManager.txt", true);

            await target.delete(Settings.Bucket, "test_BucketManager.txt");

            DomainsResult domainsResult = await target.domains(Settings.Bucket);

            BucketsResult bucketsResult = await target.buckets();
        }
    }
}
