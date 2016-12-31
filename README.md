# Qiniu SDK For Universal Windows Platform

##关于

Qiniu.uwp是根据七牛云C# SDK [v7.0.0.5 release](https://github.com/qiniu/csharp-sdk/releases/tag/v7.0.0.5)版本修改而来，已完成所有的测试工作。


####Nuget安装

``
Install-Package Qiniu.uwp
``


与官方版本同步，以后会在第一时间跟进


有关一个已知问题需要说明下，WinRT下WebRequest无法禁止重定向，在使用过程中请注意这个问题。暂时没有完全WinRT化打算


关于使用，可以参考[官方文档](https://github.com/qiniu/csharp-sdk/blob/master/README.md)或参考UnitTests，SampleApp暂不可用


Copyright (c) 2016 [yunfan.me](https://yunfan.me/)
