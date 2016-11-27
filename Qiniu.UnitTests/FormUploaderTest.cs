using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
using Qiniu.Http;
using Qiniu.Storage;
using Qiniu.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace Qiniu.UnitTests
{
    /// <summary>
    ///This is a test class for FormUploaderTest and is intended
    ///to contain all FormUploaderTest Unit Tests
    ///</summary>
    [TestClass()]
    public class FormUploaderTest
    {
        private TestContext testContextInstance;

        /// <summary>
        ///Gets or sets the test context which provides
        ///information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        #region Additional test attributes
        // 
        //You can use the following additional attributes as you write your tests:
        //
        //Use ClassInitialize to run code before running the first test in the class
        //[ClassInitialize()]
        //public static void MyClassInitialize(TestContext testContext)
        //{
        //}
        //
        //Use ClassCleanup to run code after all tests in a class have run
        //[ClassCleanup()]
        //public static void MyClassCleanup()
        //{
        //}
        //
        //Use TestInitialize to run code before running each test
        //[TestInitialize()]
        //public void MyTestInitialize()
        //{
        //}
        //
        //Use TestCleanup to run code after each test has run
        //[TestCleanup()]
        //public void MyTestCleanup()
        //{
        //}
        //
        #endregion

        [TestInitialize]
        public async Task Initialize()
        {
            await Settings.LoadFromFile();
        }

        /// <summary>
        ///A test for uploadData
        ///</summary>
        [TestMethod()]
        public async Task uploadDataTest()
        {
            Mac mac = new Mac(Settings.AccessKey, Settings.SecretKey);
            FormUploader target = new FormUploader();
            byte[] data = Encoding.UTF8.GetBytes("hello world");
            string key = "test_FormUploaderUploadData.txt";

            PutPolicy putPolicy = new PutPolicy();
            putPolicy.Scope = Settings.Bucket;
            putPolicy.SetExpires(3600);
            putPolicy.DeleteAfterDays = 1;
            string token = Auth.createUploadToken(putPolicy, mac);
            UploadOptions uploadOptions = null;
            UpCompletionHandler upCompletionHandler = new UpCompletionHandler(delegate (string fileKey, ResponseInfo respInfo, string response)
            {
                Assert.AreEqual(200, respInfo.StatusCode);
            });
            await target.uploadData(data, key, token, uploadOptions, upCompletionHandler);
        }

        /// <summary>
        /// A test for uploadFile
        /// 注意如果文件存在则会失败
        ///</summary>
        [TestMethod()]
        public async Task uploadFileTest()
        {
            Mac mac = new Mac(Settings.AccessKey, Settings.SecretKey);
            FormUploader target = new FormUploader();

            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/StoreLogo.png", UriKind.Absolute));
            string key = Path.GetFileName(file.Path);
            PutPolicy putPolicy = new PutPolicy();
            putPolicy.Scope = Settings.Bucket;
            putPolicy.SetExpires(3600);
            putPolicy.DeleteAfterDays = 1;
            string token = Auth.createUploadToken(putPolicy, mac);
            UploadOptions uploadOptions = null;

            UpCompletionHandler upCompletionHandler = new UpCompletionHandler(delegate (string fileKey, ResponseInfo respInfo, string response)
            {
                Assert.AreEqual(200, respInfo.StatusCode);
            });
            await target.uploadFile(file, key, token, uploadOptions, upCompletionHandler);
        }
    }
}
