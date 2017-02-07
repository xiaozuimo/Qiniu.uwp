using Qiniu.Util;
using Qiniu.IO.Model;
using System;
using System.IO;
using Qiniu.Http;
using System.Threading.Tasks;
using Windows.Storage;

namespace Qiniu.IO
{
    /// <summary>
    /// 上传管理器，根据文件大小以及阈值设置自动选择合适的上传方式，支持以下(1)(2)(3)
    /// 
    /// (1)网络较好并且待上传的文件体积较小时使用简单上传
    /// 
    /// (2)文件较大或者网络状况不理想时请使用分片上传
    /// 
    /// (3)文件较大上传需要花费较长时间，建议使用断点续上传
    /// </summary>
    public class UploadManager
    {
        // 根据此阈值确定是否使用分片上传(默认值1MB)
        private long PUT_THRESHOLD = 1048576;

        // 分片上传的ChunkSize(默认值2MB)
        private ChunkUnit CHUNK_UNIT = ChunkUnit.U2048K;

        // 是否从CDN上传
        private bool UPLOAD_FROM_CDN = false;

        // 上传进度处理器 - 仅用于上传大文件
        private UploadProgressHandler upph = null;

        // 上传控制器 - 仅用于上传大文件
        private UploadController upctl = null;

        // 上传记录文件 - 仅用于上传大文件
        private StorageFile recordFile = null;

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="putThreshold">根据文件大小选择简单上传或分片上传，阈值</param>
        /// <param name="uploadFromCDN">是否从CDN上传</param>
        public UploadManager(long putThreshold = 1048576, bool uploadFromCDN = false)
        {
            PUT_THRESHOLD = putThreshold;
            UPLOAD_FROM_CDN = uploadFromCDN;
        }

        /// <summary>
        /// 设置上传进度处理器-仅对于上传大文件有效，如果不设置则使用默认的进度处理器
        /// </summary>
        /// <param name="upph">上传进度处理器</param>
        public void SetUploadProgressHandler(UploadProgressHandler upph)
        {
            this.upph = upph;
        }

        /// <summary>
        /// 设置上传控制器-仅对于上传大文件有效，如不设置则使用默认控制器
        /// </summary>
        /// <param name="upctl">上传控制器</param>
        public void SetUploadController(UploadController upctl)
        {
            this.upctl = upctl;
        }

        /// <summary>
        /// 设置断点记录文件-仅对于上传大文件有效
        /// </summary>
        /// <param name="recordFile">记录文件</param>
        public void SetRecordFile(StorageFile recordFile)
        {
            this.recordFile = recordFile;
        }

        /// <summary>
        /// 设置分片上传的“片”大小(单位:字节)
        /// </summary>
        /// <param name="cu"></param>
        public void SetChunkUnit(ChunkUnit cu)
        {
            CHUNK_UNIT = cu;
        }

        /// <summary>
        /// [异步async]上传文件
        /// </summary>
        /// <param name="localFile">本地待上传的文件名</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <returns>上传文件后的返回结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile localFile, string saveKey, string token)
        {
            HttpResult result = new HttpResult();

            var fi = await localFile.GetBasicPropertiesAsync();

            if (fi.Size > (ulong)PUT_THRESHOLD)
            {
                if (recordFile == null)
                {
                    string recordKey = ResumeHelper.GetDefaultRecordKey(localFile.Path, saveKey);
                    recordFile = await (await UserEnv.GetHomeFolderAsync()).CreateFileAsync(recordKey, CreationCollisionOption.OpenIfExists);
                }
                if (upph == null)
                {
                    upph = new UploadProgressHandler(ResumableUploader.DefaultUploadProgressHandler);
                }

                if (upctl == null)
                {
                    upctl = new UploadController(ResumableUploader.DefaultUploadController);
                }

                ResumableUploader ru = new ResumableUploader(UPLOAD_FROM_CDN, CHUNK_UNIT);
                result = await ru.UploadFileAsync(localFile, saveKey, token, recordFile, upph, upctl);
            }
            else
            {
                FormUploader fu = new FormUploader(UPLOAD_FROM_CDN);
                result = await fu.UploadFileAsync(localFile, saveKey, token);
            }

            return result;
        }


        /// <summary>
        /// [异步async]上传数据
        /// </summary>
        /// <param name="data">待上传的数据</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <returns>上传文件后的返回结果</returns>
        public async Task<HttpResult> UploadDataAsync(byte[] data, string saveKey, string token)
        {
            HttpResult result = new HttpResult();

            if (data.Length > PUT_THRESHOLD)
            {
                ResumableUploader ru = new ResumableUploader(UPLOAD_FROM_CDN);
                result = await ru.UploadDataAsync(data, saveKey, token, null);
            }
            else
            {
                FormUploader fu = new FormUploader(UPLOAD_FROM_CDN);
                result = await fu.UploadDataAsync(data, saveKey, token);
            }

            return result;
        }

        /// <summary>
        /// [异步async]上传数据流，根据流长度以及设置的阈值(用户初始化UploadManager时可指定该值)自动选择表单或者分片上传
        /// </summary>
        /// <param name="stream">待上传的数据流，要求：流长度(Stream.Length)是确定的</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <returns>上传数据后的返回结果</returns>
        public async Task<HttpResult> UploadStreamAsync(Stream stream, string saveKey, string token)
        {
            HttpResult result = new HttpResult();

            if (stream.Length > PUT_THRESHOLD)
            {
                ResumableUploader ru = new ResumableUploader(UPLOAD_FROM_CDN);
                result = await ru.UploadStreamAsync(stream, saveKey, token, null);
            }
            else
            {
                FormUploader fu = new FormUploader(UPLOAD_FROM_CDN);
                result = await fu.UploadStreamAsync(stream, saveKey, token);
            }

            return result;
        }
    }
}
