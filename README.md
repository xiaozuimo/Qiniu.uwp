# Qiniu SDK For Universal Windows Platform

##关于

Qiniu.uwp是根据七牛云C# SDK [v7.2.8 release](https://github.com/qiniu/csharp-sdk/releases/tag/v7.2.8)版本修改而来。并且已经修复了[issue139](https://github.com/qiniu/csharp-sdk/issues/139)中提到的无法上传问题

另外因为官方版本已经融合了Qiniu.uwp，所以当前版本的Qiniu.uwp只是修改了官方版本中部分函数名小写的问题，未来如果官方版本解决函数名问题Qiniu.uwp应该就不会继续更新，只会编译官方版本并发布Nuget更新。

新版本不再提供大文件下载的api，uwp中可以考虑使用后台文件传输或者使用httpclient+range回源或webrequest实现（当然如果有更好的办法也请告我！）

有关SDK的使用请参考官方文档，UnitTest、SampleApp暂不可用。


Copyright (c) 2017 [yunfan.me](https://yunfan.me/)
