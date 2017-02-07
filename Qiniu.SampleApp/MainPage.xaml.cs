using Qiniu.CDN;
using Qiniu.Common;
using Qiniu.IO;
using Qiniu.IO.Model;
using Qiniu.RS;
using Qiniu.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Qiniu.SampleApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            this.Loaded += MainPage_Loaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            var accessKey = "2RzN79slQnF-prpQWmS5Ll56A0AzqAW4oKSW2nKr";
            var secretKey = "TKbvQVmkCqMe0i_fLREgbLIlPg9olZ2LlE4tqX1r";
            var bucket = "uwptest";

            await Config.AutoZoneAsync(accessKey, bucket, true);

            var mac = new Mac(accessKey, secretKey);
            var putPolicy = new PutPolicy();
            putPolicy.Scope = bucket;
            putPolicy.SetExpires(30);
            putPolicy.DeleteAfterDays = 30;

            var token = Auth.CreateUploadToken(mac, putPolicy.ToJsonString());

            var formUploader = new FormUploader(true);

            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.FileTypeFilter.Add("*");
            var file = await picker.PickSingleFileAsync();

            var res = await formUploader.UploadFileAsync(file, Path.GetFileName(file.Path), token);

            if (res.Code == 200)
            {
                await file.DeleteAsync();
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var url = "http://ohaep8g7x.bkt.clouddn.com/bytes.txt";
            var fileKey = "bytes.txt";
            var data = "uwptest";
            var bucket = "uwptest";
            var ak = "";
            var sk = "";

            await Config.AutoZoneAsync(ak, bucket, true);

            var bucketManager = new BucketManager(new Mac(ak, sk));
            await bucketManager.DeleteAsync(bucket, fileKey);

            var putPolicy = new PutPolicy();
            putPolicy.Scope = bucket;
            putPolicy.SetExpires(60);
            
            var token = Auth.CreateUploadToken(new Mac(ak, sk), putPolicy.ToJsonString());

            var formUploader = new FormUploader();

            var res = (await formUploader.UploadDataAsync(Encoding.UTF8.GetBytes(data), fileKey, token)).Code == 200;

            if (res)
            {
                var cdnManager = new CdnManager(new Mac(ak, sk));
                await cdnManager.RefreshUrlsAsync(new[] { url });
            }
        }
    }
}
