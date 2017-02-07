using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json;
using Qiniu.Common;
using Qiniu.IO.Model;
using Qiniu.Util;
using Qiniu.Http;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;

namespace Qiniu.IO
{
    /// <summary>
    /// 分片上传/断点续上传，适合于以下情形(2)(3)
    /// 
    /// (1)网络较好并且待上传的文件体积较小时使用简单上传
    /// 
    /// (2)文件较大或者网络状况不理想时请使用分片上传
    /// 
    /// (3)文件较大上传需要花费较长时间，建议使用断点续上传
    /// </summary>
    public class ResumableUploader
    {
        //分片上传切片大小(要求:一个块能够恰好被分割为若干片)，可根据ChunkUnit设置
        private int CHUNK_SIZE;

        //分片上传块的大小，固定为4M，不可修改
        private const int BLOCK_SIZE = 4 * 1024 * 1024;


        // 上传域名，默认Config.ZONE.upHost
        private string uploadHost;

        // HTTP请求管理器(GET/POST)
        private HttpManager httpManager;

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="uploadFromCDN">是否从CDN上传(默认否)，使用CDN上传可能会有更好的效果</param>
        /// <param name="chunkUnit">分片大小，默认设置为2MB</param>
        public ResumableUploader(bool uploadFromCDN = false, ChunkUnit chunkUnit = ChunkUnit.U2048K)
        {
            uploadHost = uploadFromCDN ? Config.ZONE.UploadHost : Config.ZONE.UpHost;

            httpManager = new HttpManager();

            CHUNK_SIZE = RCU.GetChunkSize(chunkUnit);
        }

        /// <summary>
        /// [异步async]分片上传/断点续上传，使用默认记录文件(recordFile)和默认进度处理(progressHandler)
        /// </summary>
        /// <param name="localFile">本地待上传的文件名</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <returns>上传文件后的返回结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile localFile, string saveKey, string token)
        {
            string recordKey = ResumeHelper.GetDefaultRecordKey(localFile.Path, saveKey);
            var recordFile = await (await UserEnv.GetHomeFolderAsync()).CreateFileAsync(recordKey, CreationCollisionOption.OpenIfExists);
            UploadProgressHandler uppHandler = new UploadProgressHandler(DefaultUploadProgressHandler);
            return await UploadFileAsync(localFile, saveKey, token, recordFile, uppHandler);
        }

        /// <summary>
        /// [异步async]分片上传/断点续上传，使用默认进度处理(progressHandler)
        /// </summary>
        /// <param name="localFile">本地待上传的文件名</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <returns>上传文件后的返回结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile localFile, string saveKey, string token, StorageFile recordFile)
        {
            UploadProgressHandler uppHandler = new UploadProgressHandler(DefaultUploadProgressHandler);
            return await UploadFileAsync(localFile, saveKey, token, recordFile, uppHandler);
        }

