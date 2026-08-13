using System;
using System.Text;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
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

            string szJson = jsonValue.ToString();

            string host = GetJsonValue(szJson, "ip");
            if (string.IsNullOrEmpty(host)) host = "127.0.0.1";

            int port = 25;
            int.TryParse(GetJsonValue(szJson, "port"), out port);
            if (port <= 0) port = 25;

            bool ssl = false;
            bool.TryParse(GetJsonValue(szJson, "ssl"), out ssl);

            string user = GetJsonValue(szJson, "user");
            string pass = GetJsonValue(szJson, "pass");
            string action = GetJsonValue(szJson, "action");
            string extraData = GetJsonValue(szJson, "data");

            int timeout = 15;
            int.TryParse(GetJsonValue(szJson, "timeout"), out timeout);
            if (timeout <= 0) timeout = 15;

            SmtpClientManager manager = new SmtpClientManager(host, port, ssl, user, pass, timeout);
            return manager.ProcessAction(action, extraData);
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

public class SmtpClientManager
{
    private string host;
    private int port;
    private bool ssl;
    private string user;
    private string pass;
    private int timeout;

    public SmtpClientManager(string host, int port, bool ssl, string user, string pass, int timeout)
    {
        this.host = host;
        this.port = port;
        this.ssl = ssl;
        this.user = user;
        this.pass = pass;
        this.timeout = timeout > 0 ? timeout : 15;
    }

    public string ProcessAction(string action, string extraData)
    {
        if (string.IsNullOrEmpty(action)) action = "test";

        try
        {
            using (SmtpClient smtpClient = new SmtpClient(host, port))
            {
                smtpClient.EnableSsl = ssl;
                smtpClient.Timeout = timeout * 1000;
                smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;

                if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass))
                {
                    smtpClient.Credentials = new NetworkCredential(user, pass);
                }
                else
                {
                    smtpClient.UseDefaultCredentials = false;
                }

                if (action.ToLower() == "test")
                {
                    using (TcpClient tcp = new TcpClient())
                    {
                        tcp.Connect(host, port);
                        using (NetworkStream stream = tcp.GetStream())
                        {
                            byte[] buffer = new byte[1024];
                            int read = stream.Read(buffer, 0, buffer.Length);
                            string greeting = Encoding.ASCII.GetString(buffer, 0, read);
                            return $"[+] SUCCESS_SMTP_CONNECTED\nServer Greeting: {greeting.Trim()}";
                        }
                    }
                }
                else if (action.ToLower() == "send")
                {
                    string from = ExtractValue(extraData, "from");
                    string to = ExtractValue(extraData, "to");
                    string subject = ExtractValue(extraData, "subject");
                    string body = ExtractValue(extraData, "body");

                    if (string.IsNullOrEmpty(from)) from = "admin@local.test";
                    if (string.IsNullOrEmpty(to)) return "[-] Failed to send: Recipient (to) is empty.";

                    using (MailMessage mail = new MailMessage(from, to, subject, body))
                    {
                        mail.IsBodyHtml = false;
                        smtpClient.Send(mail);
                        return $"[+] SUCCESS_MAIL_SENT -> Successfully sent test email to {to} via {host}:{port}";
                    }
                }
                else
                {
                    return "[-] ERROR: Unknown SMTP action.";
                }
            }
        }
        catch (Exception ex)
        {
            return "[-] SMTP Error: " + ex.Message;
        }
    }

    private string ExtractValue(string json, string key)
    {
        Match match = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"(.*?)\"");
        if (match.Success) return match.Groups[1].Value;
        return "";
    }
}