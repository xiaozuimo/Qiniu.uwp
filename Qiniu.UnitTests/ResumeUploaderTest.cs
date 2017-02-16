using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
using Qiniu.Http;
using Qiniu.IO;
using Qiniu.IO.Model;
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
    [TestClass]
    public class ResumeUploaderTest : QiniuTestEnvars
    {
        [TestMethod]
        public async Task UploadFileTest()
        {
            Mac mac = new Mac(AccessKey, SecretKey);
            string key = FileKey2;

            PutPolicy putPolicy = new PutPolicy();
            putPolicy.Scope = Bucket1 + ":" + key;
            putPolicy.SetExpires(3600);
            putPolicy.DeleteAfterDays = 1;
            string token = Auth.CreateUploadToken(mac, putPolicy.ToJsonString());

            ResumableUploader target = new ResumableUploader();
            HttpResult result = await target.UploadFileAsync(LocalStorageFile2, key, token);

            Assert.AreEqual((int)HttpCode.OK, result.Code);

        }

        [TestMethod]
        public async Task UploadDataTest()
        {
            Mac mac = new Mac(AccessKey, SecretKey);
            byte[] data = await FormUploader.ReadToByteArrayAsync(LocalStorageFile2);
            string key = FileKey2;

            PutPolicy putPolicy = new PutPolicy();
            putPolicy.Scope = Bucket1 + ":" + key;
            putPolicy.SetExpires(3600);
            putPolicy.DeleteAfterDays = 1;
            string token = Auth.CreateUploadToken(mac, putPolicy.ToJsonString());

            ResumableUploader target = new ResumableUploader();
            HttpResult result = await target.UploadDataAsync(data, key, token, null);
            Assert.AreEqual((int)HttpCode.OK, result.Code);

        }

        [TestMethod]
        public async Task UploadStreamTest()
        {
            Mac mac = new Mac(AccessKey, SecretKey);
            string key = FileKey2;
            
            Stream fs = (await LocalStorageFile2.OpenReadAsync()).AsStream();

            PutPolicy putPolicy = new PutPolicy();
            putPolicy.Scope = Bucket1 + ":" + key;
            putPolicy.SetExpires(3600);
            putPolicy.DeleteAfterDays = 1;
            string token = Auth.CreateUploadToken(mac, putPolicy.ToJsonString());

            ResumableUploader target = new ResumableUploader();
            HttpResult result = await target.UploadStreamAsync(fs, key, token, null);
            Assert.AreEqual((int)HttpCode.OK, result.Code);

        }
    }
}
