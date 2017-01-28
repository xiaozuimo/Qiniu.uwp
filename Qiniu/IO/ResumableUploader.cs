﻿using System;
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
        /// <param name="uploadFromCDN">是否从CDN上传</param>
        /// <param name="chunkUnit">分片大小</param>
        public ResumableUploader(bool uploadFromCDN = false, ChunkUnit chunkUnit = ChunkUnit.U2048K)
        {
            uploadHost = uploadFromCDN ? Config.ZONE.UploadHost : Config.ZONE.UpHost;

            httpManager = new HttpManager();

            CHUNK_SIZE = RCU.getChunkSize(chunkUnit);
        }

        /// <summary>
        /// 分片上传/断点续上传
        /// 使用默认记录文件(recordFile)和默认进度处理(progressHandler)
        /// </summary>
        /// <param name="file">本地待上传的文件</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <returns>上传结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile file, string saveKey, string token)
        {
            var recordKey = DefaultRecordKey(file.Path, saveKey);
            var recordFile = await (await UserEnv.GetHomeFolderAsync()).CreateFileAsync(recordKey);
            UploadProgressHandler uppHandler = new UploadProgressHandler(DefaultUploadProgressHandler);
            return await UploadFileAsync(file, saveKey, token, recordFile, uppHandler);
        }

        /// <summary>
        /// 分片上传/断点续上传
        /// 使用默认进度处理(progressHandler)
        /// </summary>
        /// <param name="file">本地待上传的文件</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <returns>上传结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile file, string saveKey, string token, StorageFile recordFile)
        {
            UploadProgressHandler uppHandler = new UploadProgressHandler(DefaultUploadProgressHandler);
            return await UploadFileAsync(file, saveKey, token, recordFile, uppHandler);
        }

        /// <summary>
        /// 分片上传/断点续上传，带有自定义进度处理
        /// </summary>
        /// <param name="file">本地待上传的文件</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <param name="uppHandler">上传进度处理</param>
        /// <returns>上传结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile file, string saveKey, string token, StorageFile recordFile, UploadProgressHandler uppHandler)
        {
            HttpResult result = new HttpResult();

            Stream fs = null;

            if(uppHandler==null)
            {
                uppHandler = DefaultUploadProgressHandler;
            }

            try
            {
                fs = await file.OpenStreamForReadAsync();

                long fileSize = fs.Length;
                long chunkSize = CHUNK_SIZE;
                long blockSize = BLOCK_SIZE;
                byte[] chunkBuffer = new byte[chunkSize];
                int blockCount = (int)((fileSize + blockSize - 1) / blockSize);

                var resumeInfo = await ResumeHelper.LoadAsync(recordFile);
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
                    #region one_block

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

                    fs.Read(chunkBuffer, 0, (int)chunkSize);
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

                            fs.Read(chunkBuffer, 0, (int)chunkSize);
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

                    #endregion one_block

                    resumeInfo.BlockIndex = index;
                    resumeInfo.Contexts[index] = context;
                    await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                    ++index;
                }

                //string fileName = Path.GetFileName(file.Path);
                hr = await MkfileAsync(fileSize, saveKey, resumeInfo.Contexts, token);
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
                                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), file.Path, saveKey);

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
        /// 分片上传/断点续上传，带有自定义进度处理、高级控制功能
        /// </summary>
        /// <param name="file">本地待上传的文件</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <param name="uppHandler">上传进度处理</param>
        /// <param name="uploadController">上传控制(暂停/继续/退出)</param>
        /// <returns>上传结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile file, string saveKey, string token, StorageFile recordFile, UploadProgressHandler uppHandler, UploadController uploadController)
        {
            HttpResult result = new HttpResult();

            Stream fs = null;

            if (uppHandler == null)
            {
                uppHandler = new UploadProgressHandler(DefaultUploadProgressHandler);
            }

            try
            {
                fs = await file.OpenStreamForReadAsync();

                long fileSize = fs.Length;
                long chunkSize = CHUNK_SIZE;
                long blockSize = BLOCK_SIZE;
                byte[] chunkBuffer = new byte[chunkSize];
                int blockCount = (int)((fileSize + blockSize - 1) / blockSize);

                var resumeInfo = await ResumeHelper.LoadAsync(recordFile);
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

                        #region one_block

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

                        fs.Read(chunkBuffer, 0, (int)chunkSize);
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

                                fs.Read(chunkBuffer, 0, (int)chunkSize);
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

                        #endregion one_block

                        resumeInfo.BlockIndex = index;
                        resumeInfo.Contexts[index] = context;
                        await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                        ++index;
                    }
                }

                //string fileName = Path.GetFileName(file.Path);
                hr = await MkfileAsync(fileSize, saveKey, resumeInfo.Contexts, token);
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
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), file.Path, saveKey);
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
        /// 分片上传/断点续上传，检查CRC32，支持重试
        /// 使用默认记录文件(recordFile)和默认进度处理(progressHandler)
        /// </summary>
        /// <param name="file">本地待上传的文件</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="maxTry">最大尝试次数</param>
        /// <returns>上传结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile file, string saveKey, string token, int maxTry)
        {
            string recordKey = DefaultRecordKey(file.Path, saveKey);
            var recordFile = await (await UserEnv.GetHomeFolderAsync()).CreateFileAsync(recordKey);
            UploadProgressHandler uppHandler = new UploadProgressHandler(DefaultUploadProgressHandler);
            return await UploadFileAsync(file, saveKey, token, recordFile, maxTry, uppHandler);
        }

        /// <summary>
        /// 分片上传/断点续上传，检查CRC32，支持重试
        /// 使用默认进度处理(progressHandler)
        /// </summary>
        /// <param name="file">本地待上传的文件</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <param name="maxTry">最大尝试次数</param>
        /// <returns>上传结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile file, string saveKey, string token, StorageFile recordFile, int maxTry)
        {
            UploadProgressHandler uppHandler = new UploadProgressHandler(DefaultUploadProgressHandler);
            return await UploadFileAsync(file, saveKey, token, recordFile, maxTry, uppHandler);
        }

        /// <summary>
        /// 分片上传/断点续上传，检查CRC32，带有自定义进度处理，支持重试
        /// </summary>
        /// <param name="file">本地待上传的文件</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <param name="maxTry">最大尝试次数</param>
        /// <param name="uppHandler">上传进度处理</param>
        /// <returns>上传结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile file, string saveKey, string token, StorageFile recordFile, int maxTry, UploadProgressHandler uppHandler)
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
                fs = await file.OpenStreamForReadAsync();

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
                    #region one_block

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

                    fs.Read(chunkBuffer, 0, (int)chunkSize);

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

                            fs.Read(chunkBuffer, 0, (int)chunkSize);

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

                    #endregion one_block

                    resumeInfo.BlockIndex = index;
                    resumeInfo.Contexts[index] = context;
                    await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                    ++index;
                }

                //string fileName = Path.GetFileName(file.Path);
                hr = await MkfileAsync(fileSize, saveKey, resumeInfo.Contexts, token);
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
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), file.Path, saveKey);
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

                sb.AppendFormat(" @{0}\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));

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
        /// 分片上传/断点续上传，检查CRC32，，带有自定义进度处理和上传控制，支持重试
        /// </summary>
        /// <param name="file">本地待上传的文件</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <param name="maxTry">最大尝试次数</param>
        /// <param name="uppHandler">上传进度处理</param>
        /// <param name="uploadController">上传控制(暂停/继续/退出)</param>
        /// <returns>上传结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile file, string saveKey, string token, StorageFile recordFile, int maxTry, UploadProgressHandler uppHandler, UploadController uploadController)
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
                fs = await file.OpenStreamForReadAsync();

                long fileSize = fs.Length;
                long chunkSize = CHUNK_SIZE;
                long blockSize = BLOCK_SIZE;
                byte[] chunkBuffer = new byte[chunkSize];
                int blockCount = (int)((fileSize + blockSize - 1) / blockSize);

                var resumeInfo = await ResumeHelper.LoadAsync(recordFile);
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

                        #region one_block

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

                        fs.Read(chunkBuffer, 0, (int)chunkSize);

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

                                fs.Read(chunkBuffer, 0, (int)chunkSize);

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

                        #endregion one_block

                        resumeInfo.BlockIndex = index;
                        resumeInfo.Contexts[index] = context;
                        await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                        ++index;
                    }
                }

                //string fileName = Path.GetFileName(file.Path);
                hr = await MkfileAsync(fileSize, saveKey, resumeInfo.Contexts, token);
                if (hr.Code != (int)HttpCode.OK)
                {
                    result.Shadow(hr);
                    result.RefText += string.Format("[{0}] [ResumableUpload] Uploaded: \"{1}\" ==> \"{2}\"\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), file.Path, saveKey);

                    return result;
                }

                await recordFile.DeleteAsync();
                result.Shadow(hr);
                result.RefText += string.Format("[{0}] [ResumableUpload] Uploaded: \"{1}\" ==> \"{2}\"\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), file.Path, saveKey);
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
        /// 分片上传/断点续上传，检查CRC32，，带有自定义进度处理和上传控制，支持重试
        /// </summary>
        /// <param name="file">本地待上传的文件</param>
        /// <param name="saveKey">要保存的文件名称</param>
        /// <param name="token">上传凭证</param>
        /// <param name="recordFile">记录文件(记录断点信息)</param>
        /// <param name="maxTry">最大尝试次数</param>
        /// <param name="uppHandler">上传进度处理</param>
        /// <param name="uploadController">上传控制(暂停/继续/退出)</param>
        /// <param name="extraParams">用户自定义的附加参数</param>
        /// <returns>上传结果</returns>
        public async Task<HttpResult> UploadFileAsync(StorageFile file, string saveKey, string token, StorageFile recordFile, int maxTry, UploadProgressHandler uppHandler, UploadController uploadController, Dictionary<string, string> extraParams)
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
                fs = await file.OpenStreamForReadAsync();

                long fileSize = fs.Length;
                long chunkSize = CHUNK_SIZE;
                long blockSize = BLOCK_SIZE;
                byte[] chunkBuffer = new byte[chunkSize];
                int blockCount = (int)((fileSize + blockSize - 1) / blockSize);

                var resumeInfo = await ResumeHelper.LoadAsync(recordFile);
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

                        #region one_block

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

                        fs.Read(chunkBuffer, 0, (int)chunkSize);

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

                                fs.Read(chunkBuffer, 0, (int)chunkSize);

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

                        #endregion one_block

                        resumeInfo.BlockIndex = index;
                        resumeInfo.Contexts[index] = context;
                        await ResumeHelper.SaveAsync(resumeInfo, recordFile);
                        ++index;
                    }
                }

                //string fileName = Path.GetFileName(file.Path);
                hr = await MkfileAsync(fileSize, saveKey, resumeInfo.Contexts, token);
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
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), file.Path, saveKey);
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
        /// 上传数据流-分片方式
        /// </summary>
        /// <param name="stream">数据流，流长度必须可确定</param>
        /// <param name="saveKey">要保存的key</param>
        /// <param name="token">上传凭证</param>
        /// <param name="spHandler">数据流上传进度处理</param> 
        /// <returns></returns>
        public async Task<HttpResult> UploadStreamAsync(Stream stream, string saveKey, string token, StreamProgressHandler spHandler)
        {
            HttpResult result = new HttpResult();

            if(spHandler==null)
            {
                spHandler = new StreamProgressHandler(DefaultStreamProgressHandler);
            }

            try
            {
                long fileSize = 0;
                int blockSize = (int)BLOCK_SIZE;
                int chunkSize = 0;
                byte[] buffer = new byte[blockSize];

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
                    #region one_block

                    #region mkblk
                    chunkSize = stream.Read(buffer, 0, blockSize);

                    if(chunkSize==0)
                    {
                        break;
                    }

                    if (chunkSize < blockSize)
                    {
                        hr = await MkblkAsync(buffer, chunkSize, chunkSize, token);
                    }
                    else
                    {
                        hr = await MkblkAsync(buffer, blockSize, chunkSize, token);
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

                    #endregion one_block

                    resumeInfo.FileSize = fileSize;
                    resumeInfo.BlockIndex = index;
                    resumeInfo.BlockCount = index + 1;
                    resumeInfo.SContexts.Add(context);
                    ++index;
                }

                hr = await MkfileAsync(fileSize, saveKey, resumeInfo.SContexts, token);
                if (hr.Code != (int)HttpCode.OK)
                {
                    result.Shadow(hr);
                    result.RefText += string.Format("[{0}] [ResumableUpload] Error: mkfile: code = {1}, text = {2}\n",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), hr.Code, hr.Text);

                    return result;
                }

                result.Shadow(hr);
                result.RefText += string.Format("[{0}] [ResumableUpload] Uploaded: #DATA# ==> \"{1}\"\n",
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
        /// 上传数据
        /// </summary>
        /// <param name="data">待上传的数据</param>
        /// <param name="saveKey">要保存的key</param>
        /// <param name="token">上传凭证</param>
        /// <param name="spHandler">上传进度处理</param>
        /// <returns></returns>
        public async Task<HttpResult> UploadDataAsync(byte[] data, string saveKey, string token, StreamProgressHandler spHandler)
        {
            Stream ms = new MemoryStream(data);
            return await UploadStreamAsync(ms, saveKey, token, spHandler);
        }

        /// <summary>
        /// 默认的断点记录文件名称
        /// </summary>
        /// <param name="localFile">待上传的本地文件</param>
        /// <param name="saveKey">要保存的目标key</param>
        /// <returns></returns>
        public string DefaultRecordKey(string localFile, string saveKey)
        {
            return "QiniuRU_" + Hashing.CalcMD5(localFile + saveKey);
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
                Debug.WriteLine("[{0}] [ResumableUpload] Progress: {1,7:0.000}%", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), 100.0 * uploadedBytes / totalBytes);
            }
            else
            {
                Debug.WriteLine("[{0}] [ResumableUpload] Progress: {1,7:0.000}%\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), 100.0);
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
                Debug.WriteLine("[{0}] [ResumableUpload] UploadledBytes: {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"), uploadedBytes);
            }
            else
            {
                Debug.WriteLine("[{0}] [ResumableUpload] Done.\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));
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
        /// 创建文件
        /// </summary>
        private async Task<HttpResult> MkfileAsync(long size, IList<string> contexts, string token)
        {
            HttpResult result = new HttpResult();

            try
            {
                string url = string.Format("{0}/mkfile/{1}", uploadHost, size);
                string body = StringHelper.Join(contexts, ",");
                string upToken = string.Format("UpToken {0}", token);

                result = await httpManager.PostPlainAsync(url, body, upToken);
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
        /// 创建文件
        /// </summary>
        private async Task<HttpResult> MkfileAsync(long size, string saveKey, IList<string> contexts, string token)
        {
            HttpResult result = new HttpResult();

            try
            {
                string keyStr = string.Format("/key/{0}", Base64.UrlSafeBase64Encode(saveKey));

                string url = string.Format("{0}/mkfile/{1}{2}", uploadHost, size, keyStr);
                string body = StringHelper.Join(contexts, ",");
                string upToken = string.Format("UpToken {0}", token);

                result = await httpManager.PostPlainAsync(url, body, upToken);
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
        /// 创建文件
        /// </summary>
        private async Task<HttpResult> MkfileAsync(long size, string saveKey, string mimeType, IList<string> contexts, string token)
        {
            HttpResult result = new HttpResult();

            try
            {
                string keyStr = string.Format("/key/{0}", Base64.UrlSafeBase64Encode(saveKey));
                string mimeTypeStr = string.Format("/mimeType/{0}", Base64.UrlSafeBase64Encode(mimeType));

                string url = string.Format("{0}/mkfile/{1}{2}{3}", uploadHost, size, keyStr, mimeTypeStr);
                string body = StringHelper.Join(contexts, ",");
                string upToken = string.Format("UpToken {0}", token);

                result = await httpManager.PostPlainAsync(url, body, upToken);
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
        /// 创建文件
        /// </summary>
        private async Task<HttpResult> MkfileAsync(string fileName, long size, string saveKey, string mimeType, IList<string> contexts, string token)
        {
            HttpResult result = new HttpResult();

            try
            {
                string fnameStr = string.Format("/fname/{0}", Base64.UrlSafeBase64Encode(fileName));
                string mimeTypeStr = string.Format("/mimeType/{0}", Base64.UrlSafeBase64Encode(mimeType));
                string keyStr = string.Format("/key/{0}", Base64.UrlSafeBase64Encode(saveKey));

                string url = string.Format("{0}/mkfile/{1}{2}{3}{4}", uploadHost, size, mimeTypeStr, fnameStr, keyStr);
                string body = StringHelper.Join(contexts, ",");
                string upToken = string.Format("UpToken {0}", token);

                result = await httpManager.PostFormAsync(url, body, upToken);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder("mkfile Error: ");
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }

                sb.AppendFormat(" @{0}\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// 创建文件
        /// </summary>
        private async Task<HttpResult> MkfileAsync(long size, string saveKey, IList<string> contexts, string token, Dictionary<string, string> extraParams)
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

                result = await httpManager.PostPlainAsync(url, body, upToken);
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
        /// 创建文件
        /// </summary>
        private async Task<HttpResult> MkfileAsync(string fileName, long size, string saveKey, string mimeType, List<string> contexts, string token, Dictionary<string, string> extraParams)
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

                result = await httpManager.PostFormAsync(url, body, upToken);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder("mkfile Error: ");
                Exception e = ex;
                while (e != null)
                {
                    sb.Append(e.Message + " ");
                    e = e.InnerException;
                }

                sb.AppendFormat(" @{0}\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff"));

                result.RefCode = (int)HttpCode.USER_EXCEPTION;
                result.RefText += sb.ToString();
            }

            return result;
        }

        /// <summary>
        /// 创建块(携带首片数据)
        /// </summary>
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
        /// 创建块(携带首片数据),同时检查CRC32
        /// </summary>
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
        /// 上传数据片
        /// </summary>
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
        /// 上传数据片,同时检查CRC32
        /// </summary>
        /// <param name="chunkBuffer"></param>
        /// <param name="offset"></param>
        /// <param name="chunkSize"></param>
        /// <param name="context"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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
