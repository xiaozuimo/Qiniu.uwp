using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
using Qiniu.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Qiniu.UnitTests
{
    /// <summary>
    /// Zone&Config
    /// </summary>
    [TestClass()]
    public class ZoneConfigTest
    {
        private TestContext testContextInstance;

        /// <summary>
        ///Gets or sets the test context which provides
        ///information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        /// <summary>
        /// A test for Zone
        /// </summary>
        [TestMethod()]
        public void zoneTest()
        {
            Zone v1 = new Zone();
            Zone v2 = Zone.ZONE_CN_East(true);
            Zone v3 = Zone.ZONE_CN_North(true);
            Zone v4 = Zone.ZONE_CN_South(true);
            Zone v5 = Zone.ZONE_US_North(true);
        }

        /// <summary>
        /// A test for AutoZone
        /// 设置AK和Bucket后通过测试
        /// </summary>
        [TestMethod()]
        public async Task autoZoneTest()
        {
            //Settings.load();
            await Settings.LoadFromFile();
            ZoneID zid = await Zone.getZone.Query(Settings.AccessKey, Settings.Bucket);
        }

        /// <summary>
        /// A test for Config
        /// </summary>
        [TestMethod()]
        public void configTest()
        {
            Config.ConfigZone(ZoneID.CN_East);
        }
    }
}
