import java.io.*;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public class payload {

    public payload() { }

    private String fnExtractJsonValue(String json, String key) {
        String pattern = "\"" + key + "\"\\s*:\\s*\"?([^\",}]+)\"?";
        Pattern r = Pattern.compile(pattern);
        Matcher m = r.matcher(json);
        if (m.find()) {
            return m.group(1).trim();
        }
        return "";
    }

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

            String dbType = fnExtractJsonValue(szJson, "dbtype");
            String host = fnExtractJsonValue(szJson, "ip");
            if (host == null || host.isEmpty()) {
                host = "127.0.0.1";
            }

            int port = 6379;
            try {
                String portStr = fnExtractJsonValue(szJson, "port");
                if (!portStr.isEmpty()) {
                    port = Integer.parseInt(portStr);
                }
            } catch (Exception ignored) {}

            if (port <= 0) {
                port = dbType.equalsIgnoreCase("mongodb") ? 27017 : 6379;
            }

            String user = fnExtractJsonValue(szJson, "user");
            String pass = fnExtractJsonValue(szJson, "pass");
            String action = fnExtractJsonValue(szJson, "action");
            String query = fnExtractJsonValue(szJson, "query");

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

            if (dbType.equalsIgnoreCase("redis")) {
                return processRedisAction(host, port, pass, timeout, action, query);
            } else if (dbType.equalsIgnoreCase("mongodb")) {
                return processMongoAction(host, port, user, pass, timeout, action, query);
            } else {
                return "[-] ERROR: Unsupported NoSQL database type.";
            }
        } catch (Exception e) {
            return "[-] ERROR: " + e.getMessage();
        }
    }

    private String processRedisAction(String host, int port, String pass, int timeout, String action, String query) {
        try {
            try (Socket socket = new Socket()) {
                int timeoutMs = timeout * 1000;
                socket.setSoTimeout(timeoutMs);
                socket.connect(new java.net.InetSocketAddress(host, port), timeoutMs);

                try (OutputStream out = socket.getOutputStream();
                     InputStream in = socket.getInputStream()) {

                    if (pass != null && !pass.isEmpty()) {
                        String authCmd = "*2\r\n$4\r\nAUTH\r\n$" + pass.length() + "\r\n" + pass + "\r\n";
                        out.write(authCmd.getBytes(StandardCharsets.UTF_8));
                        out.flush();

                        byte[] respBuffer = new byte[1024];
                        int read = in.read(respBuffer);
                        String authResp = new String(respBuffer, 0, read > 0 ? read : 0, StandardCharsets.UTF_8);
                        if (authResp.contains("-ERR")) {
                            return "[-] Redis Auth Failed: " + authResp;
                        }
                    }

                    if (action.equalsIgnoreCase("connect")) {
                        return "[+] SUCCESS_REDIS_CONNECTED -> Ready to send RESP commands.";
                    }

                    if (query == null || query.isEmpty()) {
                        query = "INFO";
                    }

                    String[] parts = query.trim().split("\\s+");
                    StringBuilder respBuilder = new StringBuilder();
                    respBuilder.append("*").append(parts.length).append("\r\n");
                    for (String part : parts) {
                        respBuilder.append("$").append(part.getBytes(StandardCharsets.UTF_8).length).append("\r\n").append(part).append("\r\n");
                    }

                    out.write(respBuilder.toString().getBytes(StandardCharsets.UTF_8));
                    out.flush();

                    byte[] buffer = new byte[8192];
                    int bytesRead = in.read(buffer);

                    return new String(buffer, 0, bytesRead > 0 ? bytesRead : 0, StandardCharsets.UTF_8);
                }
            }
        } catch (Exception ex) {
            return "[-] Redis Connection Error: " + ex.getMessage();
        }
    }

    private String processMongoAction(String host, int port, String user, String pass, int timeout, String action, String query) {
        try {
            try (Socket socket = new Socket()) {
                int timeoutMs = timeout * 1000;
                socket.setSoTimeout(timeoutMs);
                socket.connect(new java.net.InetSocketAddress(host, port), timeoutMs);

                try (OutputStream out = socket.getOutputStream();
                     InputStream in = socket.getInputStream()) {

                    if (action.equalsIgnoreCase("connect")) {
                        byte[] pingCommand = buildMongoCommand("admin", "isMaster", 1);
                        out.write(pingCommand);
                        out.flush();

                        byte[] buffer = new byte[4096];
                        int bytesRead = in.read(buffer);
                        if (bytesRead > 0) {
                            return "[+] SUCCESS_MONGO_CONNECTED -> Successfully connected to MongoDB server.";
                        }
                        return "[-] MongoDB Connection failed: No response received.";
                    }

                    if (query == null || query.isEmpty()) {
                        query = "db.stats()";
                    }

                    String dbName = (user == null || user.isEmpty()) ? "admin" : user;
                    byte[] cmdBytes = buildMongoCommand(dbName, "ping", 1);

                    if (query.contains("stats")) {
                        cmdBytes = buildMongoCommand(dbName, "dbStats", 1);
                    } else if (query.contains("listCollections")) {
                        cmdBytes = buildMongoCommand(dbName, "listCollections", 1);
                    }

                    out.write(cmdBytes);
                    out.flush();

                    byte[] respBuffer = new byte[8192];
                    int readBytes = in.read(respBuffer);

                    return "[+] MongoDB Command Executed Successfully:\n" + extractMongoText(respBuffer, readBytes);
                }
            }
        } catch (Exception ex) {
            return "[-] MongoDB Connection/Query Error: " + ex.getMessage();
        }
    }

    private byte[] buildMongoCommand(String dbName, String commandName, int commandValue) throws IOException {
        try (ByteArrayOutputStream ms = new ByteArrayOutputStream();
             DataOutputStream bw = new DataOutputStream(ms)) {

            bw.writeInt(0);
            bw.writeInt(Integer.reverseBytes(12345));
            bw.writeInt(Integer.reverseBytes(0));
            bw.writeInt(Integer.reverseBytes(2013));

            bw.writeInt(Integer.reverseBytes(0));
            byte[] dbBytes = (dbName + ".$cmd\0").getBytes(StandardCharsets.UTF_8);
            bw.write(dbBytes);

            bw.writeInt(Integer.reverseBytes(0));
            bw.writeInt(Integer.reverseBytes(1));

            try (ByteArrayOutputStream bsonMs = new ByteArrayOutputStream();
                 DataOutputStream bsonBw = new DataOutputStream(bsonMs)) {

                bsonBw.writeInt(0);
                bsonBw.writeByte(0x10);
                bsonBw.write(commandName.getBytes(StandardCharsets.UTF_8));
                bsonBw.writeByte(0);
                bsonBw.writeInt(Integer.reverseBytes(commandValue));
                bsonBw.writeByte(0);

                bsonBw.flush();
                byte[] bsonBytes = bsonMs.toByteArray();
                int bsonLen = bsonBytes.length;
                
                byte[] lenBytes = new byte[] {
                    (byte)(bsonLen & 0xFF),
                    (byte)((bsonLen >> 8) & 0xFF),
                    (byte)((bsonLen >> 16) & 0xFF),
                    (byte)((bsonLen >> 24) & 0xFF)
                };
                System.arraycopy(lenBytes, 0, bsonBytes, 0, 4);
                bw.write(bsonBytes);
            }

            bw.flush();
            byte[] totalBytes = ms.toByteArray();
            int totalLen = totalBytes.length;
            
            byte[] totalLenBytes = new byte[] {
                (byte)(totalLen & 0xFF),
                (byte)((totalLen >> 8) & 0xFF),
                (byte)((totalLen >> 16) & 0xFF),
                (byte)((totalLen >> 24) & 0xFF)
            };
            System.arraycopy(totalLenBytes, 0, totalBytes, 0, 4);

            return totalBytes;
        }
    }

    private String extractMongoText(byte[] buffer, int length) {
        try {
            StringBuilder sb = new StringBuilder();
            int limit = length > 0 ? length : 0;
            for (int i = 0; i < limit; i++) {
                char c = (char) buffer[i];
                if (c >= 32 && c <= 126) {
                    sb.append(c);
                } else if (c == '\n' || c == '\r' || c == '\t') {
                    sb.append(c);
                }
            }
            String result = sb.toString();
            return result.isEmpty() ? "[Raw BSON Binary Response Received]" : result;
        } catch (Exception e) {
            return "[+] MongoDB Operation Completed.";
        }
    }
}