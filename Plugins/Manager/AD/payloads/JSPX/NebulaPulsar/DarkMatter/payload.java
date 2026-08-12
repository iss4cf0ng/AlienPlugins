import java.util.*;
import java.util.regex.*;
import javax.naming.*;
import javax.naming.directory.*;

public class payload {

    public payload() {}

    public String execute(Object param) {
        if (!(param instanceof Map)) {
            return "ERROR: Invalid parameter type. Expected Map.";
        }
        
        @SuppressWarnings("unchecked")
        Map<String, Object> mapParam = (Map<String, Object>) param;
        
        Object jsonValue = mapParam.get("json");
        if (jsonValue == null || jsonValue.toString().isEmpty()) {
            return "ERROR: JSON data is empty.";
        }

        String szJson = jsonValue.toString();
        String server = fnGetJsonValue(szJson, "server");
        String portStr = fnGetJsonValue(szJson, "port");
        String username = fnGetJsonValue(szJson, "username");
        String password = fnGetJsonValue(szJson, "password");
        String baseDn = fnGetJsonValue(szJson, "basedn");
        String action = fnGetJsonValue(szJson, "action");

        if (baseDn == null || baseDn.isEmpty()) {
            baseDn = "DC=domain,DC=local";
        }

        try {
            String ldapUrl;
            if (server == null || server.isEmpty()) {
                ldapUrl = "ldap://" + baseDn;
            } else {
                int port = (portStr == null || portStr.isEmpty()) ? 389 : Integer.parseInt(portStr);
                ldapUrl = "ldap://" + server + ":" + port + "/" + baseDn;
            }

            Hashtable<String, String> env = new Hashtable<>();
            env.put(Context.INITIAL_CONTEXT_FACTORY, "com.sun.jndi.ldap.LdapCtxFactory");
            env.put(Context.PROVIDER_URL, ldapUrl);

            if (username != null && !username.isEmpty() && password != null) {
                env.put(Context.SECURITY_AUTHENTICATION, "simple");
                env.put(Context.SECURITY_PRINCIPAL, username);
                env.put(Context.SECURITY_CREDENTIALS, password);
            }

            DirContext ctx = new InitialDirContext(env);

            if ("bloodhound".equals(action)) {
                String result = executeBloodHoundCECollection(ctx, baseDn, "users");
                ctx.close();
                return result;
            }

            SearchControls searchControls = new SearchControls();
            searchControls.setSearchScope(SearchControls.SUBTREE_SCOPE);
            searchControls.setCountLimit(500);

            NamingEnumeration<SearchResult> results = ctx.search("", "(objectClass=*)", searchControls);

            Map<String, Object> rootNode = new LinkedHashMap<>();
            rootNode.put("name", baseDn);
            rootNode.put("type", "domain");

            Map<String, Object> attributes = new LinkedHashMap<>();
            attributes.put("distinguishedName", baseDn);
            rootNode.put("attributes", attributes);

            List<Object> children = new ArrayList<>();

            while (results.hasMore()) {
                SearchResult sr = results.next();
                Attributes attrs = sr.getAttributes();
                
                String dn = sr.getNameInNamespace();
                String cn = "Unknown";
                Attribute cnAttr = attrs.get("cn");
                if (cnAttr != null && cnAttr.get() != null) {
                    cn = cnAttr.get().toString();
                } else if (dn != null && !dn.isEmpty()) {
                    cn = dn;
                }

                String schemaClassName = "object";
                Attribute objectClassAttr = attrs.get("objectClass");
                if (objectClassAttr != null) {
                    for (int i = 0; i < objectClassAttr.size(); i++) {
                        String oc = objectClassAttr.get(i).toString().toLowerCase();
                        if (oc.contains("organizationalunit") || oc.contains("user") || oc.contains("computer")) {
                            schemaClassName = oc;
                            break;
                        }
                    }
                }

                String type = "object";
                if (schemaClassName.contains("organizationalunit")) type = "ou";
                else if (schemaClassName.contains("user")) type = "user";
                else if (schemaClassName.contains("computer")) type = "computer";

                Map<String, Object> childObj = new LinkedHashMap<>();
                childObj.put("name", cn);
                childObj.put("type", type);

                Map<String, Object> childAttrs = new LinkedHashMap<>();
                NamingEnumeration<? extends Attribute> allAttrs = attrs.getAll();
                while (allAttrs.hasMore()) {
                    Attribute attr = allAttrs.next();
                    String propName = attr.getID();
                    if (attr.size() > 0) {
                        Object val = attr.get(0);
                        String valStr = (val != null) ? val.toString() : "";
                        if (valStr.contains("System.__ComObject") || valStr.contains("System.Byte[]") || val instanceof byte[]) {
                            valStr = "[COM Object / Binary]";
                        }
                        childAttrs.put(propName, valStr);
                    }
                }
                childObj.put("attributes", childAttrs);
                children.add(childObj);
            }

            rootNode.put("children", children);
            ctx.close();

            Map<String, Object> responseObj = new LinkedHashMap<>();
            responseObj.put("status", "success");
            responseObj.put("mode", "live");
            responseObj.put("structure", rootNode);

            return "[+] SUCCESS\n" + serializeToJson(responseObj);

        } catch (Exception e) {
            return "[-] ERROR Details: " + e.getClass().getName() + " -> " + e.getMessage();
        }
    }

