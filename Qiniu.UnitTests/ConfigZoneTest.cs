using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
using Qiniu.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Qiniu.UnitTests
{
    [TestClass]
    public class ConfigZoneTest : QiniuTestEnvars
    {
        [TestMethod]
        public void ZoneTest()
        {
            bool useHTTPS = false;

            Zone v1 = new Zone();
            Zone v2 = Zone.ZONE_CN_East(useHTTPS);
            Zone v3 = Zone.ZONE_CN_North(useHTTPS);
            Zone v4 = Zone.ZONE_CN_South(useHTTPS);
            Zone v5 = Zone.ZONE_US_North(useHTTPS);
        }

        [TestMethod]
        public async Task QueryZoneTest()
        {
            ZoneID zid = await ZoneHelper.QueryZoneAsync(AccessKey, Bucket1);
        }

        [TestMethod]
        public void SetZoneTest()
        {
            Config.SetZone(ZoneID.CN_East, false);
        }

        [TestMethod]
        public async Task AutoZoneTest()
        {
            await Config.AutoZoneAsync(AccessKey, Bucket1, false);
        }
    }
}
