using System;
using System.Runtime.InteropServices;

namespace NeteaseReverseLadder
{
    class Program
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;

        static void Main(string[] args)
        {
            var handle = GetConsoleWindow();
            if (args.Length == 1 && args[0].Contains("-h"))
                // Hide
                ShowWindow(handle, SW_HIDE);
            else
                // Show
                ShowWindow(handle, SW_SHOW);

            start:
            var ps = new ProxySelector();
            if (!UpdateProxySelector(ps))
            {
                Console.WriteLine("获取代理列表失败");
                Console.ReadLine();
                return;
            }
            var proxy = new NeteaseProxy(ps);
            proxy.Start();
            Console.WriteLine("请设置网易云音乐代理为127.0.0.1，端口15213");
            Console.WriteLine("如果播放失败，按回车切换到下一个代理服务器");
            while (true)
            {
                var aproxy = ps.GetTop();
                if (aproxy == null)
                {
                    Console.WriteLine("没有可用代理，重新搜索");
                    proxy.Stop();
                    goto start;
                }
                Console.WriteLine("现在使用的是：" + aproxy);
                Console.ReadLine();
                ps.Remove(aproxy);
            }
        }
        static bool UpdateProxySelector(ProxySelector ps)
        {
            Console.WriteLine("获取代理列表");
            ps.UpdateList();
            Console.WriteLine("共" + ps.Proxies.Count + "条结果，测试速度");
            ps.UpdateLatency();
            return ps.Proxies.Count >= 1 && ps.Proxies[0].latency != int.MaxValue;
        }
    }
}
