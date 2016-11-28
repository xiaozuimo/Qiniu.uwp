﻿using Qiniu.Common;
using Qiniu.Http;
using Qiniu.Storage.Persistent;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace Qiniu.Storage
{
    /// <summary>
    /// 上传管理器，可以通过该上传管理器自动判断上传的内容是
    /// 采用表单上传还是分片上传。
    /// 
    /// 对于二进制数据和文件流，目前仅支持表单上传
    /// 对于沙盒文件，目前支持以表单方式和分片方式上传
    /// </summary>
    public class UploadManager
    {
        private ResumeRecorder resumeRecorder;
        private KeyGenerator keyGenerator;

        /// <summary>
        /// 默认上传管理器
        /// </summary>
        public UploadManager()
        {
            this.resumeRecorder = null;
            this.keyGenerator = null;
        }

        /// <summary>
        /// 以指定的分片上传进度记录器和分片上传记录文件名构建上传管理器
        /// 
        /// 可以指定这两个参数来使分片上传支持断点续传功能
        /// </summary>
        /// <param name="recorder">分片上传进度记录器</param>
        /// <param name="generator">分片上传进度记录文件名</param>
        public UploadManager(ResumeRecorder recorder, KeyGenerator generator)
        {
            this.resumeRecorder = recorder;
            this.keyGenerator = generator;
        }


        /// <summary>
        /// 上传字节数据
        /// </summary>
        /// <param name="data">二进制数据</param>
        /// <param name="key">保存在七牛的文件名</param>
        /// <param name="token">上传凭证</param>
        /// <param name="uploadOptions">上传可选设置</param>
        /// <param name="upCompletionHandler">上传结果处理器</param>
        #region 上传字节数据
        public async Task UploadDataAsync(byte[] data, string key,
            string token, UploadOptions uploadOptions, UpCompletionHandler upCompletionHandler)
        {
            await new FormUploader().UploadDataAsync(data, key, token, uploadOptions, upCompletionHandler);
        }
        #endregion

        /// <summary>
        /// 上传文件流
        /// </summary>
        /// <param name="stream">文件流对象</param>
        /// <param name="key">保存在七牛的文件名</param>
        /// <param name="token">上传凭证</param>
        /// <param name="uploadOptions">上传可选设置</param>
        /// <param name="upCompletionHandler">上传结果处理器</param>
        #region 上传文件流
        public async Task UploadStreamAsync(Stream stream, string key, string token,
            UploadOptions uploadOptions, UpCompletionHandler upCompletionHandler)
        {
            long fileSize = stream.Length;
            if (fileSize <= Config.PUT_THRESHOLD)
            {
                await new FormUploader().UploadStreamAsync(stream, key, token, uploadOptions, upCompletionHandler);
            }
            else
            {
                string recorderKey = null;
                if (this.keyGenerator != null)
                {
                    recorderKey = this.keyGenerator();
                }
                await new ResumeUploader(this.resumeRecorder, recorderKey, stream, key, token, uploadOptions, upCompletionHandler).UploadStreamAsync();
            }
        }
        #endregion

        /// <summary>
        /// 上传沙盒文件
        /// </summary>
        /// <param name="file">沙盒文件全路径</param>
        /// <param name="key">保存在七牛的文件名</param>
        /// <param name="token">上传凭证</param>
        /// <param name="uploadOptions">上传可选设置</param>
        /// <param name="upCompletionHandler">上传结果处理器</param>
        #region 上传文件
        public async Task UploadFileAsync(StorageFile file, string key, string token,
            UploadOptions uploadOptions, UpCompletionHandler upCompletionHandler)
        {
            try
            {
                long fileSize = 0;
                var info = await file.GetBasicPropertiesAsync();
                fileSize = (long)info.Size;
                
                //判断文件大小，选择上传方式
                if (fileSize <= Config.PUT_THRESHOLD)
                {
                    await new FormUploader().UploadFileAsync(file, key, token, uploadOptions, upCompletionHandler);
                }
                else
                {
                    string recorderKey = null;
                    if (this.keyGenerator != null)
                    {
                        recorderKey = this.keyGenerator();
                    }
                    await new ResumeUploader(this.resumeRecorder, recorderKey, file, key, token, uploadOptions, upCompletionHandler).UploadFileAsync();
                }
            }
            catch (Exception ex)
            {
                if (upCompletionHandler != null)
                {
                    upCompletionHandler(key, ResponseInfo.fileError(ex), null);
                }
            }
        }
        #endregion
    }
}