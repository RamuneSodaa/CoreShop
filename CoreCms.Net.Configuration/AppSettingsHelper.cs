
using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Newtonsoft.Json.Linq;
using SqlSugar.Extensions;

namespace CoreCms.Net.Configuration
{
    /// <summary>
    /// 获取Appsettings配置信息
    /// </summary>
    public class AppSettingsHelper
    {
        static IConfiguration Configuration { get; set; }

        public AppSettingsHelper(string contentPath)
        {
            string Path = "appsettings.json";
            Configuration = new ConfigurationBuilder()
                .SetBasePath(contentPath)
                .Add(new JsonConfigurationSource { Path = Path, Optional = false, ReloadOnChange = true })
                .AddEnvironmentVariables()
                .Build();
        }

        /// <summary>
        /// 封装要操作的字符
        /// AppSettingsHelper.GetContent(new string[] { "JwtConfig", "SecretKey" });
        /// </summary>
        /// <param name="sections">节点</param>
        /// <returns></returns>
        public static string GetContent(params string[] sections)
        {
            try
            {

                if (sections.Any())
                {
                    return Configuration[string.Join(":", sections)];
                }
            }
            catch (Exception) { }

            return "";
        }



        /// <summary>
        /// 获取电脑 MAC（物理） 地址
        /// </summary>
        /// <param name="needToken">是否只是为了套取key生成一个不同部署环境不同的序列串</param>
        /// <returns></returns>
        public static string GetMACIp(bool needToken)
        {
            //本地计算机网络连接信息
            IPGlobalProperties computerProperties = IPGlobalProperties.GetIPGlobalProperties();
            //获取本机所有网络连接
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();

            //获取本机电脑名
            var HostName = computerProperties.HostName;
            //获取域名
            var DomainName = computerProperties.DomainName;

            if (nics == null || nics.Length < 1)
            {
                return "";
            }

            var MACIp = needToken ? HostName + DomainName : "";
            foreach (NetworkInterface adapter in nics)
            {
                var adapterName = adapter.Name;

                var adapterDescription = adapter.Description;
                var NetworkInterfaceType = adapter.NetworkInterfaceType;
                //if (adapterName == "本地连接" &&
                //    adapterDescription == "Realtek PCIe GBE Family Controller" &&
                //    NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                //{
                //    PhysicalAddress address = adapter.GetPhysicalAddress();
                //    byte[] bytes = address.GetAddressBytes();
                //    for (int i = 0; i < bytes.Length; i++)
                //    {
                //        MACIp += bytes[i].ToString("X2"); //以十六进制格式化
                //        if (i != bytes.Length - 1)
                //        {
                //            MACIp += "-";
                //        }
                //    }
                //}

                // 特别注释下：由于Framework和Core获取的网卡数据顺序不一致，导致MAC地址与原来的不一样
                // 修改新方案：获取有Dns数据的网卡
                var PIPProperties = adapter.GetIPProperties();
                if (PIPProperties.DnsAddresses != null && PIPProperties.DnsAddresses.Count > 0)
                {
                    //修改为获取第一个网卡地址
                    PhysicalAddress address = adapter.GetPhysicalAddress();
                    byte[] bytes = address.GetAddressBytes();
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        MACIp += bytes[i].ToString("X2"); //以十六进制格式化
                        if (i != bytes.Length - 1)
                        {
                            MACIp += "-";
                        }
                    }
                    break;
                }
            }

            return MACIp;
        }

        /// <summary>
        /// 根据mac获取随机key
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string GetMachineRandomKey(string str)
        {
            //var s = AppSettingsHelper.GetContent("JwtConfig", "SecretKey");
            var s = str + GetMACIp(true);
            using (var md5 = MD5.Create())
            {
                var result = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
                var strResult = BitConverter.ToString(result);
                return strResult.Replace("-", "");
            }
        }

        /// <summary>
        /// 获取主机名
        /// </summary>
        /// <returns></returns>
        public static string GetHostName()
        {
            return System.Net.Dns.GetHostName();
        }
    }
}
