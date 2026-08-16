import java.io.*;
import java.net.*;
import java.util.*;
import java.util.regex.*;
import javax.net.ssl.*;
import java.nio.charset.StandardCharsets;

public class payload {

    public payload() {}

    public String execute(Object param) {
        try {
            if (!(param instanceof Map)) {
                return "[-] ERROR: Invalid parameter type. Expected Map.";
            }
            
            @SuppressWarnings("unchecked")
            Map<String, Object> mapParam = (Map<String, Object>) param;
            
            Object jsonValue = mapParam.get("json");
            if (jsonValue == null || jsonValue.toString().isEmpty()) {
                return "[-] Missing parameter json";
            }

            String szJson = jsonValue.toString();

            String host = getJsonValue(szJson, "ip");
            if (host == null || host.isEmpty()) {
                host = "127.0.0.1";
            }

            int port = 25;
            try {
                String portStr = getJsonValue(szJson, "port");
                if (portStr != null && !portStr.isEmpty()) {
                    port = Integer.parseInt(portStr);
                }
            } catch (NumberFormatException ignored) {}
            if (port <= 0) port = 25;

            boolean ssl = false;
            try {
                String sslStr = getJsonValue(szJson, "ssl");
                if (sslStr != null && !sslStr.isEmpty()) {
                    ssl = Boolean.parseBoolean(sslStr);
                }
            } catch (Exception ignored) {}

            String user = getJsonValue(szJson, "user");
            String pass = getJsonValue(szJson, "pass");
            String action = getJsonValue(szJson, "action");
            String extraData = getJsonValue(szJson, "data");

            int timeout = 15;
            try {
                String timeoutStr = getJsonValue(szJson, "timeout");
                if (timeoutStr != null && !timeoutStr.isEmpty()) {
                    timeout = Integer.parseInt(timeoutStr);
                }
            } catch (NumberFormatException ignored) {}
            if (timeout <= 0) timeout = 15;

            return processSmtpAction(host, port, ssl, user, pass, timeout, action, extraData);
        } catch (Exception e) {
            return "[-] ERROR: " + e.getMessage();
        }
    }

