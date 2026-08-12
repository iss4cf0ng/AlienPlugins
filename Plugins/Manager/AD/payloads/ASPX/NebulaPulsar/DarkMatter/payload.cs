// payload.cs

using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.DirectoryServices;

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
        string server = fnGetJsonValue(szJson, "server");
        string portStr = fnGetJsonValue(szJson, "port");
        string username = fnGetJsonValue(szJson, "username");
        string password = fnGetJsonValue(szJson, "password");
        string baseDn = fnGetJsonValue(szJson, "basedn");
        string action = fnGetJsonValue(szJson, "action");

        if (string.IsNullOrEmpty(baseDn))
        {
            baseDn = "DC=domain,DC=local";
        }

        try
        {
            string ldapPath = string.IsNullOrEmpty(server) ? $"LDAP://{baseDn}" : $"LDAP://{server}:{portStr}/{baseDn}";
            
            DirectoryEntry entry;
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                entry = new DirectoryEntry(ldapPath, username, password, AuthenticationTypes.Secure);
            }
            else
            {
                entry = new DirectoryEntry(ldapPath);
            }

            DirectorySearcher searcher = new DirectorySearcher(entry);
            searcher.PageSize = 500;

            if (action == "bloodhound")
            {
                return ExecuteBloodHoundCECollection(searcher, baseDn, "users");
            }

            searcher.Filter = "(objectClass=*)";
            var rootNode = new Dictionary<string, object>();
            rootNode["name"] = baseDn;
            rootNode["type"] = "domain";
            
            var attributes = new Dictionary<string, object>();
            attributes["distinguishedName"] = baseDn;
            rootNode["attributes"] = attributes;

            List<object> children = new List<object>();

            foreach (SearchResult result in searcher.FindAll())
            {
                DirectoryEntry de = result.GetDirectoryEntry();
                string cn = de.Name ?? "Unknown";
                string schemaClassName = de.SchemaClassName?.ToLower() ?? "object";

                string type = "object";
                if (schemaClassName.Contains("organizationalunit")) type = "ou";
                else if (schemaClassName.Contains("user")) type = "user";
                else if (schemaClassName.Contains("computer")) type = "computer";

                var childObj = new Dictionary<string, object>();
                childObj["name"] = cn;
                childObj["type"] = type;

                var childAttrs = new Dictionary<string, object>();
                foreach (string propName in de.Properties.PropertyNames)
                {
                    if (de.Properties[propName].Count > 0)
                    {
                        object val = de.Properties[propName][0];
                        string valStr = val?.ToString() ?? "";
                        
                        if (valStr.Contains("System.__ComObject") || valStr.Contains("System.Byte[]"))
                        {
                            valStr = "[COM Object / Binary]";
                        }
                        
                        childAttrs[propName] = valStr;
                    }
                }
                childObj["attributes"] = childAttrs;
                children.Add(childObj);
            }

            rootNode["children"] = children;

            var responseObj = new Dictionary<string, object>();
            responseObj["status"] = "success";
            responseObj["mode"] = "live";
            responseObj["structure"] = rootNode;

            string jsonResult = SerializeToJson(responseObj);
            return "[+] SUCCESS\n" + jsonResult;
        }
        catch (Exception e)
        {
            return $"[-] ERROR Details: {e.GetType().FullName} -> {e.Message} | StackTrace: {e.StackTrace}";
        }
    }

    private string ExecuteBloodHoundCECollection(DirectorySearcher searcher, string baseDn, string targetType)
    {
        List<object> items = new List<object>();

        searcher.Filter = "(&(objectCategory=person)(objectClass=user))";
        foreach (SearchResult res in searcher.FindAll())
        {
            try
            {
                DirectoryEntry de = res.GetDirectoryEntry();
                var u = new Dictionary<string, object>();
                string samName = de.Properties["sAMAccountName"].Value?.ToString() ?? "";
                string dn = de.Properties["distinguishedName"].Value?.ToString() ?? "";
                string objectSid = GetSidString(de.Properties["objectSid"].Value);

                if (string.IsNullOrEmpty(objectSid)) continue;

                u["ObjectIdentifier"] = objectSid;
                
                var props = new Dictionary<string, object>();
                props["name"] = samName.ToUpper() + "@" + baseDn.ToUpper();
                props["distinguishedname"] = dn;
                props["enabled"] = !(((int)(de.Properties["userAccountControl"].Value ?? 0) & 2) == 2);
                props["domain"] = baseDn.ToUpper();
                
                u["Properties"] = props;
                items.Add(u);
            }
            catch { }
        }

        var metaObj = new Dictionary<string, object>();
        metaObj["methods"] = 127999;
        metaObj["type"] = targetType;
        metaObj["count"] = items.Count;
        metaObj["version"] = 5;

        var responseObj = new Dictionary<string, object>();
        responseObj["data"] = items;
        responseObj["meta"] = metaObj;

        return "[+] SUCCESS\n" + SerializeToJson(responseObj);
    }

    private string GetSidString(object sidBytesObj)
    {
        if (sidBytesObj == null) return "";
        try
        {
            if (sidBytesObj is byte[] sidBytes)
            {
                System.Security.Principal.SecurityIdentifier si = new System.Security.Principal.SecurityIdentifier(sidBytes, 0);
                return si.Value;
            }
        }
        catch { }
        return "";
    }

    private string fnGetJsonValue(string json, string key)
    {
        Match match = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"(.*?)\"");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        match = Regex.Match(json, $"\"{key}\"\\s*:\\s*([^,\\}}\\]]+)");
        if (match.Success)
        {
            return match.Groups[1].Value.Trim().Replace("\"", "");
        }

        return "";
    }

    private string SerializeToJson(object obj)
    {
        if (obj is Dictionary<string, object> dict)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first) sb.Append(",");
                sb.Append($"\"{kvp.Key}\":{SerializeToJson(kvp.Value)}");
                first = false;
            }
            sb.Append("}");
            return sb.ToString();
        }
        else if (obj is List<object> list)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            bool first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(",");
                sb.Append(SerializeToJson(item));
                first = false;
            }
            sb.Append("]");
            return sb.ToString();
        }
        else if (obj is string str)
        {
            string escaped = str.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }
        else if (obj == null)
        {
            return "null";
        }
        else if (obj is bool b)
        {
            return b ? "true" : "false";
        }
        else if (obj is int || obj is long || obj is double || obj is float || obj is decimal)
        {
            return obj.ToString();
        }
        else
        {
            string escaped = obj.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }
    }
}