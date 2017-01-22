# Qiniu SDK For Universal Windows Platform

##关于

Qiniu.uwp是根据七牛云C# SDK [v7.2.6 release](https://github.com/qiniu/csharp-sdk/releases/tag/7.2.6)版本修改而来。


由于7.2.x版本API变化较大，所以不再兼容先前的版本，新版本中网络请求换成了httpclient，之前uwp中webrequest无法重定向问题已经解决。与官方版本相比，Qiniu.uwp主要修改了无法在uwp中运行的代码，修改了官方版中函数命名，其余所有功能均与官方版一致。


新版本不再提供大文件下载的api，uwp中可以考虑使用后台文件传输或者使用httpclient+range回源或webrequest实现（当然如果有更好的办法也请告我！）


有关SDK的使用请参考官方文档，UnitTest、SampleApp暂不可用。


Copyright (c) 2017 [yunfan.me](https://yunfan.me/)