        /// <summary>
        /// [异步async]分片上传/断点续上传，带有自定义进度处理
        /// </summary>
        /// <param name="localFile">本地待上传的文件名</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <param name="uppHandler">上传进度处理，设置为null则表示使用默认处理</param>
        /// <returns>上传文件后的返回结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile localFile, string saveKey, string token, StorageFile recordFile, UploadProgressHandler uppHandler)
        {
            HttpResult result = new HttpResult();

            Stream fs = null;

            if (uppHandler == null)
            {
                uppHandler = DefaultUploadProgressHandler;
            }

            try
            {
                fs = await localFile.OpenStreamForReadAsync();

                long fileSize = fs.Length;
                long chunkSize = CHUNK_SIZE;
                long blockSize = BLOCK_SIZE;
                byte[] chunkBuffer = new byte[chunkSize];
                int blockCount = (int)((fileSize + blockSize - 1) / blockSize);

                ResumeInfo resumeInfo = await ResumeHelper.LoadAsync(recordFile);
                if (resumeInfo == null)
                {
                    resumeInfo = new ResumeInfo()
                    {
                        FileSize = fileSize,
                        BlockIndex = 0,
                        BlockCount = blockCount,
                        Contexts = new string[blockCount]
                    };

                    await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                }

                int index = resumeInfo.BlockIndex;
                long offset = index * blockSize;
                string context = null;
                long leftBytes = fileSize - offset;
                long blockLeft = 0;
                long blockOffset = 0;
                HttpResult hr = null;
                ResumeContext rc = null;

                fs.Seek(offset, SeekOrigin.Begin);

                while (leftBytes > 0)
                {
                    #region one-block

                    #region mkblk

                    if (leftBytes < BLOCK_SIZE)
                    {
                        blockSize = leftBytes;
                    }
                    else
                    {
                        blockSize = BLOCK_SIZE;
                    }
                    if (leftBytes < CHUNK_SIZE)
                    {
                        chunkSize = leftBytes;
                    }
                    else
                    {
                        chunkSize = CHUNK_SIZE;
                    }

                    await fs.ReadAsync(chunkBuffer, 0, (int)chunkSize);
                    hr = await MkblkAsync(chunkBuffer, blockSize, chunkSize, token);
                    if (hr.Code != (int)HttpCode.OK)
                    {
                        result.Shadow(hr);
                        result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkblk: code = {1}, text = {2}, offset = {3}, blockSize = {4}, chunkSize = {5}\n",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockSize, chunkSize);

                        return result;
                    }

                    rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                    context = rc.Ctx;
                    offset += chunkSize;
                    leftBytes -= chunkSize;

                    #endregion mkblk

                    uppHandler(offset, fileSize);

                    if (leftBytes > 0)
                    {
                        blockLeft = blockSize - chunkSize;
                        blockOffset = chunkSize;
                        while (blockLeft > 0)
                        {
                            #region bput-loop

                            if (blockLeft < CHUNK_SIZE)
                            {
                                chunkSize = blockLeft;
                            }
                            else
                            {
                                chunkSize = CHUNK_SIZE;
                            }

                            await fs.ReadAsync(chunkBuffer, 0, (int)chunkSize);
                            hr = await BputAsync(chunkBuffer, blockOffset, chunkSize, context, token);
                            if (hr.Code != (int)HttpCode.OK)
                            {
                                result.Shadow(hr);
                                result.RefText += string.Format("[{0}] [ResumableUpload] Error: bput: code = {1}, text = {2}, offset = {3}, blockOffset = {4}, chunkSize = {5}\n",
                                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockOffset, chunkSize);

                                return result;
                            }

                            rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                            context = rc.Ctx;

                            offset += chunkSize;
                            leftBytes -= chunkSize;
                            blockOffset += chunkSize;
                            blockLeft -= chunkSize;

                            #endregion bput-loop

                            uppHandler(offset, fileSize);
                        }
                    }

                    #endregion one-block

                    resumeInfo.BlockIndex = index;
                    resumeInfo.Contexts[index] = context;
                    await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                    ++index;
                }

                hr = await MkFileAsync(fileSize, saveKey, resumeInfo.Contexts, token);
                if (hr.Code != (int)HttpCode.OK)
                {
                    result.Shadow(hr);
                    result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkfile: code ={1}, text = {2}\n",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text);

                    return result;
                }

                await recordFile.DeleteAsync();
                result.Shadow(hr);
                result.RefText += string.Format("[{0}] [ResumableUpload] Uploaded: \"{1}\" ==> \"{2}\"\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), localFile.Path, saveKey);

            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [ResumableUpload] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }
            finally
            {
                if (fs != null)
                {
                    fs.Dispose();
                }
            }

            return result;
        }

        /// <summary>
        /// [异步async]分片上传/断点续上传，带有自定义进度处理、高级控制功能
        /// </summary>
        /// <param name="localFile">本地待上传的文件名</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <param name="uppHandler">上传进度处理，设置为null则表示使用默认处理</param>
        /// <param name="uploadController">上传控制(暂停/继续/退出)</param>
        /// <returns>上传文件后的返回结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile localFile, string saveKey, string token, StorageFile recordFile, UploadProgressHandler uppHandler, UploadController uploadController)
        {
            HttpResult result = new HttpResult();

            Stream fs = null;

            if (uppHandler == null)
            {
                uppHandler = new UploadProgressHandler(DefaultUploadProgressHandler);
            }

            try
            {
                fs = await localFile.OpenStreamForReadAsync();

                long fileSize = fs.Length;
                long chunkSize = CHUNK_SIZE;
                long blockSize = BLOCK_SIZE;
                byte[] chunkBuffer = new byte[chunkSize];
                int blockCount = (int)((fileSize + blockSize - 1) / blockSize);

                ResumeInfo resumeInfo = await ResumeHelper.LoadAsync(recordFile);
                if (resumeInfo == null)
                {
                    resumeInfo = new ResumeInfo()
                    {
                        FileSize = fileSize,
                        BlockIndex = 0,
                        BlockCount = blockCount,
                        Contexts = new string[blockCount]
                    };

                    await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                }

                int index = resumeInfo.BlockIndex;
                long offset = index * blockSize;
                string context = null;
                long leftBytes = fileSize - offset;
                long blockLeft = 0;
                long blockOffset = 0;
                HttpResult hr = null;
                ResumeContext rc = null;

                fs.Seek(offset, SeekOrigin.Begin);

                var upts = UPTS.Activated;
                bool bres = true;
                var mres = new ManualResetEvent(true);

                while (leftBytes > 0)
                {
                    // 每上传一个BLOCK之前，都要检查一下UPTS
                    upts = uploadController();

                    if (upts == UPTS.Aborted)
                    {
                        result.Code = (int)HttpCode.USER_CANCELED;
                        result.RefCode = (int)HttpCode.USER_CANCELED;
                        result.RefText += string.Format("[{0}] [ResumableUpload] Info: upload task is aborted\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));

                        return result;
                    }
                    else if (upts == UPTS.Suspended)
                    {
                        if (bres)
                        {
                            bres = false;
                            mres.Reset();

                            result.RefCode = (int)HttpCode.USER_PAUSED;
                            result.RefText += string.Format("[{0}] [ResumableUpload] Info: upload task is paused\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                        }
                        mres.WaitOne(1000);
                    }
                    else
                    {
                        if (!bres)
                        {
                            bres = true;
                            mres.Set();

                            result.RefCode = (int)HttpCode.USER_RESUMED;
                            result.RefText += string.Format("[{0}] [ResumableUpload] Info: upload task is resumed\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                        }

                        #region one-block

                        #region mkblk

                        if (leftBytes < BLOCK_SIZE)
                        {
                            blockSize = leftBytes;
                        }
                        else
                        {
                            blockSize = BLOCK_SIZE;
                        }
                        if (leftBytes < CHUNK_SIZE)
                        {
                            chunkSize = leftBytes;
                        }
                        else
                        {
                            chunkSize = CHUNK_SIZE;
                        }

                        await fs.ReadAsync(chunkBuffer, 0, (int)chunkSize);
                        hr = await MkblkAsync(chunkBuffer, blockSize, chunkSize, token);
                        if (hr.Code != (int)HttpCode.OK)
                        {
                            result.Shadow(hr);
                            result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkblk: code = {1}, text = {2}, offset = {3}, blockSize = {4}, chunkSize = {5}\n",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockSize, chunkSize);

                            return result;
                        }

                        rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                        context = rc.Ctx;
                        offset += chunkSize;
                        leftBytes -= chunkSize;

                        #endregion mkblk

                        uppHandler(offset, fileSize);

                        if (leftBytes > 0)
                        {
                            blockLeft = blockSize - chunkSize;
                            blockOffset = chunkSize;
                            while (blockLeft > 0)
                            {
                                #region bput-loop

                                if (blockLeft < CHUNK_SIZE)
                                {
                                    chunkSize = blockLeft;
                                }
                                else
                                {
                                    chunkSize = CHUNK_SIZE;
                                }

                                await fs.ReadAsync(chunkBuffer, 0, (int)chunkSize);
                                hr = await BputAsync(chunkBuffer, blockOffset, chunkSize, context, token);
                                if (hr.Code != (int)HttpCode.OK)
                                {
                                    result.Shadow(hr);
                                    result.RefText += string.Format("[{0}] [ResumableUpload] Error: bput: code = {1}, text = {2}, offset={3}, blockOffset={4}, chunkSize={5}\n",
                                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockOffset, chunkSize);

                                    return result;
                                }

                                rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                                context = rc.Ctx;

                                offset += chunkSize;
                                leftBytes -= chunkSize;
                                blockOffset += chunkSize;
                                blockLeft -= chunkSize;

                                #endregion bput-loop

                                uppHandler(offset, fileSize);
                            }
                        }

                        #endregion one-block

                        resumeInfo.BlockIndex = index;
                        resumeInfo.Contexts[index] = context;
                        await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                        ++index;
                    }
                }

                hr = await MkFileAsync(fileSize, saveKey, resumeInfo.Contexts, token);
                if (hr.Code != (int)HttpCode.OK)
                {
                    result.Shadow(hr);
                    result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkfile: code = {1}, text = {2}\n",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text);

                    return result;
                }

                await recordFile.DeleteAsync();
                result.Shadow(hr);
                result.RefText += string.Format("[{0}] [ResumableUpload] Uploaded: \"{1}\" ==> \"{2}\"\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), localFile.Path, saveKey);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [ResumableUpload] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }
            finally
            {
                if (fs != null)
                {
                    fs.Dispose();
                }
            }

            return result;
        }

        /// <summary>
        /// [异步async]分片上传/断点续上传，检查CRC32，使用默认记录文件(recordFile)和默认进度处理(progressHandler)，可自动重试
        /// </summary>
        /// <param name="localFile">本地待上传的文件名</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="maxTry">最大尝试次数</param>
        /// <returns>上传文件后的返回结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile localFile, string saveKey, string token, int maxTry)
        {
            string recordKey = ResumeHelper.GetDefaultRecordKey(localFile.Path, saveKey);
            var recordFile = await (await UserEnv.GetHomeFolderAsync()).CreateFileAsync(recordKey, CreationCollisionOption.OpenIfExists);
            UploadProgressHandler uppHandler = new UploadProgressHandler(DefaultUploadProgressHandler);
            return await UploadFileAsync(localFile, saveKey, token, recordFile, maxTry, uppHandler);
        }

        /// <summary>
        /// [异步async]分片上传/断点续上传，检查CRC32，支持重试，使用默认进度处理(progressHandler)
        /// </summary>
        /// <param name="localFile">本地待上传的文件名</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <param name="maxTry">最大尝试次数</param>
        /// <returns>上传文件后的返回结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile localFile, string saveKey, string token, StorageFile recordFile, int maxTry)
        {
            UploadProgressHandler uppHandler = new UploadProgressHandler(DefaultUploadProgressHandler);
            return await UploadFileAsync(localFile, saveKey, token, recordFile, maxTry, uppHandler);
        }

        /// <summary>
        /// [异步async]分片上传/断点续上传，检查CRC32，带有自定义进度处理，可自动重试
        /// </summary>
        /// <param name="localFile">本地待上传的文件名</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <param name="maxTry">最大尝试次数</param>
        /// <param name="uppHandler">上传进度处理，设置为null则表示使用默认处理</param>
        /// <returns>上传文件后的返回结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile localFile, string saveKey, string token, StorageFile recordFile, int maxTry, UploadProgressHandler uppHandler)
        {
            HttpResult result = new HttpResult();

            Stream fs = null;

            int MAX_TRY = GetMaxTry(maxTry);

            if (uppHandler == null)
            {
                uppHandler = DefaultUploadProgressHandler;
            }

            try
            {
                fs = await localFile.OpenStreamForReadAsync();

                long fileSize = fs.Length;
                long chunkSize = CHUNK_SIZE;
                long blockSize = BLOCK_SIZE;
                byte[] chunkBuffer = new byte[chunkSize];
                int blockCount = (int)((fileSize + blockSize - 1) / blockSize);

                ResumeInfo resumeInfo = await ResumeHelper.LoadAsync(recordFile);
                if (resumeInfo == null)
                {
                    resumeInfo = new ResumeInfo()
                    {
                        FileSize = fileSize,
                        BlockIndex = 0,
                        BlockCount = blockCount,
                        Contexts = new string[blockCount]
                    };

                    await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                }

                int index = resumeInfo.BlockIndex;
                long offset = index * blockSize;
                string context = null;
                long leftBytes = fileSize - offset;
                long blockLeft = 0;
                long blockOffset = 0;
                HttpResult hr = null;
                ResumeContext rc = null;

                fs.Seek(offset, SeekOrigin.Begin);

                int iTry = 0;

                while (leftBytes > 0)
                {
                    #region one-block

                    #region mkblk

                    if (leftBytes < BLOCK_SIZE)
                    {
                        blockSize = leftBytes;
                    }
                    else
                    {
                        blockSize = BLOCK_SIZE;
                    }
                    if (leftBytes < CHUNK_SIZE)
                    {
                        chunkSize = leftBytes;
                    }
                    else
                    {
                        chunkSize = CHUNK_SIZE;
                    }

                    await fs.ReadAsync(chunkBuffer, 0, (int)chunkSize);

                    iTry = 0;
                    while (++iTry <= MAX_TRY)
                    {
                        result.RefText += string.Format("[{0}] [ResumableUpload] try mkblk#{1}\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), iTry);

                        hr = await MkblkCheckedAsync(chunkBuffer, blockSize, chunkSize, token);

                        if (hr.Code == (int)HttpCode.OK && hr.RefCode != (int)HttpCode.USER_NEED_RETRY)
                        {
                            break;
                        }
                    }

                    if (hr.Code != (int)HttpCode.OK || hr.RefCode == (int)HttpCode.USER_NEED_RETRY)
                    {
                        result.Shadow(hr);
                        result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkblk: code = {1}, text = {2}, offset = {3}, blockSize={4}, chunkSize={5}\n",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockSize, chunkSize);

                        return result;
                    }

                    rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                    context = rc.Ctx;
                    offset += chunkSize;
                    leftBytes -= chunkSize;

                    #endregion mkblk

                    uppHandler(offset, fileSize);

                    if (leftBytes > 0)
                    {
                        blockLeft = blockSize - chunkSize;
                        blockOffset = chunkSize;
                        while (blockLeft > 0)
                        {
                            #region bput-loop

                            if (blockLeft < CHUNK_SIZE)
                            {
                                chunkSize = blockLeft;
                            }
                            else
                            {
                                chunkSize = CHUNK_SIZE;
                            }

                            await fs.ReadAsync(chunkBuffer, 0, (int)chunkSize);

                            iTry = 0;
                            while (++iTry <= MAX_TRY)
                            {
                                result.RefText += string.Format("[{0}] [ResumableUpload] try bput#{1}\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), iTry);

                                hr = await BputCheckedAsync(chunkBuffer, blockOffset, chunkSize, context, token);

                                if (hr.Code == (int)HttpCode.OK && hr.RefCode != (int)HttpCode.USER_NEED_RETRY)
                                {
                                    break;
                                }
                            }
                            if (hr.Code != (int)HttpCode.OK || hr.RefCode == (int)HttpCode.USER_NEED_RETRY)
                            {
                                result.Shadow(hr);
                                result.RefText += string.Format("[{0}] [ResumableUpload] Error: bput: code = {1}, text = {2}, offset = {3}, blockOffset = {4}, chunkSize = {5}\n",
                                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockOffset, chunkSize);
                                return result;
                            }

                            rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                            context = rc.Ctx;

                            offset += chunkSize;
                            leftBytes -= chunkSize;
                            blockOffset += chunkSize;
                            blockLeft -= chunkSize;

                            #endregion bput-loop

                            uppHandler(offset, fileSize);
                        }
                    }

                    #endregion one-block

                    resumeInfo.BlockIndex = index;
                    resumeInfo.Contexts[index] = context;
                    await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                    ++index;
                }

                hr = await MkFileAsync(fileSize, saveKey, resumeInfo.Contexts, token);
                if (hr.Code != (int)HttpCode.OK)
                {
                    result.Shadow(hr);
                    result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkfile: code = {1}, text = {2}\n",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text);

                    return result;
                }

                await recordFile.DeleteAsync();
                result.Shadow(hr);
                result.RefText += string.Format("[{0}] [ResumableUpload] Uploaded: \"{1}\" ==> \"{2}\"\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), localFile.Path, saveKey);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [ResumableUpload] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }
            finally
            {
                if (fs != null)
                {
                    fs.Dispose();
                }
            }

            return result;
        }

        /// <summary>
        /// [异步async]分片上传/断点续上传，检查CRC32，带有自定义进度处理和上传控制，可自动重试
        /// </summary>
        /// <param name="localFile">本地待上传的文件名</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <param name="maxTry">最大尝试次数</param>
        /// <param name="uppHandler">上传进度处理，设置为null则表示使用默认处理</param>
        /// <param name="uploadController">上传控制(暂停/继续/退出)</param>
        /// <returns>上传文件后的返回结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile localFile, string saveKey, string token, StorageFile recordFile, int maxTry, UploadProgressHandler uppHandler, UploadController uploadController)
        {
            HttpResult result = new HttpResult();

            Stream fs = null;

            int MAX_TRY = GetMaxTry(maxTry);

            if (uppHandler == null)
            {
                uppHandler = DefaultUploadProgressHandler;
            }

            try
            {
                fs = await localFile.OpenStreamForReadAsync();

                long fileSize = fs.Length;
                long chunkSize = CHUNK_SIZE;
                long blockSize = BLOCK_SIZE;
                byte[] chunkBuffer = new byte[chunkSize];
                int blockCount = (int)((fileSize + blockSize - 1) / blockSize);

                ResumeInfo resumeInfo = await ResumeHelper.LoadAsync(recordFile);
                if (resumeInfo == null)
                {
                    resumeInfo = new ResumeInfo()
                    {
                        FileSize = fileSize,
                        BlockIndex = 0,
                        BlockCount = blockCount,
                        Contexts = new string[blockCount]
                    };

                    await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                }

                int index = resumeInfo.BlockIndex;
                long offset = index * blockSize;
                string context = null;
                long leftBytes = fileSize - offset;
                long blockLeft = 0;
                long blockOffset = 0;
                HttpResult hr = null;
                ResumeContext rc = null;

                fs.Seek(offset, SeekOrigin.Begin);

                var upts = UPTS.Activated;
                bool bres = true;
                var mres = new ManualResetEvent(true);
                int iTry = 0;

                while (leftBytes > 0)
                {
                    // 每上传一个BLOCK之前，都要检查一下UPTS
                    upts = uploadController();

                    if (upts == UPTS.Aborted)
                    {
                        result.Code = (int)HttpCode.USER_CANCELED;
                        result.RefCode = (int)HttpCode.USER_CANCELED;
                        result.RefText += string.Format("[{0}] [ResumableUpload] Info: upload task is aborted\n",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));

                        return result;
                    }
                    else if (upts == UPTS.Suspended)
                    {
                        if (bres)
                        {
                            bres = false;
                            mres.Reset();

                            result.RefCode = (int)HttpCode.USER_PAUSED;
                            result.RefText += string.Format("[{0}] [ResumableUpload] Info: upload task is paused\n",
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                        }
                        mres.WaitOne(1000);
                    }
                    else
                    {
                        if (!bres)
                        {
                            bres = true;
                            mres.Set();

                            result.RefCode = (int)HttpCode.USER_RESUMED;
                            result.RefText += string.Format("[{0}] [ResumableUpload] Info: upload task is resumed\n",
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                        }

                        #region one-block

                        #region mkblk

                        if (leftBytes < BLOCK_SIZE)
                        {
                            blockSize = leftBytes;
                        }
                        else
                        {
                            blockSize = BLOCK_SIZE;
                        }
                        if (leftBytes < CHUNK_SIZE)
                        {
                            chunkSize = leftBytes;
                        }
                        else
                        {
                            chunkSize = CHUNK_SIZE;
                        }

                        await fs.ReadAsync(chunkBuffer, 0, (int)chunkSize);

                        iTry = 0;
                        while (++iTry <= MAX_TRY)
                        {
                            result.RefText += string.Format("[{0}] [ResumableUpload] try mkblk#{1}\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), iTry);

                            hr = await MkblkCheckedAsync(chunkBuffer, blockSize, chunkSize, token);

                            if (hr.Code == (int)HttpCode.OK && hr.RefCode != (int)HttpCode.USER_NEED_RETRY)
                            {
                                break;
                            }
                        }
                        if (hr.Code != (int)HttpCode.OK || hr.RefCode == (int)HttpCode.USER_NEED_RETRY)
                        {
                            result.Shadow(hr);
                            result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkblk: code = {1}, text = {2}, offset = {3}, blockSize = {4}, chunkSize = {5}\n",
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockSize, chunkSize);

                            return result;
                        }

                        rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                        context = rc.Ctx;
                        offset += chunkSize;
                        leftBytes -= chunkSize;

                        #endregion mkblk

                        uppHandler(offset, fileSize);

                        if (leftBytes > 0)
                        {
                            blockLeft = blockSize - chunkSize;
                            blockOffset = chunkSize;
                            while (blockLeft > 0)
                            {
                                #region bput-loop

                                if (blockLeft < CHUNK_SIZE)
                                {
                                    chunkSize = blockLeft;
                                }
                                else
                                {
                                    chunkSize = CHUNK_SIZE;
                                }

                                await fs.ReadAsync(chunkBuffer, 0, (int)chunkSize);

                                iTry = 0;
                                while (++iTry <= MAX_TRY)
                                {
                                    result.RefText += string.Format("[{0}] [ResumableUpload] try bput#{1}\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), iTry);

                                    hr = await BputCheckedAsync(chunkBuffer, blockOffset, chunkSize, context, token);

                                    if (hr.Code == (int)HttpCode.OK && hr.RefCode != (int)HttpCode.USER_NEED_RETRY)
                                    {
                                        break;
                                    }
                                }
                                if (hr.Code != (int)HttpCode.OK || hr.RefCode == (int)HttpCode.USER_NEED_RETRY)
                                {
                                    result.Shadow(hr);
                                    result.RefText += string.Format("[{0}] [ResumableUpload] Error: bput: code = {1}, text = {2}, offset = {3}, blockOffset = {4}, chunkSize = {5}\n",
                                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockOffset, chunkSize);

                                    return result;
                                }

                                rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                                context = rc.Ctx;

                                offset += chunkSize;
                                leftBytes -= chunkSize;
                                blockOffset += chunkSize;
                                blockLeft -= chunkSize;

                                #endregion bput-loop

                                uppHandler(offset, fileSize);
                            }
                        }

                        #endregion one-block

                        resumeInfo.BlockIndex = index;
                        resumeInfo.Contexts[index] = context;
                        await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                        ++index;
                    }
                }

                hr = await MkFileAsync(fileSize, saveKey, resumeInfo.Contexts, token);
                if (hr.Code != (int)HttpCode.OK)
                {
                    result.Shadow(hr);
                    result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkfile: code = {1}, text = {2}\n",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text);

                    return result;
                }

                await recordFile.DeleteAsync();
                result.Shadow(hr);
                result.RefText += string.Format("[{0}] [ResumableUpload] Uploaded: \"{1}\" ==> \"{2}\"\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), localFile.Path, saveKey);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [ResumableUpload] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }
            finally
            {
                if (fs != null)
                {
                    fs.Dispose();
                }
            }

            return result;
        }

        /// <summary>
        /// [异步async]分片上传/断点续上传，检查CRC32，带有自定义进度处理和上传控制，支持重试
        /// </summary>
        /// <param name="localFile">本地待上传的文件名</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <param name="maxTry">最大尝试次数</param>
        /// <param name="uppHandler">上传进度处理，设置为null则表示使用默认处理</param>
        /// <param name="uploadController">上传控制(暂停/继续/退出)</param>
        /// <param name="extraParams">用户自定义的附加参数</param>
        /// <returns>上传文件后的返回结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile localFile, string saveKey, string token, StorageFile recordFile, int maxTry, UploadProgressHandler uppHandler, UploadController uploadController, Dictionary<string, string> extraParams)
        {
            HttpResult result = new HttpResult();

            Stream fs = null;

            int MAX_TRY = GetMaxTry(maxTry);

            if (uppHandler == null)
            {
                uppHandler = DefaultUploadProgressHandler;
            }

            try
            {
                fs = await localFile.OpenStreamForReadAsync();

                long fileSize = fs.Length;
                long chunkSize = CHUNK_SIZE;
                long blockSize = BLOCK_SIZE;
                byte[] chunkBuffer = new byte[chunkSize];
                int blockCount = (int)((fileSize + blockSize - 1) / blockSize);

                ResumeInfo resumeInfo = await ResumeHelper.LoadAsync(recordFile);
                if (resumeInfo == null)
                {
                    resumeInfo = new ResumeInfo()
                    {
                        FileSize = fileSize,
                        BlockIndex = 0,
                        BlockCount = blockCount,
                        Contexts = new string[blockCount]
                    };

                    await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                }

                int index = resumeInfo.BlockIndex;
                long offset = index * blockSize;
                string context = null;
                long leftBytes = fileSize - offset;
                long blockLeft = 0;
                long blockOffset = 0;
                HttpResult hr = null;
                ResumeContext rc = null;

                fs.Seek(offset, SeekOrigin.Begin);

                var upts = UPTS.Activated;
                bool bres = true;
                var mres = new ManualResetEvent(true);
                int iTry = 0;

                while (leftBytes > 0)
                {
                    // 每上传一个BLOCK之前，都要检查一下UPTS
                    upts = uploadController();

                    if (upts == UPTS.Aborted)
                    {
                        result.Code = (int)HttpCode.USER_CANCELED;
                        result.RefCode = (int)HttpCode.USER_CANCELED;
                        result.RefText += string.Format("[{0}] [ResumableUpload] Info: upload task is aborted\n",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));

                        return result;
                    }
                    else if (upts == UPTS.Suspended)
                    {
                        if (bres)
                        {
                            bres = false;
                            mres.Reset();

                            result.RefCode = (int)HttpCode.USER_PAUSED;
                            result.RefText += string.Format("[{0}] [ResumableUpload] Info: upload task is paused\n",
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                        }
                        mres.WaitOne(1000);
                    }
                    else
                    {
                        if (!bres)
                        {
                            bres = true;
                            mres.Set();

                            result.RefCode = (int)HttpCode.USER_RESUMED;
                            result.RefText += string.Format("[{0}] [ResumableUpload] Info: upload task is resumed\n",
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                        }

                        #region one-block

                        #region mkblk

                        if (leftBytes < BLOCK_SIZE)
                        {
                            blockSize = leftBytes;
                        }
                        else
                        {
                            blockSize = BLOCK_SIZE;
                        }
                        if (leftBytes < CHUNK_SIZE)
                        {
                            chunkSize = leftBytes;
                        }
                        else
                        {
                            chunkSize = CHUNK_SIZE;
                        }

                        await fs.ReadAsync(chunkBuffer, 0, (int)chunkSize);

                        iTry = 0;
                        while (++iTry <= MAX_TRY)
                        {
                            result.RefText += string.Format("[{0}] [ResumableUpload] try mkblk#{1}\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), iTry);

                            hr = await MkblkCheckedAsync(chunkBuffer, blockSize, chunkSize, token);

                            if (hr.Code == (int)HttpCode.OK && hr.RefCode != (int)HttpCode.USER_NEED_RETRY)
                            {
                                break;
                            }
                        }
                        if (hr.Code != (int)HttpCode.OK || hr.RefCode == (int)HttpCode.USER_NEED_RETRY)
                        {
                            result.Shadow(hr);
                            result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkblk: code = {1}, text = {2}, offset = {3}, blockSize = {4}, chunkSize = {5}\n",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockSize, chunkSize);

                            return result;
                        }

                        rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                        context = rc.Ctx;
                        offset += chunkSize;
                        leftBytes -= chunkSize;

                        #endregion mkblk

                        uppHandler(offset, fileSize);

                        if (leftBytes > 0)
                        {
                            blockLeft = blockSize - chunkSize;
                            blockOffset = chunkSize;
                            while (blockLeft > 0)
                            {
                                #region bput-loop

                                if (blockLeft < CHUNK_SIZE)
                                {
                                    chunkSize = blockLeft;
                                }
                                else
                                {
                                    chunkSize = CHUNK_SIZE;
                                }

                                await fs.ReadAsync(chunkBuffer, 0, (int)chunkSize);

                                iTry = 0;
                                while (++iTry <= MAX_TRY)
                                {
                                    result.RefText += string.Format("[{0}] [ResumableUpload] try bput#{1}\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), iTry);

                                    hr = await BputCheckedAsync(chunkBuffer, blockOffset, chunkSize, context, token);

                                    if (hr.Code == (int)HttpCode.OK && hr.RefCode != (int)HttpCode.USER_NEED_RETRY)
                                    {
                                        break;
                                    }
                                }
                                if (hr.Code != (int)HttpCode.OK || hr.RefCode == (int)HttpCode.USER_NEED_RETRY)
                                {
                                    result.Shadow(hr);
                                    result.RefText += string.Format("[{0}] [ResumableUpload] Error: bput: code = {1}, text = {2}, offset = {3}, blockOffset = {4}, chunkSize = {5}\n",
                                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockOffset, chunkSize);

                                    return result;
                                }

                                rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                                context = rc.Ctx;

                                offset += chunkSize;
                                leftBytes -= chunkSize;
                                blockOffset += chunkSize;
                                blockLeft -= chunkSize;

                                #endregion bput-loop

                                uppHandler(offset, fileSize);
                            }
                        }

                        #endregion one-block

                        resumeInfo.BlockIndex = index;
                        resumeInfo.Contexts[index] = context;
                        await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                        ++index;
                    }
                }

                hr = await MkFileAsync(fileSize, saveKey, resumeInfo.Contexts, token, extraParams);
                if (hr.Code != (int)HttpCode.OK)
                {
                    result.Shadow(hr);
                    result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkfile: code = {1}, text = {2}\n",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text);

                    return result;
                }

                await recordFile.DeleteAsync();
                result.Shadow(hr);
                result.RefText += string.Format("[{0}] [ResumableUpload] Uploaded: \"{1}\" ==> \"{2}\"\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), localFile.Path, saveKey);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [ResumableUpload] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }
            finally
            {
                if (fs != null)
                {
                    fs.Dispose();
                }
            }

            return result;
        }

        /// <summary>
        /// [异步async]上传数据-分片方式，不支持断点续上传。如果预估数据比较大或者网络状态不佳，可以使用此方式
        /// </summary>
        /// <param name="data">待上传的数据</param>
        /// <param name="saveKey">要保存的key</param>
        /// <param name="token">上传凭证</param>
        /// <param name="uppHandler">上传进度处理，设置为null则表示使用默认处理</param>
        /// <returns>上传数据后的返回结果</returns>
        public async Task<HttpResult> UploadDataAsync(byte[] data, string saveKey, string token, UploadProgressHandler uppHandler)
        {
            HttpResult result = new HttpResult();

            if (uppHandler == null)
            {
                uppHandler = DefaultUploadProgressHandler;
            }

            MemoryStream ms = null;

            try
            {
                ms = new MemoryStream(data);

                long fileSize = data.Length;
                long chunkSize = CHUNK_SIZE;
                long blockSize = BLOCK_SIZE;
                byte[] chunkBuffer = new byte[chunkSize];
                int blockCount = (int)((fileSize + blockSize - 1) / blockSize);

                ResumeInfo resumeInfo = new ResumeInfo()
                {
                    FileSize = 0,
                    BlockIndex = 0,
                    BlockCount = 0,
                    SContexts = new List<string>()
                };

                int index = resumeInfo.BlockIndex;
                long offset = index * blockSize;
                string context = null;
                long leftBytes = fileSize - offset;
                long blockLeft = 0;
                long blockOffset = 0;
                HttpResult hr = null;
                ResumeContext rc = null;

                while (leftBytes > 0)
                {
                    #region one-block

                    #region mkblk

                    if (leftBytes < BLOCK_SIZE)
                    {
                        blockSize = leftBytes;
                    }
                    else
                    {
                        blockSize = BLOCK_SIZE;
                    }
                    if (leftBytes < CHUNK_SIZE)
                    {
                        chunkSize = leftBytes;
                    }
                    else
                    {
                        chunkSize = CHUNK_SIZE;
                    }

                    await ms.ReadAsync(chunkBuffer, 0, (int)chunkSize);
                    hr = await MkblkAsync(chunkBuffer, blockSize, chunkSize, token);
                    if (hr.Code != (int)HttpCode.OK)
                    {
                        result.Shadow(hr);
                        result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkblk: code = {1}, text = {2}, offset = {3}, blockSize = {4}, chunkSize = {5}\n",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockSize, chunkSize);

                        return result;
                    }

                    rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                    context = rc.Ctx;
                    offset += chunkSize;
                    leftBytes -= chunkSize;

                    #endregion mkblk

                    uppHandler(offset, fileSize);

                    if (leftBytes > 0)
                    {
                        blockLeft = blockSize - chunkSize;
                        blockOffset = chunkSize;
                        while (blockLeft > 0)
                        {
                            #region bput-loop

                            if (blockLeft < CHUNK_SIZE)
                            {
                                chunkSize = blockLeft;
                            }
                            else
                            {
                                chunkSize = CHUNK_SIZE;
                            }

                            await ms.ReadAsync(chunkBuffer, 0, (int)chunkSize);
                            hr = await BputAsync(chunkBuffer, blockOffset, chunkSize, context, token);
                            if (hr.Code != (int)HttpCode.OK)
                            {
                                result.Shadow(hr);
                                result.RefText += string.Format("[{0}] [ResumableUpload] Error: bput: code = {1}, text = {2}, offset = {3}, blockOffset = {4}, chunkSize = {5}\n",
                                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockOffset, chunkSize);

                                return result;
                            }

                            rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                            context = rc.Ctx;

                            offset += chunkSize;
                            leftBytes -= chunkSize;
                            blockOffset += chunkSize;
                            blockLeft -= chunkSize;

                            #endregion bput-loop

                            uppHandler(offset, fileSize);
                        }
                    }

                    #endregion one-block

                    resumeInfo.BlockIndex = index;
                    resumeInfo.SContexts.Add(context);
                    ++index;
                }

                hr = await MkFileAsync(fileSize, saveKey, resumeInfo.Contexts, token);
                if (hr.Code != (int)HttpCode.OK)
                {
                    result.Shadow(hr);
                    result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkfile: code ={1}, text = {2}\n",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text);

                    return result;
                }

                result.Shadow(hr);
                result.RefText += string.Format("[{0}] [ResumableUpload] Uploaded: \"#DATA#\" ==> \"{1}\"\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), saveKey);

            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [ResumableUpload] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }
            finally
            {
                if (ms != null)
                {
                    ms.Dispose();
                }
            }

            return result;
        }

        /// <summary>
        /// [异步async]上传数据流-分片方式，不支持断点续上传。如果预估数据流比较长或者网络状态不佳，可以使用此方式
        /// </summary>
        /// <param name="stream">数据流，比如FileStream等</param>
        /// <param name="saveKey">要保存的key</param>
        /// <param name="token">上传凭证</param>
        /// <param name="spHandler">数据流上传进度处理，设置为null则表示使用默认处理</param> 
        /// <returns>上传数据流后的返回结果</returns>
        public async Task<HttpResult> UploadStreamAsync(Stream stream, string saveKey, string token, StreamProgressHandler spHandler)
        {
            HttpResult result = new HttpResult();

            if (spHandler == null)
            {
                spHandler = new StreamProgressHandler(DefaultStreamProgressHandler);
            }

            try
            {
                long fileSize = 0;
                int blockSize = (int)BLOCK_SIZE;
                int chunkSize = 0;
                byte[] chunkBuffer = new byte[blockSize];

                ResumeInfo resumeInfo = new ResumeInfo()
                {
                    FileSize = 0,
                    BlockIndex = 0,
                    BlockCount = 0,
                    SContexts = new List<string>()
                };

                int index = 0;
                long offset = 0;
                string context = null;
                HttpResult hr = null;
                ResumeContext rc = null;

                while (true)
                {
                    #region one-block

                    #region mkblk

                    chunkSize = await stream.ReadAsync(chunkBuffer, 0, blockSize);

                    if (chunkSize == 0)
                    {
                        break;
                    }

                    if (chunkSize < blockSize)
                    {
                        hr = await MkblkAsync(chunkBuffer, chunkSize, chunkSize, token);
                    }
                    else
                    {
                        hr = await MkblkAsync(chunkBuffer, blockSize, chunkSize, token);
                    }
                    if (hr.Code != (int)HttpCode.OK)
                    {
                        result.Shadow(hr);
                        result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkblk: code = {1}, text = {2}, offset = {3}, blockSize = {4}, chunkSize = {5}\n",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text, offset, blockSize, chunkSize);

                        return result;
                    }

                    rc = JsonConvert.DeserializeObject<ResumeContext>(hr.Text);
                    context = rc.Ctx;
                    offset += chunkSize;
                    fileSize += chunkSize;

                    #endregion mkblk

                    spHandler(offset);

                    #endregion one-block

                    resumeInfo.FileSize = fileSize;
                    resumeInfo.BlockIndex = index;
                    resumeInfo.BlockCount = index + 1;
                    resumeInfo.SContexts.Add(context);
                    ++index;
                }

                hr = await MkFileAsync(fileSize, saveKey, resumeInfo.SContexts, token);

                // 表示数据流读取完毕
                spHandler(-1);

                if (hr.Code != (int)HttpCode.OK)
                {
                    result.Shadow(hr);
                    result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkfile: code = {1}, text = {2}\n",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text);

                    return result;
                }

                result.Shadow(hr);
                result.RefText += string.Format("[{0}] [ResumableUpload] Uploaded: #STREAM# ==> \"{1}\"\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), saveKey);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] [ResumableUpload] Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }
            finally
            {
                stream.Dispose();
            }

            return result;
        }

        /// <summary>
        /// 默认的进度处理函数
        /// </summary>
        /// <param name="uploadedBytes">已上传的字节数</param>
        /// <param name="totalBytes">文件总字节数</param>
        public static void DefaultUploadProgressHandler(long uploadedBytes, long totalBytes)
        {
            if (uploadedBytes < totalBytes)
            {
                System.Diagnostics.Debug.WriteLine("[{0}] [ResumableUpload] Progress: {1,7:0.000}%", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), 100.0 * uploadedBytes / totalBytes);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[{0}] [ResumableUpload] Progress: {1,7:0.000}%\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), 100.0);
            }
        }

        /// <summary>
        /// 默认的进度处理函数
        /// </summary>
        /// <param name="uploadedBytes">已上传的字节数</param>
        public static void DefaultStreamProgressHandler(long uploadedBytes)
        {
            if (uploadedBytes > 0)
            {
                System.Diagnostics.Debug.WriteLine("[{0}] [ResumableUpload] UploadledBytes: {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), uploadedBytes);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[{0}] [ResumableUpload] Done.\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
            }
        }

        /// <summary>
        /// 默认的上传控制函数
        /// </summary>
        /// <returns>控制状态</returns>
        public static UPTS DefaultUploadController()
        {
            return UPTS.Activated;
        }
        
        /// <summary>
        /// [异步async]根据已上传的所有分片数据创建文件，保存的文件名会自动生成
        /// </summary>
        /// <param name="size">目标文件大小</param>
        /// <param name="contexts">所有数据块的Context</param>
        /// <param name="token">上传凭证</param>
        /// <returns>此操作执行后的返回结果</returns>
        private async Task<HttpResult> MkFileAsync(long size, IList<string> contexts, string token)
        {
            HttpResult result = new HttpResult();

            try
            {
                string url = string.Format("{0}/mkfile/{1}", uploadHost, size);
                string body = StringHelper.Join(contexts, ",");
                string upToken = string.Format("UpToken {0}", token);

                result = await httpManager.PostTextAsync(url, body, upToken);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] mkfile Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// [异步async]根据已上传的所有分片数据创建文件
        /// </summary>
        /// <param name="size">目标文件大小</param>
        /// <param name="saveKey">要保存的文件名</param>
        /// <param name="contexts">所有数据块的Context</param>
        /// <param name="token">上传凭证</param>
        /// <returns>此操作执行后的返回结果</returns>
        private async Task<HttpResult> MkFileAsync(long size, string saveKey, IList<string> contexts, string token)
        {
            HttpResult result = new HttpResult();

            try
            {
                string keyStr = string.Format("/key/{0}", Base64.UrlSafeBase64Encode(saveKey));

                string url = string.Format("{0}/mkfile/{1}{2}", uploadHost, size, keyStr);
                string body = StringHelper.Join(contexts, ",");
                string upToken = string.Format("UpToken {0}", token);

                result = await httpManager.PostTextAsync(url, body, upToken);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] mkfile Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// [异步async]根据已上传的所有分片数据创建文件，可指定保存文件的MimeType
        /// </summary>
        /// <param name="size">目标文件大小</param>
        /// <param name="saveKey">要保存的文件名</param>
        /// <param name="mimeType">用户指定的文件MimeType</param>
        /// <param name="contexts">所有数据块的Context</param>
        /// <param name="token">上传凭证</param>
        /// <returns>此操作执行后的返回结果</returns>
        private async Task<HttpResult> MkFileAsync(long size, string saveKey, string mimeType, IList<string> contexts, string token)
        {
            HttpResult result = new HttpResult();

            try
            {
                string keyStr = string.Format("/key/{0}", Base64.UrlSafeBase64Encode(saveKey));
                string mimeTypeStr = string.Format("/mimeType/{0}", Base64.UrlSafeBase64Encode(mimeType));

                string url = string.Format("{0}/mkfile/{1}{2}{3}", uploadHost, size, keyStr, mimeTypeStr);
                string body = StringHelper.Join(contexts, ",");
                string upToken = string.Format("UpToken {0}", token);

                result = await httpManager.PostTextAsync(url, body, upToken);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] mkfile Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// [异步async]根据已上传的所有分片数据创建文件
        /// </summary>
        /// <param name="fileName">源文件名</param>
        /// <param name="size">文件大小</param>
        /// <param name="saveKey">要保存的文件名</param>
        /// <param name="mimeType">用户指定的文件MimeType</param>
        /// <param name="contexts">所有数据块的Context</param>
        /// <param name="token">上传凭证</param>
        /// <returns>此操作执行后的返回结果</returns>
        private async Task<HttpResult> MkFileAsync(string fileName, long size, string saveKey, string mimeType, IList<string> contexts, string token)
        {
            HttpResult result = new HttpResult();

            try
            {
                string keyStr = string.Format("/key/{0}", Base64.UrlSafeBase64Encode(saveKey));
                string mimeTypeStr = string.Format("/mimeType/{0}", Base64.UrlSafeBase64Encode(mimeType));
                string fnameStr = string.Format("/fname/{0}", Base64.UrlSafeBase64Encode(fileName));

                string url = string.Format("{0}/mkfile/{1}{2}{3}{4}", uploadHost, size, keyStr, mimeTypeStr, fnameStr);
                string body = StringHelper.Join(contexts, ",");
                string upToken = string.Format("UpToken {0}", token);

                result = await httpManager.PostTextAsync(url, body, upToken);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] mkfile Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// [异步async]根据已上传的所有分片数据创建文件
        /// </summary>
        /// <param name="size">文件大小</param>
        /// <param name="saveKey">要保存的文件名</param>
        /// <param name="contexts">所有数据块的Context</param>
        /// <param name="token">上传凭证</param>
        /// <param name="extraParams">用户指定的额外参数</param>
        /// <returns>此操作执行后的返回结果</returns>
        private async Task<HttpResult> MkFileAsync(long size, string saveKey, IList<string> contexts, string token, Dictionary<string, string> extraParams)
        {
            HttpResult result = new HttpResult();

            try
            {
                string keyStr = string.Format("/key/{0}", Base64.UrlSafeBase64Encode(saveKey));
                string paramStr = "";
                if (extraParams != null && extraParams.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var kvp in extraParams)
                    {
                        sb.AppendFormat("/{0}/{1}", kvp.Key, kvp.Value);
                    }

                    paramStr = sb.ToString();
                }

                string url = string.Format("{0}/mkfile/{1}{2}{3}", uploadHost, size, keyStr, paramStr);
                string body = StringHelper.Join(contexts, ",");
                string upToken = string.Format("UpToken {0}", token);

                result = await httpManager.PostTextAsync(url, body, upToken);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] mkfile Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// [异步async]根据已上传的所有分片数据创建文件
        /// </summary>
        /// <param name="fileName">源文件名</param>
        /// <param name="size">文件大小</param>
        /// <param name="saveKey">要保存的文件名</param>
        /// <param name="mimeType">用户设置的文件MimeType</param>
        /// <param name="contexts">所有数据块的Context</param>
        /// <param name="token">上传凭证</param>
        /// <param name="extraParams">用户指定的额外参数</param>
        /// <returns>此操作执行后的返回结果</returns>
        private async Task<HttpResult> MkFileAsync(string fileName, long size, string saveKey, string mimeType, IList<string> contexts, string token, Dictionary<string, string> extraParams)
        {
            HttpResult result = new HttpResult();

            try
            {
                string fnameStr = string.Format("/fname/{0}", Base64.UrlSafeBase64Encode(fileName));
                string mimeTypeStr = string.Format("/mimeType/{0}", Base64.UrlSafeBase64Encode(mimeType));
                string keyStr = string.Format("/key/{0}", Base64.UrlSafeBase64Encode(saveKey));
                string paramStr = "";
                if (extraParams != null && extraParams.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var kvp in extraParams)
                    {
                        sb.AppendFormat("/{0}/{1}", kvp.Key, kvp.Value);
                    }

                    paramStr = sb.ToString();
                }

                string url = string.Format("{0}/mkfile/{1}{2}{3}{4}{5}", uploadHost, size, mimeTypeStr, fnameStr, keyStr, paramStr);
                string body = StringHelper.Join(contexts, ",");
                string upToken = string.Format("UpToken {0}", token);

                result = await httpManager.PostTextAsync(url, body, upToken);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] mkfile Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// [异步async]创建块(携带首片数据)
        /// </summary>
        /// <param name="chunkBuffer">数据片，此操作都会携带第一个数据片</param>
        /// <param name="blockSize">块大小，除了最后一块可能不足4MB，前面的所有数据块恒定位4MB</param>
        /// <param name="chunkSize">分片大小，一个块可以被分为若干片依次上传然后拼接或者不分片直接上传整块</param>
        /// <param name="token">上传凭证</param>
        /// <returns>此操作执行后的返回结果</returns>
        private async Task<HttpResult> MkblkAsync(byte[] chunkBuffer, long blockSize, long chunkSize, string token)
        {
            HttpResult result = new HttpResult();

            try
            {
                string url = string.Format("{0}/mkblk/{1}", uploadHost, blockSize);
                string upToken = string.Format("UpToken {0}", token);
                using (MemoryStream ms = new MemoryStream(chunkBuffer, 0, (int)chunkSize))
                {
                    byte[] data = ms.ToArray();
                    result = await httpManager.PostDataAsync(url, data, upToken);
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] mkblk Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// [异步async]创建块(携带首片数据),同时检查CRC32
        /// </summary>
        /// <param name="chunkBuffer">数据片，此操作都会携带第一个数据片</param>
        /// <param name="blockSize">块大小，除了最后一块可能不足4MB，前面的所有数据块恒定位4MB</param>
        /// <param name="chunkSize">分片大小，一个块可以被分为若干片依次上传然后拼接或者不分片直接上传整块</param>
        /// <param name="token">上传凭证</param>
        /// <returns>此操作执行后的返回结果</returns>
        private async Task<HttpResult> MkblkCheckedAsync(byte[] chunkBuffer, long blockSize, long chunkSize, string token)
        {
            HttpResult result = new HttpResult();

            try
            {
                string url = string.Format("{0}/mkblk/{1}", uploadHost, blockSize);
                string upToken = string.Format("UpToken {0}", token);
                using (MemoryStream ms = new MemoryStream(chunkBuffer, 0, (int)chunkSize))
                {
                    byte[] data = ms.ToArray();

                    result = await httpManager.PostDataAsync(url, data, upToken);

                    if (result.Code == (int)HttpCode.OK)
                    {
                        var rd = JsonConvert.DeserializeObject<Dictionary<string, string>>(result.Text);
                        if (rd.ContainsKey("crc32"))
                        {
                            uint crc_1 = Convert.ToUInt32(rd["crc32"]);
                            uint crc_2 = CRC32.CheckSumSlice(chunkBuffer, 0, (int)chunkSize);
                            if (crc_1 != crc_2)
                            {
                                result.RefCode = (int)HttpCode.USER_NEED_RETRY;
                                result.RefText += string.Format(" CRC32: remote={0}, local={1}\n", crc_1, crc_2);
                            }
                        }
                    }
                    else
                    {
                        result.RefCode = (int)HttpCode.USER_NEED_RETRY;
                    }
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] mkblk Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// [异步async]上传数据片
        /// </summary>
        /// <param name="chunkBuffer">数据片</param>
        /// <param name="offset">当前片在块中的偏移位置</param>
        /// <param name="chunkSize">当前片的大小</param>
        /// <param name="context">承接前一片数据用到的Context</param>
        /// <param name="token">上传凭证</param>
        /// <returns>此操作执行后的返回结果</returns>
        private async Task<HttpResult> BputAsync(byte[] chunkBuffer, long offset, long chunkSize, string context, string token)
        {
            HttpResult result = new HttpResult();

            try
            {
                string url = string.Format("{0}/bput/{1}/{2}", uploadHost, context, offset);
                string upToken = string.Format("UpToken {0}", token);
                using (MemoryStream ms = new MemoryStream(chunkBuffer, 0, (int)chunkSize))
                {
                    byte[] data = ms.ToArray();
                    result = await httpManager.PostDataAsync(url, data, upToken);
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] bput Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// [异步async]上传数据片,同时检查CRC32
        /// </summary>
        /// <param name="chunkBuffer">数据片</param>
        /// <param name="offset">当前片在块中的偏移位置</param>
        /// <param name="chunkSize">当前片的大小</param>
        /// <param name="context">承接前一片数据用到的Context</param>
        /// <param name="token">上传凭证</param>
        /// <returns>此操作执行后的返回结果</returns>
        private async Task<HttpResult> BputCheckedAsync(byte[] chunkBuffer, long offset, long chunkSize, string context, string token)
        {
            HttpResult result = new HttpResult();

            try
            {
                string url = string.Format("{0}/bput/{1}/{2}", uploadHost, context, offset);
                string upToken = string.Format("UpToken {0}", token);

                using (MemoryStream ms = new MemoryStream(chunkBuffer, 0, (int)chunkSize))
                {
                    byte[] data = ms.ToArray();

                    result = await httpManager.PostDataAsync(url, data, upToken);

                    if (result.Code == (int)HttpCode.OK)
                    {
                        var rd = JsonConvert.DeserializeObject<Dictionary<string, string>>(result.Text);
                        if (rd.ContainsKey("crc32"))
                        {
                            uint crc_1 = Convert.ToUInt32(rd["crc32"]);
                            uint crc_2 = CRC32.CheckSumSlice(chunkBuffer, 0, (int)chunkSize);
                            if (crc_1 != crc_2)
                            {
                                result.RefCode = (int)HttpCode.USER_NEED_RETRY;
                                result.RefText += string.Format(" CRC32: remote={0}, local={1}\n", crc_1, crc_2);
                            }
                        }
                    }
                    else
                    {
                        result.RefCode = (int)HttpCode.USER_NEED_RETRY;
                    }
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("[{0}] bput Error: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }
                sb.AppendLine();

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// 设置最大尝试次数，取值范围1~20
        /// </summary>
        /// <param name="maxTry">用户设置得最大尝试次数</param>
        /// <returns></returns>
        private int GetMaxTry(int maxTry)
        {
            int iMaxTry = 5;
            int MIN = 1;
            int MAX = 20;

            if (maxTry < MIN)
            {
                iMaxTry = MIN;
            }
            else if (maxTry > MAX)
            {
                iMaxTry = MAX;
            }
            else
            {
                iMaxTry = maxTry;
            }

            return iMaxTry;
        }
    }
}
