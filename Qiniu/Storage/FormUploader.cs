﻿using System.Collections.Generic;
using System.IO;
using Qiniu.Http;
using Qiniu.Common;
using Qiniu.Util;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;

namespace Qiniu.Storage
{
    /// <summary>
    /// 数据或者文件的表单上传方式
    /// </summary>
    public class FormUploader
    {
        private HttpManager mHttpManager;
        public FormUploader()
        {
            this.mHttpManager = new HttpManager();
        }

        public async Task uploadData(byte[] data, int offset, int count, string key, string token,
            UploadOptions uploadOptions, UpCompletionHandler upCompletionHandler)
        {
            HttpFormFile fFile = HttpFormFile.NewFileFromSlice(key, null, data, offset, count);
            await upload(fFile, key, token, uploadOptions, upCompletionHandler);
        }

        /// <summary>
        /// 以表单方式上传字节数据
        /// </summary>
        /// <param name="data">字节数据</param>
        /// <param name="key">保存在七牛的文件名</param>
        /// <param name="token">上传凭证</param>
        /// <param name="uploadOptions">上传可选设置</param>
        /// <param name="upCompletionHandler">上传完成结果处理器</param>
        public async Task uploadData(byte[] data, string key,
            string token, UploadOptions uploadOptions, UpCompletionHandler upCompletionHandler)
        {
            HttpFormFile fFile = HttpFormFile.NewFileFromBytes(key, null, data); 
            // 此处未设置FormFile.ContentType,稍后设置(在upload中已设置) @fengyh 2016-08-17 15:03
            await upload(fFile, key, token, uploadOptions, upCompletionHandler);
        }

        /// <summary>
        /// 以表单方式上传数据流
        /// </summary>
        /// <param name="stream">文件数据流</param>
        /// <param name="key">保存在七牛的文件名</param>
        /// <param name="token">上传凭证</param>
        /// <param name="uploadOptions">上传可选设置</param>
        /// <param name="upCompletionHandler">上传完成结果处理器</param>
        public async Task uploadStream(Stream stream, string key, string token,
            UploadOptions uploadOptions, UpCompletionHandler upCompletionHandler)
        {
            HttpFormFile fFile = HttpFormFile.NewFileFromStream(key, null, stream);
            await upload(fFile, key, token, uploadOptions, upCompletionHandler);
        }

        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="httpManager">HttpManager对象</param>
        /// <param name="file">文件的完整路径</param>
        /// <param name="key">保存在七牛的文件名</param>
        /// <param name="token">上传凭证</param>
        /// <param name="uploadOptions">上传可选设置</param>
        /// <param name="upCompletionHandler">上传完成结果处理器</param>
        public async Task uploadFile(StorageFile file, string key,
            string token, UploadOptions uploadOptions, UpCompletionHandler upCompletionHandler)
        {
            HttpFormFile fFile = HttpFormFile.NewFileFromPath(key, null, file);
            await upload(fFile, key, token, uploadOptions, upCompletionHandler);
        }

