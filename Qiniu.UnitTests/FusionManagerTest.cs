using System;
using Qiniu.Fusion;
using Qiniu.Fusion.Model;
using Qiniu.Util;
using Qiniu.UnitTests;
using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;

namespace QiniuTest
{
    /// <summary>
    /// Test class of BucketManager
    /// </summary>
    [TestClass]
    public class FusionManagerTest
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
        public async void fusionMgrTest()
        {
            //Settings.load();
            await Settings.LoadFromFile();
            string testResUrl = "http://test.fengyh.cn/qiniu/files/hello.txt";
            Mac mac = new Mac(Settings.AccessKey, Settings.SecretKey);
            FusionManager target = new FusionManager(mac);

            //      
        }
    }
}