    private String executeBloodHoundCECollection(DirContext ctx, String baseDn, String targetType) {
        List<Object> items = new ArrayList<>();
        try {
            SearchControls searchControls = new SearchControls();
            searchControls.setSearchScope(SearchControls.SUBTREE_SCOPE);
            
            NamingEnumeration<SearchResult> results = ctx.search("", "(&(objectCategory=person)(objectClass=user))", searchControls);

            while (results.hasMore()) {
                try {
                    SearchResult res = results.next();
                    Attributes attrs = res.getAttributes();

                    Attribute samAttr = attrs.get("sAMAccountName");
                    Attribute dnAttr = attrs.get("distinguishedName");
                    Attribute sidAttr = attrs.get("objectSid");
                    Attribute uacAttr = attrs.get("userAccountControl");

                    String samName = (samAttr != null && samAttr.get() != null) ? samAttr.get().toString() : "";
                    String dn = (dnAttr != null && dnAttr.get() != null) ? dnAttr.get().toString() : "";
                    
                    byte[] sidBytes = null;
                    if (sidAttr != null && sidAttr.get() != null) {
                        Object sidVal = sidAttr.get();
                        if (sidVal instanceof byte[]) {
                            sidBytes = (byte[]) sidVal;
                        }
                    }

                    String objectSid = getSidString(sidBytes);
                    if (objectSid.isEmpty()) continue;

                    Map<String, Object> u = new LinkedHashMap<>();
                    u.put("ObjectIdentifier", objectSid);

                    Map<String, Object> props = new LinkedHashMap<>();
                    props.put("name", samName.toUpperCase() + "@" + baseDn.toUpperCase());
                    props.put("distinguishedname", dn);
                    
                    int uac = 0;
                    if (uacAttr != null && uacAttr.get() != null) {
                        try {
                            uac = Integer.parseInt(uacAttr.get().toString());
                        } catch (Exception ignored) {}
                    }
                    boolean enabled = ((uac & 2) != 2);
                    props.put("enabled", enabled);
                    props.put("domain", baseDn.toUpperCase());

                    u.put("Properties", props);
                    items.add(u);
                } catch (Exception ignored) {}
            }
        } catch (Exception ignored) {}

        Map<String, Object> metaObj = new LinkedHashMap<>();
        metaObj.put("methods", 127999);
        metaObj.put("type", targetType);
        metaObj.put("count", items.size());
        metaObj.put("version", 5);

        Map<String, Object> responseObj = new LinkedHashMap<>();
        responseObj.put("data", items);
        responseObj.put("meta", metaObj);

        return "[+] SUCCESS\n" + serializeToJson(responseObj);
    }

    private String getSidString(byte[] sidBytes) {
        if (sidBytes == null || sidBytes.length < 8) return "";
        try {
            int revision = sidBytes[0];
            int subAuthorityCount = sidBytes[1] & 0xFF;
            
            long identifierAuthority = 0;
            for (int i = 2; i < 8; i++) {
                identifierAuthority = (identifierAuthority << 8) + (sidBytes[i] & 0xFF);
            }

            StringBuilder sb = new StringBuilder();
            sb.append("S-").append(revision).append("-").append(identifierAuthority);

            for (int i = 0; i < subAuthorityCount; i++) {
                int offset = 8 + (i * 4);
                if (offset + 4 > sidBytes.length) break;
                long subAuthority = 0;
                for (int j = 3; j >= 0; j--) {
                    subAuthority = (subAuthority << 8) + (sidBytes[offset + j] & 0xFF);
                }
                sb.append("-").append(subAuthority);
            }
            return sb.toString();
        } catch (Exception e) {
            return "";
        }
    }

    private String fnGetJsonValue(String json, String key) {
        Pattern pattern = Pattern.compile("\"" + key + "\"\\s*:\\s*\"(.*?)\"");
        Matcher matcher = pattern.matcher(json);
        if (matcher.find()) {
            return matcher.group(1);
        }

        pattern = Pattern.compile("\"" + key + "\"\\s*:\\s*([^,\\}\\]]+)");
        matcher = pattern.matcher(json);
        if (matcher.find()) {
            return matcher.group(1).trim().replace("\"", "");
        }

        return "";
    }

    private String serializeToJson(Object obj) {
        if (obj instanceof Map) {
            @SuppressWarnings("unchecked")
            Map<String, Object> dict = (Map<String, Object>) obj;
            StringBuilder sb = new StringBuilder();
            sb.append("{");
            boolean first = true;
            for (Map.Entry<String, Object> entry : dict.entrySet()) {
                if (!first) sb.append(",");
                sb.append("\"").append(entry.getKey()).append("\":").append(serializeToJson(entry.getValue()));
                first = false;
            }
            sb.append("}");
            return sb.toString();
        } else if (obj instanceof List) {
            @SuppressWarnings("unchecked")
            List<Object> list = (List<Object>) obj;
            StringBuilder sb = new StringBuilder();
            sb.append("[");
            boolean first = true;
            for (Object item : list) {
                if (!first) sb.append(",");
                sb.append(serializeToJson(item));
                first = false;
            }
            sb.append("]");
            return sb.toString();
        } else if (obj instanceof String) {
            String str = (String) obj;
            String escaped = str.replace("\\", "\\\\").replace("\"", "\\\"");
            return "\"" + escaped + "\"";
        } else if (obj == null) {
            return "null";
        } else if (obj instanceof Boolean) {
            return ((Boolean) obj) ? "true" : "false";
        } else if (obj instanceof Number) {
            return obj.toString();
        } else {
            String escaped = obj.toString().replace("\\", "\\\\").replace("\"", "\\\"");
            return "\"" + escaped + "\"";
        }
    }
}