        private async Task upload(HttpFormFile fFile, string key, string token,
            UploadOptions uploadOptions, UpCompletionHandler upCompletionHandler)
        {
            // 使用uploadHost -- REMINDME-0
            // 是否使用CDN(默认：是)
            string uploadHost = Config.UploadFromCDN ? Config.ZONE.UploadHost : Config.ZONE.UpHost;

            if (uploadOptions == null)
            {
                uploadOptions = UploadOptions.defaultOptions();
            }
            Dictionary<string, string> vPostParams = new Dictionary<string, string>();
            //设置key
            if (!string.IsNullOrEmpty(key))
            {
                vPostParams.Add("key", key);
            }
            //设置token
            vPostParams.Add("token", token);
            //设置crc32校验
            if (uploadOptions.CheckCrc32)
            {
                switch (fFile.BodyType)
                {
                    case HttpFileType.DATA_SLICE:
                        vPostParams.Add("crc32", string.Format("{0}", CRC32.CheckSumSlice(fFile.BodyBytes, fFile.Offset, fFile.Count)));
                        break;
                    case HttpFileType.DATA_BYTES:
                        vPostParams.Add("crc32", string.Format("{0}", CRC32.CheckSumBytes(fFile.BodyBytes)));
                        break;
                    case HttpFileType.FILE_STREAM:
                        long streamLength = fFile.BodyStream.Length;
                        byte[] buffer = new byte[streamLength];
                        int cnt = fFile.BodyStream.Read(buffer, 0, (int)streamLength);
                        vPostParams.Add("crc32", string.Format("{0}", CRC32.CheckSumSlice(buffer, 0, cnt)));
                        fFile.BodyStream.Seek(0, SeekOrigin.Begin);
                        break;
                    case HttpFileType.FILE_PATH:
                        vPostParams.Add("crc32", string.Format("{0}", await CRC32.CheckSumFile(fFile.BodyFile)));
                        break;
                }
            }

            //设置MimeType
            // FIX: (添加了下一行代码)
            // 修正上传文件MIME总为octect-stream(原因:未初始化FormFile.ContentType)的问题
            // @fengyh 2016-08-17 14:50
            fFile.ContentType = uploadOptions.MimeType;  
            //设置扩展参数
            foreach (KeyValuePair<string, string> kvp in uploadOptions.ExtraParams)
            {
                vPostParams.Add(kvp.Key, kvp.Value);
            }
            //设置进度处理和取消信号
            ProgressHandler fUpProgressHandler = new ProgressHandler(delegate (long bytesWritten, long totalBytes)
             {
                 double percent = (double)bytesWritten / totalBytes;
                //这样做是为了等待回复
                if (percent > 0.95)
                 {
                     percent = 0.95;
                 }
                 uploadOptions.ProgressHandler(key, percent);
             });

            CancellationSignal fCancelSignal = new CancellationSignal(delegate ()
             {
                 return uploadOptions.CancellationSignal();
             });


            // [x]第一次失败后使用备用域名重试一次
            // FIX 2016-11-22 17:11 
            // 网络错误(网络断开)恢复正常后，重试会出现“流不可读”的错误，因此原域名和重试域名一致
            CompletionHandler fUpCompletionHandler = new CompletionHandler(async delegate (ResponseInfo respInfo, string response)
            {
                Debug.WriteLine("form upload result, {0}",respInfo.StatusCode);
                if (respInfo.needRetry())
                {
                    if (fFile.BodyStream != null)
                    {
                        fFile.BodyStream.Seek(0, SeekOrigin.Begin);
                    }

                    CompletionHandler retried = new CompletionHandler(delegate (ResponseInfo retryRespInfo, string retryResponse)
                    {
                        Debug.WriteLine("form upload retry result, {0}",retryRespInfo.StatusCode);
                        if (respInfo.isOk())
                        {
                            uploadOptions.ProgressHandler(key, 1.0);
                        }

                        if (fFile.BodyStream != null)
                        {
                            fFile.BodyStream.Dispose();
                        }

                        if (upCompletionHandler != null)
                        {
                            try
                            {
                                upCompletionHandler(key, retryRespInfo, retryResponse);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine("form upload retry completion error, {0}", ex.Message);
                            }
                        }
                    });

                    // 使用uploadHost -- REMINDME-1
                    await this.mHttpManager.postMultipartDataForm(uploadHost, null, vPostParams, fFile, fUpProgressHandler, retried);
                }
                else
                {
                    if (respInfo.isOk())
                    {
                        uploadOptions.ProgressHandler(key, 1.0);
                    }

                    if (fFile.BodyStream != null)
                    {
                        fFile.BodyStream.Dispose();
                    }

                    if (upCompletionHandler != null)
                    {
                        try
                        {
                            upCompletionHandler(key, respInfo, response);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("form upload completion error, {0}", ex.Message);
                        }
                    }
                }
            });

            // // 使用uploadHost -- REMINDME-2
            await this.mHttpManager.postMultipartDataForm(uploadHost, null, vPostParams, fFile, fUpProgressHandler, fUpCompletionHandler);
        }
    }
}