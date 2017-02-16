﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace Qiniu.UnitTests
{
    /// <summary>
    /// 测试环境变量
    /// </summary>
    public class QiniuTestEnvars
    {
        // AK,SK,bucket,key
        protected static string AccessKey;
        protected static string SecretKey;
        protected static string Bucket1;
        protected static string Bucket2;
        protected static string FileKey1;
        protected static string FileKey2;
        protected static string LocalFile1;  // 本地文件，小文件       
        protected static string LocalFile2;  // 本地文件，大文件
        protected static StorageFile LocalStorageFile1;  // uwp 本地文件，小文件       
        protected static StorageFile LocalStorageFile2;  // uwp 本地文件，大文件
        protected static string TestDomain;
        protected static string TestURL1;
        protected static string TestURL2;

        private static bool loaded = false;

        /// <summary>
        /// 本地测试，从文件载入设置
        /// </summary>
        private void LoadSettings(string cfgFile)
        {
            if (!loaded)
            {
                string[] lines = System.IO.File.ReadAllLines(cfgFile);
                var dict = new System.Collections.Generic.Dictionary<string, string>();
                foreach (var line in lines)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith("#")) continue;

                    var d = line.Split('=');
                    dict.Add(d[0].Trim(), d[1].Trim());
                }

                AccessKey = dict["QINIU_ACCESS_KEY"];
                SecretKey = dict["QINIU_SECRET_KEY"];
                Bucket1 = dict["QINIU_BUCKET_1"];
                Bucket1 = dict["QINIU_BUCKET_2"];
                FileKey1 = dict["FILE_KEY_1"];
                FileKey2 = dict["FILE_KEY_2"];
                LocalFile1 = dict["LOCAL_FILE_1"];
                LocalFile2 = dict["LOCAL_FILE_2"];
                TestDomain = dict["TEST_DOMAIN"];
                TestURL1 = dict["TEST_URL_1"];
                TestURL2 = dict["TEST_URL_2"];

                loaded = true;
            }
        }

        /// <summary>
        /// 自行设置
        /// </summary>
        private async Task ManualSettings()
        {
            if (!loaded)
            {
                AccessKey = "QINIU_ACCESS_KEY";
                SecretKey = "QINIU_SECRET_KEY";
                Bucket1 = "QINIU_BUCKET_1";
                Bucket1 = "QINIU_BUCKET_2";
                FileKey1 = "FILE_KEY_1";
                FileKey2 = "FILE_KEY_2";
                LocalFile1 = "LOCAL_FILE_1";
                LocalFile2 = "LOCAL_FILE_2";
                TestDomain = "TEST_DOMAIN";
                TestURL1 = "TEST_URL_1";
                TestURL2 = "TEST_URL_2";
                
                //AccessKey = "";
                //SecretKey = "";
                //Bucket1 = "uwptest1";
                //Bucket1 = "uwptest2";
                //FileKey1 = "qiniu_uwp_filekey1_StoreLogo.png";
                //FileKey2 = "qiniu_uwp_filekey2_我执.mp3";
                //LocalFile1 = "LOCAL_FILE_1";
                //LocalFile2 = "LOCAL_FILE_2";
                //LocalStorageFile1 = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/StoreLogo.png"));
                //LocalStorageFile2 = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/我执.mp3"));
                //TestDomain = "TEST_DOMAIN";
                //TestURL1 = "http://assets.qiniu.com/v2/qiniu-409x220.png";
                //TestURL2 = "http://assets.qiniu.com/v2/qiniu-blue.png";

                loaded = true;
            }
        }

        /// <summary>
        /// 线上测试，读取EXPORT的设置
        /// </summary>
        private void InitSettings()
        {
            if (!loaded)
            {
                AccessKey = System.Environment.GetEnvironmentVariable("QINIU_ACCESS_KEY");
                SecretKey = System.Environment.GetEnvironmentVariable("QINIU_SECRET_KEY");
                Bucket1 = System.Environment.GetEnvironmentVariable("QINIU_BUCKET_1");
                Bucket1 = System.Environment.GetEnvironmentVariable("QINIU_BUCKET_2");
                FileKey1 = System.Environment.GetEnvironmentVariable("FILE_KEY_1");
                FileKey2 = System.Environment.GetEnvironmentVariable("FILE_KEY_2");
                // 本地文件，小文件
                LocalFile1 = System.Environment.GetEnvironmentVariable("LOCAL_FILE_1");
                // 本地文件，大文件
                LocalFile2 = System.Environment.GetEnvironmentVariable("LOCAL_FILE_2");
                TestDomain = System.Environment.GetEnvironmentVariable("TEST_DOMAIN");
                TestURL1 = System.Environment.GetEnvironmentVariable("TEST_URL_1");
                TestURL2 = System.Environment.GetEnvironmentVariable("TEST_URL_2");

                loaded = true;
            }
        }

        public QiniuTestEnvars()
        {
            ManualSettings().Wait();
        }
    }
}