    private String getJsonValue(String json, String key) {
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

    private String processSmtpAction(String host, int port, boolean ssl, String user, String pass, int timeout, String action, String extraData) {
        if (action == null || action.isEmpty()) {
            action = "test";
        }
        action = action.toLowerCase();

        try {
            if (action.equals("test")) {
                Socket socket = createSocket(host, port, ssl, timeout);
                try {
                    socket.setSoTimeout(timeout * 1000);
                    BufferedReader reader = new BufferedReader(new InputStreamReader(socket.getInputStream(), StandardCharsets.US_ASCII));
                    String greeting = reader.readLine();
                    if (greeting == null) greeting = "";
                    return "[+] SUCCESS_SMTP_CONNECTED\nServer Greeting: " + greeting.trim();
                } finally {
                    socket.close();
                }
            } else if (action.equals("send")) {
                String from = extractValue(extraData, "from");
                String to = extractValue(extraData, "to");
                String subject = extractValue(extraData, "subject");
                String body = extractValue(extraData, "body");

                if (from == null || from.isEmpty()) {
                    from = "admin@local.test";
                }
                if (to == null || to.isEmpty()) {
                    return "[-] Failed to send: Recipient (to) is empty.";
                }

                Socket socket = createSocket(host, port, ssl, timeout);
                try {
                    socket.setSoTimeout(timeout * 1000);
                    BufferedReader reader = new BufferedReader(new InputStreamReader(socket.getInputStream(), StandardCharsets.US_ASCII));
                    BufferedWriter writer = new BufferedWriter(new OutputStreamWriter(socket.getOutputStream(), StandardCharsets.US_ASCII));

                    // Read greeting
                    String resp = reader.readLine();
                    if (resp == null || !resp.startsWith("220")) {
                        return "[-] SMTP Error: Invalid greeting -> " + resp;
                    }

                    // EHLO
                    writer.write("EHLO localhost\r\n");
                    writer.flush();
                    readMultilineResponse(reader);

                    // Auth if credentials provided
                    if (user != null && !user.isEmpty() && pass != null) {
                        writer.write("AUTH LOGIN\r\n");
                        writer.flush();
                        resp = reader.readLine();
                        if (resp == null || !resp.startsWith("334")) {
                            return "[-] SMTP Error: AUTH LOGIN failed -> " + resp;
                        }

                        writer.write(Base64.getEncoder().encodeToString(user.getBytes(StandardCharsets.UTF_8)) + "\r\n");
                        writer.flush();
                        resp = reader.readLine();
                        if (resp == null || !resp.startsWith("334")) {
                            return "[-] SMTP Error: Username rejected -> " + resp;
                        }

                        writer.write(Base64.getEncoder().encodeToString(pass.getBytes(StandardCharsets.UTF_8)) + "\r\n");
                        writer.flush();
                        resp = reader.readLine();
                        if (resp == null || !resp.startsWith("235")) {
                            return "[-] SMTP Error: Authentication failed -> " + resp;
                        }
                    }

                    // MAIL FROM
                    writer.write("MAIL FROM:<" + from + ">\r\n");
                    writer.flush();
                    resp = reader.readLine();
                    if (resp == null || !resp.startsWith("250")) {
                        return "[-] SMTP Error: MAIL FROM failed -> " + resp;
                    }

                    // RCPT TO
                    writer.write("RCPT TO:<" + to + ">\r\n");
                    writer.flush();
                    resp = reader.readLine();
                    if (resp == null || (!resp.startsWith("250") && !resp.startsWith("251"))) {
                        return "[-] SMTP Error: RCPT TO failed -> " + resp;
                    }

                    // DATA
                    writer.write("DATA\r\n");
                    writer.flush();
                    resp = reader.readLine();
                    if (resp == null || !resp.startsWith("354")) {
                        return "[-] SMTP Error: DATA command failed -> " + resp;
                    }

                    // Send Email Content
                    writer.write("From: " + from + "\r\n");
                    writer.write("To: " + to + "\r\n");
                    writer.write("Subject: " + (subject != null ? subject : "") + "\r\n");
                    writer.write("Content-Type: text/plain; charset=UTF-8\r\n");
                    writer.write("\r\n");
                    writer.write((body != null ? body : "") + "\r\n");
                    writer.write(".\r\n");
                    writer.flush();

                    resp = reader.readLine();
                    if (resp == null || !resp.startsWith("250")) {
                        return "[-] SMTP Error: Message data rejected -> " + resp;
                    }

                    // QUIT
                    writer.write("QUIT\r\n");
                    writer.flush();

                    return "[+] SUCCESS_MAIL_SENT -> Successfully sent test email to " + to + " via " + host + ":" + port;
                } finally {
                    socket.close();
                }
            } else {
                return "[-] ERROR: Unknown SMTP action.";
            }
        } catch (Exception ex) {
            return "[-] SMTP Error: " + ex.getMessage();
        }
    }

    private Socket createSocket(String host, int port, boolean ssl, int timeout) throws Exception {
        if (ssl) {
            SSLSocketFactory factory = (SSLSocketFactory) SSLSocketFactory.getDefault();
            SSLSocket socket = (SSLSocket) factory.createSocket();
            socket.connect(new InetSocketAddress(host, port), timeout * 1000);
            return socket;
        } else {
            Socket socket = new Socket();
            socket.connect(new InetSocketAddress(host, port), timeout * 1000);
            return socket;
        }
    }

    private void readMultilineResponse(BufferedReader reader) throws IOException {
        String line;
        while ((line = reader.readLine()) != null) {
            if (line.length() >= 4 && line.charAt(3) == ' ') {
                break;
            }
        }
    }

    private String extractValue(String json, String key) {
        if (json == null || json.isEmpty()) return "";
        Pattern pattern = Pattern.compile("\"" + key + "\"\\s*:\\s*\"(.*?)\"");
        Matcher matcher = pattern.matcher(json);
        if (matcher.find()) {
            return matcher.group(1);
        }
        return "";
    }
}