using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace NeteaseReverseLadder
{
    class NeteaseProxy
    {
        private ProxyServer proxyServer;
        public ProxySelector ps;

        public NeteaseProxy(ProxySelector ps)
        {
            proxyServer = new ProxyServer();
            proxyServer.TrustRootCertificate = true;
            this.ps = ps;
            var timer = new System.Timers.Timer();
            timer.Elapsed += new ElapsedEventHandler(OnTimedEvent);
            timer.Interval = 5 * 60 * 1000;
            timer.Enabled = true;
        }

        private void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            lock(this)
                foreach (var pair in requests)
                {
                    if ((DateTime.Now - pair.Value.time).TotalSeconds > 30)
                        requests.Remove(pair.Key);
                }
        }

        public void StartProxy()
        {
            proxyServer.BeforeRequest += OnRequest;
            proxyServer.BeforeResponse += OnResponse;

            var explicitEndPoint = new ExplicitProxyEndPoint(IPAddress.Any, 15213, true)
            {
                // ExcludedHttpsHostNameRegex = new List<string>() { "google.com", "dropbox.com" }
            };

            proxyServer.AddEndPoint(explicitEndPoint);
            proxyServer.Start();

            foreach (var endPoint in proxyServer.ProxyEndPoints)
                Console.WriteLine("在 IP {0} 和端口 {1} 上开启代理服务器", endPoint.IpAddress, endPoint.Port);
        }

        public void Stop()
        {
            proxyServer.BeforeRequest -= OnRequest;
            proxyServer.BeforeResponse -= OnResponse;

            proxyServer.Stop();
        }

        //intecept & cancel, redirect or update requests
        public async Task OnRequest(object sender, SessionEventArgs e)
        {
            //Console.WriteLine("Request：" + e.WebSession.Request.Url + "Test=" + TestURL(e.WebSession.Request.Url));
            if (e.WebSession.Request.Url.Contains("music.163.com/eapi/song/enhance") || e.WebSession.Request.Url.Contains("music.163.com/eapi/song/like") || TestURL(e.WebSession.Request.Url))
            {
                Console.WriteLine("Request：" + e.WebSession.Request.Url);
                var request = new RequestInfo() {
                    body = await e.GetRequestBody(),
                    head = e.WebSession.Request.RequestHeaders,
                    time = DateTime.Now
                };
                lock (this)
                    requests.Add(e.WebSession.RequestId, request);
            }

        }
        class RequestInfo
        {
            public byte[] body;
            public Dictionary<string, HttpHeader> head;
            public DateTime time;
        }
        private Dictionary<Guid, RequestInfo> requests = new Dictionary<Guid, RequestInfo>();
        //Modify response
        public async Task OnResponse(object sender, SessionEventArgs e)
        {
            Console.WriteLine("Response：" + e.WebSession.Request.Url);
            //read response headers
            var responseHeaders = e.WebSession.Response.ResponseHeaders;
            if ((e.WebSession.Request.Method == "GET" || e.WebSession.Request.Method == "POST") && e.WebSession.Response.ResponseStatusCode == "200")
            {
                if (e.WebSession.Response.ContentType != null && (e.WebSession.Response.ContentType.Trim().ToLower().Contains("text") || e.WebSession.Response.ContentType.Trim().ToLower().Contains("json")) || e.WebSession.Request.Url.Contains("music.163.com/eapi/song") || TestURL(e.WebSession.Request.Url))
                {
                    string url = e.WebSession.Request.Url;
                    if (e.WebSession.Request.Url.Contains("music.163.com/eapi/song/enhance") || e.WebSession.Request.Url.Contains("music.163.com/eapi/song/like") || TestURL(e.WebSession.Request.Url))
                    {
                        Console.WriteLine("从代理服务器获取：" + e.WebSession.Request.Url);
                        //new added
                        var body = await e.GetResponseBodyAsString();
                        Console.WriteLine("Not modified: \n" + body);

                        var st = new Stopwatch();
                        st.Start();
                        await e.SetResponseBody(UseProxy(e));
                        st.Stop();
                        Console.WriteLine("修改完成，用时 " + st.ElapsedMilliseconds + " ms");
                        lock (this)
                            requests.Remove(e.WebSession.RequestId);
                    }
                    else if (e.WebSession.Request.Url.Contains("music.163.com/eapi/"))
                    {
                        var body = await e.GetResponseBodyAsString();
                        if (Regex.Match(body, "\"st\":-\\d+").Success)
                        {
                            Console.WriteLine("替换歌曲列表信息");
                            body = Regex.Replace(body, "\"st\":-\\d+", "\"st\":0");
                            body = body.Replace("\"pl\":0", "\"pl\":320000");
                            body = body.Replace("\"dl\":0", "\"dl\":320000");
                            body = body.Replace("\"fl\":0", "\"fl\":320000");
                            body = body.Replace("\"sp\":0", "\"sp\":7");
                            body = body.Replace("\"cp\":0", "\"cp\":1");
                            body = body.Replace("\"subp\":0", "\"subp\":1");
                            await e.SetResponseBodyString(body);
                        }
                    }
                   
                    else if (e.WebSession.Request.Url.Contains("vipdown/fcgi-bin/fcg_3g_song_list_rover.fcg")) {
                        string body = await e.GetResponseBodyAsString();
                        body = body.Replace("\"pay_status\":0", "\"pay_status\":1");
                        body = body.Replace("\"status\":0", "\"status\":1");
                        await e.SetResponseBodyString(body);
                        Console.WriteLine("修改成功：" + e.WebSession.Request.Url + "\n" + body);

                    }
                    else if (e.WebSession.Request.Url.Contains("vipmusic/fcgi-bin/fcg_vip_login.fcg"))
                    {
                        string body = await e.GetResponseBodyAsString();
                        body = body.Replace("\"viptype\":0", "\"viptype\":1");
                        await e.SetResponseBodyString(body);
                        Console.WriteLine("修改成功：" + e.WebSession.Request.Url + "\n" + body);
                    }
                    else if (url.Contains("qqmusic/fcgi-bin/qm_rplstingmus.fcg") || url.Contains("musichall/fcgi-bin/fcg_action_ctrl") || url.Contains("qqmusic/fcgi-bin/update_songinfo.fcg"))
                    {
                        string body = await e.GetResponseBodyAsString();
                        body = body.Replace("msgid=\"23\"", "msgid=\"0\"");
                        body = body.Replace("alert=\"0\"", "alert=\"2\"");
                        body = body.Replace("switch=\"1\"", "switch=\"3749695\"");
                        body = body.Replace("\"msgid\":23", "\"msgid\":0");
                        body = body.Replace("\"alert\":0", "\"alert\":2");
                        body = body.Replace("\"switch\":1", "\"switch\":3749695");
                        await e.SetResponseBodyString(body);
                        Console.WriteLine("修改成功：" + e.WebSession.Request.Url + "\n" + body);
                    }

                    else
                    {
                        if (TestURLBlock(e.WebSession.Request.Url))
                        {
                            Console.WriteLine("Blocked：" + e.WebSession.Request.Url);
                            await e.SetResponseBodyString("");
                            return;
                        }
                        var body = await e.GetResponseBodyAsString();
                        if (!TestURLDismiss(e.WebSession.Request.Url)) {
                            Console.WriteLine("未知链接：" + e.WebSession.Request.Url);
                            Console.WriteLine("Body: \n" + body);
                        }
                        await e.SetResponseBody(await e.GetResponseBody());
                    }
                        
                }
            }
            else
            {
                Console.WriteLine("获取失败：" + e.WebSession.Request.Url);
            }
        }
        private byte[] UseProxy(SessionEventArgs e)
        {
            var proxy = ps.GetTopProxies(1)[0];
            byte[] ret = null;
            try
            {
                using (var wc = new ImpatientWebClient())
                {
                    RequestInfo request;
                    lock (this)
                        request = requests[e.WebSession.RequestId];
                    requests.Remove(e.WebSession.RequestId);
                    wc.Proxy = new WebProxy(proxy.host, proxy.port);
                    foreach (var aheader in request.head)
                    {
                        var str = aheader.Key.ToLower();
                        if (str == "host" || str == "content-length" || str == "accept" || str == "user-agent" || str == "connection")
                            continue;
                        wc.Headers.Add(aheader.Key, aheader.Value.Value);
                    }
                    string removeHttpsUrl = e.WebSession.Request.Url.Replace("https://", "http://");
                    ret = wc.UploadData(removeHttpsUrl, request.body);
                }
                // new added
                string responseBody = System.Text.Encoding.UTF8.GetString(ret);
                Console.WriteLine("Modified：\n" + e.WebSession.Request.Url + "\n" + responseBody);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            return ret;

        }

        List<string> urlToBlock = new List<string>(new string[] { "x.jd.com"});

        List<string> urlToProxy = new List<string>(new string[] { "fefefwef" });

        List<string> urlToDismiss = new List<string>(new string[] { "v3/static/splash_pc.json", "qqmusic/fcgi-bin/qm_getudpinfo2.fcg", "base/fcgi-bin/fcg_unite_config.fcg",
            "fcgi-bin/a_player_stat.fcg", "fcgi-bin/fcg_access_moni.fcg", "fcgi-bin/fcg_get_advert.fcg", "3gmusic/fcgi-bin/3g_action_alter", "wk_v15/client/config/url.json",
            "base/fcgi-bin/fcg_supersound_config.fcg", "qqmusic/fcgi-bin/qm_autologin2.fcg", "fcgi-bin/fcg_access_moni.fcg", "fcgi-bin/fcg_qm_login_stat.fcg",
            "splcloud/fcgi-bin/fcg_get_deviceused.fcg", "folder/fcgi-bin/fcg_uniform_playlst_read.fcg", "rsc/fcgi-bin/3g_profile_homepage", "wcloud/fcgi-bin/fcg_get_downloadlist.fcg",
            "ext/fcgi-bin/fcg_web_access_stat.fcg", "fcgi-bin/fcg_datarpt.fcg", "fcgi-bin/reportmus.fcg", "base/fcgi-bin/wns_device_register.fcg", "node/pc/wk_v15/first.html",
            "wkframe/client/", "fcgi-bin/qm_search_photo.fcg", "base/fcgi-bin/fcg_global_comment_h5.fcg", "qqmusic/fcgi-bin/lyric_download.fcg"});
        

        private bool TestURL(string url) {
            foreach (string next_url in urlToProxy) {
                if (url.Contains(next_url))
                    return true;
            }
            return false;
        }

        private bool TestURLBlock(string url)
        {
            foreach (string next_url in urlToBlock)
            {
                if (url.Contains(next_url))
                    return true;
            }
            return false;

        }

        private bool TestURLDismiss(string url)
        {
            foreach (string next_url in urlToDismiss)
            {
                if (url.Contains(next_url))
                    return true;
            }
            return false;

        }

    }
}
