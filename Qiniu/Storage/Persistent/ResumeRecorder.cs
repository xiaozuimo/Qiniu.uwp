using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Qiniu.Storage.Persistent
{
    /// <summary>
    /// 分片上传进度记录器
    /// </summary>
    public class ResumeRecorder
    {
        //上传进度记录目录
        private StorageFolder _folder;

        /// <summary>
        /// 构建上传进度记录器
        /// </summary>
        /// <param name="dir">保存目录</param>
        public ResumeRecorder(StorageFolder folder)
        {
            _folder = folder;
        }

        /// <summary>
        /// 写入或更新上传进度记录
        /// </summary>
        /// <param name="key">记录文件名</param>
        /// <param name="data">上传进度数据</param>
        public async Task SetAsync(string key, byte[] data)
        {
            string filePath = Path.Combine(_folder.Path, key);
            var file = await _folder.CreateFileAsync(key, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteBytesAsync(file, data);
        }

        /// <summary>
        /// 获取上传进度记录
        /// </summary>
        /// <param name="key">记录文件名</param>
        /// <returns>上传进度数据</returns>
        public async Task<byte[]> GetAsync(string key)
        {
            byte[] data = null;
            try
            {
                var file = await _folder.GetFileAsync(key);
                var buffer = await FileIO.ReadBufferAsync(file);
                var reader = DataReader.FromBuffer(buffer);
                data = new byte[buffer.Length];
                reader.ReadBytes(data);
            }
            catch (Exception)
            {

            }
            return data;
        }

        /// <summary>
        /// 删除上传进度记录
        /// </summary>
        /// <param name="key">记录文件名</param>
        public async Task DeleteAsync(string key)
        {
            try
            {
                var file = await _folder.GetFileAsync(key);
                await file.DeleteAsync();
            }
            catch (Exception)
            {

            }
        }
    }
}
