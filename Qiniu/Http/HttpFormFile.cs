using System.IO;
using Windows.Storage;

namespace Qiniu.Http
{
    public class HttpFormFile
    {
        public string Filename { set; get; }
        public string ContentType { set; get; }
        public HttpFileType BodyType { set; get; }
        public Stream BodyStream { set; get; }
        //public string BodyFile { set; get; }

        public StorageFile BodyFile { get; set; }

        public byte[] BodyBytes { set; get; }
        public int Offset { set; get; }
        public int Count { set; get; }

        private HttpFormFile()
        {
        }

        private static HttpFormFile NewObject(string filename, string contentType, object body)
        {
            HttpFormFile obj = new HttpFormFile();
            obj.Filename = filename;
            obj.ContentType = contentType;
            if (body is Stream)
            {
                obj.BodyStream = (Stream)body;
            }
            else if (body is byte[])
            {
                obj.BodyBytes = (byte[])body;
            }
            else if (body is StorageFile)
            {
                obj.BodyFile = (StorageFile)body;
            }
            return obj;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="contentType"></param>
        /// <param name="file"></param>
        /// <returns></returns>
        public static HttpFormFile NewFileFromPath(string filename, string contentType, StorageFile file)
        {
            HttpFormFile obj = NewObject(filename, contentType, file);
            obj.BodyType = HttpFileType.FILE_PATH;
            return obj;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="contentType"></param>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static HttpFormFile NewFileFromStream(string filename, string contentType, Stream stream)
        {
            HttpFormFile obj = NewObject(filename, contentType, stream);
            obj.BodyType = HttpFileType.FILE_STREAM;
            return obj;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="contentType"></param>
        /// <param name="fileData"></param>
        /// <returns></returns>
        public static HttpFormFile NewFileFromBytes(string filename, string contentType, byte[] fileData)
        {
            HttpFormFile obj = NewObject(filename, contentType, fileData);
            obj.BodyType = HttpFileType.DATA_BYTES;
            return obj;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="contentType"></param>
        /// <param name="fileData"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static HttpFormFile NewFileFromSlice(string filename, string contentType, byte[] fileData, int offset, int count)
        {
            HttpFormFile obj = NewObject(filename, contentType, fileData);
            obj.BodyType = HttpFileType.DATA_SLICE;
            obj.Offset = offset;
            obj.Count = count;
            return obj;
        }
    }
}
